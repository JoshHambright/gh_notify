# Install -> upgrade -> uninstall test. CI ONLY: it installs and uninstalls the real per-user install
# (%LocalAppData%\GhNotify), which would delete a developer's legacy data. Do not run it on a dev machine.
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

function Fail($msg) { Write-Host "::error::$msg"; Write-Error $msg; exit 1 }
function Step($msg) { Write-Host "== $msg" }

# Never clobber a developer's real install or running app.
if (-not $Force) {
    if (Test-Path $updateExe) { Fail "gh_notify is already installed here; rerun with -Force to replace it" }
    if (Get-Process GhNotify -ErrorAction SilentlyContinue) { Fail "gh_notify is running; quit it or rerun with -Force" }
}
Get-Process GhNotify -ErrorAction SilentlyContinue | Stop-Process -Force

# Set before anything launches the app so the test never touches a real config or data dir.
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("ghn-it-" + [guid]::NewGuid().ToString('N'))
$env:GHNOTIFY_CONFIG_DIR = Join-Path $tmp 'config'
$env:GHNOTIFY_DATA_DIR = Join-Path $tmp 'data'
New-Item -ItemType Directory -Force $env:GHNOTIFY_CONFIG_DIR, $env:GHNOTIFY_DATA_DIR | Out-Null
$config = Join-Path $env:GHNOTIFY_CONFIG_DIR 'config.json'
Set-Content $config '{ "pollSeconds": 300, "github": { "enabled": false }, "ado": { "enabled": false } }' -NoNewline
$configHash = (Get-FileHash $config).Hash
$exe = Join-Path $installRoot 'current\GhNotify.exe'   # stable path (spike notes)

# Prefer the supported Update.exe apply path; pick the full package from the new feed.
$fullPkg = Get-ChildItem $NewFeed -Filter '*-full.nupkg' | Select-Object -First 1
if (-not $fullPkg) { Fail "no *-full.nupkg found in $NewFeed" }

function Wait-Started([int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $log = Get-ChildItem (Join-Path $env:GHNOTIFY_DATA_DIR 'logs') -ErrorAction SilentlyContinue | Get-Content -ErrorAction SilentlyContinue
        if ($log -match 'gh_notify started') { return }
        Start-Sleep -Milliseconds 500
    }
    Fail "app did not log 'gh_notify started' within $seconds s"
}
function Version-Of { (& $exe --version | Out-String).Trim() }
function Start-Check {
    Remove-Item (Join-Path $env:GHNOTIFY_DATA_DIR 'logs') -Recurse -ErrorAction SilentlyContinue
    $p = Start-Process $exe -PassThru
    try { Wait-Started } finally { Stop-Process $p.Id -Force -ErrorAction SilentlyContinue }
}

try {
    Step "1. install old"
    & $OldSetup --silent | Out-Null          # silent setup does not launch the app
    Start-Sleep 3
    Get-Process GhNotify -ErrorAction SilentlyContinue | Stop-Process -Force
    if (-not (Test-Path $exe)) { Fail "stable exe missing: $exe" }
    $old = Version-Of
    if (-not (Test-Path $shortcut)) { Fail "Start-menu shortcut missing" }
    # Run key is written by our OnAfterInstallFastCallback hook.
    $run = (Get-ItemProperty $runKey -Name GhNotify -ErrorAction SilentlyContinue).GhNotify
    if ($run -ne "`"$exe`"") { Fail "Run key is '$run', expected '`"$exe`"'" }
    Start-Check

    Step "2. update to new"
    & $updateExe apply --package $fullPkg.FullName --norestart --silent | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "Update.exe apply exited $LASTEXITCODE" }
    Start-Sleep 5
    Get-Process GhNotify -ErrorAction SilentlyContinue | Stop-Process -Force
    $new = Version-Of
    if ($new -eq $old) { Fail "version did not change after update (still $old)" }
    if ((Get-FileHash $config).Hash -ne $configHash) { Fail "config.json changed during update" }
    $run = (Get-ItemProperty $runKey -Name GhNotify -ErrorAction SilentlyContinue).GhNotify
    if ($run -ne "`"$exe`"") { Fail "Run key wrong after update: '$run'" }
    Start-Check

    Step "3. uninstall"
    & $updateExe --uninstall --silent | Out-Null
    Start-Sleep 3
    if (Test-Path $exe) { Fail "app still present after uninstall" }
    if (Get-ItemProperty $runKey -Name GhNotify -ErrorAction SilentlyContinue) { Fail "Run key still present after uninstall" }
    if (Test-Path $shortcut) { Fail "shortcut still present after uninstall" }
    if (-not (Test-Path $config)) { Fail "config.json was deleted by uninstall" }

    Write-Host "install/upgrade/uninstall: PASS ($old -> $new)"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
