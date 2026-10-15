<#
.SYNOPSIS
  Builds a release's notes, or checks that a release PR has them.

.DESCRIPTION
  Hand-written notes live in docs/releases/vX.Y.Z.md (see docs/releases/TEMPLATE.md). Links in them are relative to that
  folder, so they work when the file is browsed on GitHub; this script rewrites them to the release tag so the GitHub
  release page and the update package link to the docs as they were at that version.

  Compose: the notes file, then "## All changes" and the release-please changelog. Without a notes file the changelog
  alone is used, with a warning.
    ./build/release-notes.ps1 -Tag v2.0.0 -Changelog artifacts/changelog.md -Out artifacts/notes.md

  Check (CI on release-please PRs): fails unless docs/releases/v<manifest version>.md exists.
    ./build/release-notes.ps1 -Check
#>
[CmdletBinding(DefaultParameterSetName = 'Compose')]
param(
    [Parameter(ParameterSetName = 'Compose', Mandatory)] [string]$Tag,
    [Parameter(ParameterSetName = 'Compose')] [string]$Changelog,
    [Parameter(ParameterSetName = 'Compose', Mandatory)] [string]$Out,
    [Parameter(ParameterSetName = 'Compose')] [string]$Repo = $env:GITHUB_REPOSITORY,
    [Parameter(ParameterSetName = 'Check', Mandatory)] [switch]$Check
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$releases = Join-Path $root 'docs/releases'

# Everything above this marker in a published body is our notes; below it is the changelog. Re-running a release reads
# the body back, so this keeps the notes from being stacked twice.
$marker = '<!-- changelog -->'

if ($Check) {
    $version = (Get-Content (Join-Path $root '.release-please-manifest.json') -Raw | ConvertFrom-Json).'.'
    $file = Join-Path $releases "v$version.md"
    if (-not (Test-Path $file)) {
        Write-Host "::error file=docs/releases/v$version.md::Release notes for v$version are missing. Copy docs/releases/TEMPLATE.md to docs/releases/v$version.md, fill it in and merge it before this release PR."
        exit 1
    }
    $text = Get-Content $file -Raw
    if ($text -match '\{\{|TODO') {
        Write-Host "::error file=docs/releases/v$version.md::docs/releases/v$version.md still has template placeholders ({{…}} or TODO)."
        exit 1
    }
    Write-Host "release notes found: docs/releases/v$version.md"
    exit 0
}

if (-not $Repo) { $Repo = 'JoshHambright/gh_notify' }

function Rewrite-Links([string]$md) {
    # [text](../../README.md) -> the repo root at the tag.
    $md = [regex]::Replace($md, '(?<!!)\[([^\]]*)\]\(\.\./\.\./([^)\s]+)\)', { param($m) "[$($m.Groups[1].Value)](https://github.com/$Repo/blob/$Tag/$($m.Groups[2].Value))" })
    # ![alt](../images/x.png) -> raw file at the tag, so the image renders on the release page.
    $md = [regex]::Replace($md, '!\[([^\]]*)\]\(\.\./([^)\s]+)\)', { param($m) "![$($m.Groups[1].Value)](https://raw.githubusercontent.com/$Repo/$Tag/docs/$($m.Groups[2].Value))" })
    # [text](../actions.md#merge) -> the doc at the tag.
    $md = [regex]::Replace($md, '(?<!!)\[([^\]]*)\]\(\.\./([^)\s]+)\)', { param($m) "[$($m.Groups[1].Value)](https://github.com/$Repo/blob/$Tag/docs/$($m.Groups[2].Value))" })
    return $md
}

$changes = ''
if ($Changelog -and (Test-Path $Changelog)) { $changes = (Get-Content $Changelog -Raw).Trim() }
$i = $changes.IndexOf($marker)
if ($i -ge 0) { $changes = $changes.Substring($i + $marker.Length).Trim() }

$file = Join-Path $releases "$Tag.md"
if (Test-Path $file) {
    $notes = Rewrite-Links ((Get-Content $file -Raw).Trim())
    # The file's own "# gh_notify vX" title duplicates the release name on GitHub.
    $notes = ($notes -replace '\A#\s[^\r\n]*\r?\n+', '').Trim()
    $body = if ($changes) { "$notes`n`n## All changes`n`n$marker`n$changes" } else { $notes }
} else {
    Write-Host "::warning::no docs/releases/$Tag.md; the release notes are the changelog only"
    $body = $changes
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
Set-Content $Out $body.Trim()
Write-Host "wrote $Out ($((Get-Item $Out).Length) bytes)"
