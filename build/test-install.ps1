# Install -> upgrade -> uninstall test. CI ONLY: it installs and uninstalls the real per-user install
# (%LocalAppData%\GhNotify) and writes sentinel files into the real profile. Do not run it on a dev machine.
param(
    [Parameter(Mandatory)][string]$OldSetup,
    [Parameter(Mandatory)][string]$NewFeed,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'GhNotify'
$updateExe = Join-Path $installRoot 'Update.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\GhNotify.lnk'
$realConfig = Join-Path $env:APPDATA 'GhNotify\config.json'
$realState = Join-Path $env:LOCALAPPDATA 'GhNotify-data\state.json'

function Fail($msg) { Write-Host "::error::$msg"; Write-Error $msg; exit 1 }
function Step($msg) { Write-Host "== $msg" }

# This script writes into the real profile and replaces the real install: CI or explicit -Force only.
if (-not $Force) {
    if ($env:CI -ne 'true') { Fail "refusing to run outside CI; rerun with -Force if you really mean it" }
    if (Test-Path $updateExe) { Fail "gh_notify is already installed here; rerun with -Force to replace it" }
    if (Get-Process GhNotify -ErrorAction SilentlyContinue) { Fail "gh_notify is running; quit it or rerun with -Force" }
}
Get-Process GhNotify -ErrorAction SilentlyContinue | Stop-Process -Force

# Set before anything launches the app so the app launches never touch a real config or data dir.
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("ghn-it-" + [guid]::NewGuid().ToString('N'))
$env:GHNOTIFY_CONFIG_DIR = Join-Path $tmp 'config'
$env:GHNOTIFY_DATA_DIR = Join-Path $tmp 'data'
New-Item -ItemType Directory -Force $env:GHNOTIFY_CONFIG_DIR, $env:GHNOTIFY_DATA_DIR | Out-Null
$config = Join-Path $env:GHNOTIFY_CONFIG_DIR 'config.json'
Set-Content $config '{ "pollSeconds": 300, "github": { "enabled": false }, "ado": { "enabled": false } }' -NoNewline
$configHash = (Get-FileHash $config).Hash
$exe = Join-Path $installRoot 'current\GhNotify.exe'   # stable path (spike notes)

# Sentinels in the REAL config/data locations: install, update and uninstall must leave them untouched.
$sentinels = @{}   # path -> hash, only for files this script created
foreach ($s in @($realConfig, $realState)) {
    if (-not (Test-Path $s)) {
        New-Item -ItemType Directory -Force (Split-Path $s) | Out-Null
        Set-Content $s '{ "sentinel": true }' -NoNewline
        $sentinels[$s] = (Get-FileHash $s).Hash
    }
}
function Assert-Sentinels($when) {
    foreach ($s in $sentinels.Keys) {
        if (-not (Test-Path $s)) { Fail "sentinel $s was deleted $when" }
        if ((Get-FileHash $s).Hash -ne $sentinels[$s]) { Fail "sentinel $s changed $when" }
    }
}

# Prefer the supported Update.exe apply path; pick the highest-versioned full package in the new feed.
function Parse-PkgVersion($file) {
    if ($file.Name -notmatch '^GhNotify-(.+)-full\.nupkg$') { return $null }
    try { return [System.Management.Automation.SemanticVersion]::Parse($Matches[1]) } catch { return $null }
}
$fullPkg = Get-ChildItem $NewFeed -Filter '*-full.nupkg' |
    Where-Object { Parse-PkgVersion $_ } |
    Sort-Object { Parse-PkgVersion $_ } -Descending | Select-Object -First 1
if (-not $fullPkg) { Fail "no GhNotify-<version>-full.nupkg found in $NewFeed" }

function Wait-Until([scriptblock]$cond, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $cond) { return }
        Start-Sleep -Milliseconds 500
    }
    Fail "timed out after $seconds s waiting for $what"
}
function Wait-Started([int]$seconds = 20) {
    Wait-Until {
        $log = Get-ChildItem (Join-Path $env:GHNOTIFY_DATA_DIR 'logs') -ErrorAction SilentlyContinue | Get-Content -ErrorAction SilentlyContinue
        $log -match 'gh_notify started'
    } $seconds "app to log 'gh_notify started'"
}
function Version-Of {
    $v = (& $exe --version | Out-String).Trim()
    if (-not $v) { $v = ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion + '').Trim() }
    if (-not $v) { Fail "could not determine the app version from --version or the exe ProductVersion" }
    $v
}
function Start-Check {
    Remove-Item (Join-Path $env:GHNOTIFY_DATA_DIR 'logs') -Recurse -ErrorAction SilentlyContinue
    $p = Start-Process $exe -PassThru
    try { Wait-Started } finally { Stop-Process $p.Id -Force -ErrorAction SilentlyContinue }
}

try {
    Step "1. install old"
    & $OldSetup --silent | Out-Null          # silent setup does not launch the app
    Wait-Until { Test-Path $exe } 60 "stable exe $exe"
    Get-Process GhNotify -ErrorAction SilentlyContinue | Stop-Process -Force
    $old = Version-Of
    if (-not (Test-Path $shortcut)) { Fail "Start-menu shortcut missing" }
    # Run key is written by our OnAfterInstallFastCallback hook.
    $run = (Get-ItemProperty $runKey -Name GhNotify -ErrorAction SilentlyContinue).GhNotify
    if ($run -ne "`"$exe`"") { Fail "Run key is '$run', expected '`"$exe`"'" }
    Assert-Sentinels "by install"
    Start-Check

    Step "2. update to new ($($fullPkg.Name))"
    & $updateExe apply --package $fullPkg.FullName --norestart --silent | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "Update.exe apply exited $LASTEXITCODE" }
    Wait-Until { (& $exe --version | Out-String).Trim() -ne $old } 60 "version to change from $old"
    Get-Process GhNotify -ErrorAction SilentlyContinue | Stop-Process -Force
    $new = Version-Of
    if ($new -eq $old) { Fail "version did not change after update (still $old)" }
    if ((Get-FileHash $config).Hash -ne $configHash) { Fail "config.json changed during update" }
    $run = (Get-ItemProperty $runKey -Name GhNotify -ErrorAction SilentlyContinue).GhNotify
    if ($run -ne "`"$exe`"") { Fail "Run key wrong after update: '$run'" }
    Assert-Sentinels "by update"
    Start-Check

    Step "3. uninstall"
    & $updateExe --uninstall --silent | Out-Null
    Wait-Until { -not (Test-Path $installRoot) } 30 "install root $installRoot to be removed"
    if (Test-Path $exe) { Fail "app still present after uninstall" }
    if (Get-ItemProperty $runKey -Name GhNotify -ErrorAction SilentlyContinue) { Fail "Run key still present after uninstall" }
    if (Test-Path $shortcut) { Fail "shortcut still present after uninstall" }
    Assert-Sentinels "by uninstall"
    if (-not (Test-Path $config)) { Fail "temp config.json was deleted" }

    Write-Host "install/upgrade/uninstall: PASS ($old -> $new)"
}
finally {
    foreach ($s in $sentinels.Keys) { Remove-Item $s -Force -ErrorAction SilentlyContinue }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
