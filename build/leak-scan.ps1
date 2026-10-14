param([string]$TermsFile)
$ErrorActionPreference = 'Stop'
$raw = if ($TermsFile) { Get-Content $TermsFile -Raw } else { $env:LEAK_TERMS }
$terms = @(($raw ?? '') -split "`r?`n" | ForEach-Object Trim | Where-Object { $_ })
if ($terms.Count -eq 0) { Write-Host "::notice::leak scan skipped (no LEAK_TERMS)"; exit 0 }
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$binary = '\.(png|ico|wav|ttf|dll|exe|zip|nupkg)$'
$hits = 0
foreach ($file in (git -C $root ls-files)) {
    if ($file -match $binary) { continue }
    $lines = @(Get-Content (Join-Path $root $file) -ErrorAction SilentlyContinue)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($t in $terms) {
            if ($lines[$i].IndexOf($t, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Write-Host "${file}:$($i + 1)"; $hits++ }
        }
    }
}
foreach ($c in (git -C $root log --all --format='%H')) {
    $msg = (git -C $root log -1 --format="%B%n%an%n%ae" $c) -join "`n"
    foreach ($t in $terms) { if ($msg -and $msg.IndexOf($t, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Write-Host "commit $c"; $hits++ } }
}
if ($hits -gt 0) { Write-Host "::error::leak scan: $hits hit(s) (terms not shown)"; exit 1 }
Write-Host "leak scan: clean ($($terms.Count) terms)"
exit 0
