param([Parameter(Mandatory)][string]$Results)
$ErrorActionPreference = 'Stop'
$floor = [double](Get-Content (Join-Path $PSScriptRoot 'coverage-floor.txt') -Raw).Trim()
# Line-level union across all reports: a line (class filename + number) is covered if any report hit it.
# Summing per-report totals would double-count, because the App tests re-instrument the library projects.
$lines = @{}
foreach ($f in Get-ChildItem $Results -Recurse -Filter 'coverage.cobertura.xml') {
    [xml]$x = Get-Content $f.FullName -Raw
    foreach ($cls in $x.SelectNodes('//class')) {
        foreach ($l in $cls.SelectNodes('lines/line')) {
            $key = "$($cls.filename)|$($l.number)"
            $hit = [int]$l.hits -gt 0
            if (-not $lines.ContainsKey($key)) { $lines[$key] = $hit } elseif ($hit) { $lines[$key] = $true }
        }
    }
}
$valid = $lines.Count
if ($valid -eq 0) { Write-Host "::error::no coverage results under $Results"; exit 1 }
$covered = @($lines.Values | Where-Object { $_ }).Count
$pct = [math]::Round(100 * $covered / $valid, 1)
Write-Host "line coverage: $pct% ($covered/$valid distinct lines, floor $floor%)"
if ($pct -lt $floor) { Write-Host "::error::coverage $pct% is below the floor $floor%"; exit 1 }
exit 0
