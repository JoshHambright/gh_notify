$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("ghn-shots-" + [guid]::NewGuid().ToString('N'))
dotnet build (Join-Path $root 'src/GhNotify.App') -c Release | Out-Null
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$exe = Join-Path $root 'src/GhNotify.App/bin/Release/net10.0-windows10.0.19041.0/GhNotify.exe'
& $exe --render-preview $tmp | Out-Null
$out = Join-Path $root 'docs/images'
New-Item -ItemType Directory -Force $out | Out-Null
$map = @{
    'popup-dark-expanded.png'          = 'hero-dark.png'
    'popup-dark-collapsed.png'         = 'popup-dark.png'
    'popup-light-collapsed.png'        = 'popup-light.png'
    'popup-dark-confirm-approval.png'  = 'approve.png'
    'popup-dark-filters.png'           = 'filters.png'
    'settings-workflows-dark.png'      = 'settings-workflows.png'
    'settings-notifications-light.png' = 'settings-notifications.png'
    'tray-32-zero.png'                 = 'tray-calm.png'
    'tray-32-count.png'                = 'tray-waiting.png'
}
$copied = 0
foreach ($k in $map.Keys) {
    $src = Join-Path $tmp $k
    if (Test-Path $src) { Copy-Item $src (Join-Path $out $map[$k]) -Force; $copied++ }
    else { Write-Warning "preview not produced, skipped: $k" }
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "updated $copied of $($map.Count) images in docs/images (sample data only)"
