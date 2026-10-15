# Renders docs/images/*.png from sample data with Avalonia.Headless (no desktop session needed; works on any OS).
# The renderer is the "Screenshots" test in tests/GhNotify.UI.Tests, skipped unless GHNOTIFY_SCREENSHOTS is set.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'docs/images'
New-Item -ItemType Directory -Force $out | Out-Null
$env:GHNOTIFY_SCREENSHOTS = $out
try {
    dotnet test (Join-Path $root 'tests/GhNotify.UI.Tests') -c Release --filter 'Category=Screenshots'
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
finally { Remove-Item Env:GHNOTIFY_SCREENSHOTS -ErrorAction SilentlyContinue }
# The review frames (every state, light and dark) are for the parity checklist, not the docs.
Remove-Item (Join-Path $out 'review') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'updated docs/images (sample data only)'
