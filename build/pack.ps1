param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$PrevReleases,
    [string]$ReleaseNotes
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$pub = Join-Path $root "artifacts/publish/$Version"
dotnet publish (Join-Path $root 'src/GhNotify.App') -c Release -r win-x64 --self-contained -p:Version=$Version -o $pub
if ($LASTEXITCODE) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$prevFiles = if ($PrevReleases -and (Test-Path $PrevReleases)) { @(Get-ChildItem $PrevReleases -File) } else { @() }
if ($prevFiles.Count) { Copy-Item $prevFiles.FullName $OutDir -Force } # lets vpk build deltas
$notesArgs = if ($ReleaseNotes -and (Test-Path $ReleaseNotes)) { @("--releaseNotes", (Resolve-Path $ReleaseNotes).Path) } else { @() }
vpk pack --packId GhNotify --packVersion $Version --packDir $pub --mainExe GhNotify.exe `
    --icon (Join-Path $root "src/GhNotify.App/app.ico") --shortcuts StartMenuRoot --outputDir $OutDir @notesArgs
# SIGNING: add --signParams / Trusted Signing options here when a certificate exists.
exit $LASTEXITCODE
