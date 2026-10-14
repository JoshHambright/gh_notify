param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$PrevReleases
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$pub = Join-Path $root "artifacts/publish/$Version"
dotnet publish (Join-Path $root 'src/GhNotify.App') -c Release -r win-x64 --self-contained -p:Version=$Version -o $pub
if ($LASTEXITCODE) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force $OutDir | Out-Null
if ($PrevReleases -and (Test-Path $PrevReleases)) { Copy-Item (Join-Path $PrevReleases '*') $OutDir -Force } # lets vpk build deltas
vpk pack --packId GhNotify --packVersion $Version --packDir $pub --mainExe GhNotify.exe `
    --icon (Join-Path $root 'src/GhNotify.App/app.ico') --shortcuts StartMenuRoot --outputDir $OutDir
# SIGNING: add --signParams / Trusted Signing options here when a certificate exists.
exit $LASTEXITCODE
