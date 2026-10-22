#!/bin/bash
# macOS check for gh_notify (round 1: build, headless tests, screenshots). See docs/macos-testing.md.
# Usage, from the repo root:  ./build/mac-test.sh [git-ref]      (default ref: main)
#                             ./build/mac-test.sh --round 2 <pkg>   (round 2: build/mac-test-round2.sh)
# Writes mac-test-<timestamp>.zip in the repo root. Send that one file back.
# Works with the bash 3.2 that ships with macOS; needs only git, the .NET 10 SDK and the standard tools.

set -u
# Round 2 (install the packaged app and walk through it) lives in its own script.
if [ "${1:-}" = "--round" ] && [ "${2:-}" = "2" ]; then exec "$(dirname "$0")/mac-test-round2.sh" "${3:-}"; fi
REF="${1:-main}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$ROOT/mac-test-$STAMP"
mkdir -p "$OUT/logs" "$OUT/test-results" "$OUT/screenshots"
cd "$ROOT" || exit 1

STEPS=""   # "name=exitcode" lines for summary.json

# Runs one step, logs its full output, and keeps going on failure. Home paths are replaced with ~ in every log.
step() {
  local name="$1"; shift
  echo "==> $name"
  "$@" 2>&1 | sed "s|$HOME|~|g" > "$OUT/logs/$name.log"
  local code=${PIPESTATUS[0]}
  STEPS="$STEPS$name=$code"$'\n'
  if [ "$code" -eq 0 ]; then echo "    ok"; else echo "    FAILED (exit $code), see logs/$name.log"; fi
}

# Counters from a .trx file: total passed failed skipped (all 0 when the file is missing).
trx_counts() {
  local f="$1"
  if [ ! -f "$f" ]; then echo "0 0 0 0"; return; fi
  local line; line="$(grep -o '<Counters [^>]*>' "$f" | head -1)"
  local t p f2 s
  t="$(echo "$line" | sed -n 's/.* total="\([0-9]*\)".*/\1/p')"
  p="$(echo "$line" | sed -n 's/.* passed="\([0-9]*\)".*/\1/p')"
  f2="$(echo "$line" | sed -n 's/.* failed="\([0-9]*\)".*/\1/p')"
  s="$(echo "$line" | sed -n 's/.* notExecuted="\([0-9]*\)".*/\1/p')"
  echo "${t:-0} ${p:-0} ${f2:-0} ${s:-0}"
}

# Names of failed tests in a .trx file, one per line.
trx_failures() {
  [ -f "$1" ] || return 0
  grep -o '<UnitTestResult [^>]*outcome="Failed"[^>]*>' "$1" | sed -n 's/.*testName="\([^"]*\)".*/\1/p'
}

json_escape() { sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\r' | awk 'BEGIN{ORS="\\n"} {print}'; }

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found. Install the .NET 10 SDK (10.0.400 or later) from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again."
  exit 1
fi

step checkout     git -c advice.detachedHead=false checkout "$REF"
step environment  sh -c 'sw_vers; echo "arch: $(uname -m)"; echo "git: $(git rev-parse HEAD)"; dotnet --info'
step build        dotnet build tests/GhNotify.UI.Tests/GhNotify.UI.Tests.csproj -c Release -warnaserror
step ui-tests     dotnet test tests/GhNotify.UI.Tests/GhNotify.UI.Tests.csproj -c Release --no-build \
                    --logger "trx;LogFileName=ui.trx" --results-directory "$OUT/test-results"
step core-tests   dotnet test tests/GhNotify.Tests/GhNotify.Tests.csproj -c Release \
                    --logger "trx;LogFileName=core.trx" --results-directory "$OUT/test-results"
step screenshots  env GHNOTIFY_SCREENSHOTS="$OUT/screenshots" \
                    dotnet test tests/GhNotify.UI.Tests/GhNotify.UI.Tests.csproj -c Release --no-build --filter "Category=Screenshots"

# summary.json: the same shape on every machine, so results can be compared run to run.
read -r UT UP UF US <<< "$(trx_counts "$OUT/test-results/ui.trx")"
read -r CT CP CF CS <<< "$(trx_counts "$OUT/test-results/core.trx")"
SHOTS="$(ls "$OUT/screenshots" 2>/dev/null | grep -c '\.png$')"
{
  echo "{"
  echo "  \"schema\": 1,"
  echo "  \"ref\": \"$REF\","
  echo "  \"commit\": \"$(git rev-parse HEAD 2>/dev/null)\","
  echo "  \"utc\": \"$STAMP\","
  echo "  \"macos\": \"$(sw_vers -productVersion)\","
  echo "  \"arch\": \"$(uname -m)\","
  echo "  \"dotnet\": \"$(dotnet --version 2>/dev/null)\","
  echo "  \"steps\": {"
  printf '%s' "$STEPS" | awk -F= 'NF==2 {a[n++]=sprintf("    \"%s\": %s", $1, $2)} END {for (i=0;i<n;i++) printf "%s%s\n", a[i], (i<n-1?",":"")}'
  echo "  },"
  echo "  \"uiTests\":   { \"total\": $UT, \"passed\": $UP, \"failed\": $UF, \"skipped\": $US, \"failures\": \"$(trx_failures "$OUT/test-results/ui.trx" | json_escape)\" },"
  echo "  \"coreTests\": { \"total\": $CT, \"passed\": $CP, \"failed\": $CF, \"skipped\": $CS, \"failures\": \"$(trx_failures "$OUT/test-results/core.trx" | json_escape)\" },"
  echo "  \"screenshots\": $SHOTS"
  echo "}"
} > "$OUT/summary.json"

# notes.md: the one part a person fills in.
cat > "$OUT/notes.md" <<'EOF'
# Screenshot review

Open each PNG in screenshots/ next to the file with the same name in docs/images/ (the Windows reference).
For each item, write "ok" or describe what is different.

- Text cut off, overlapping or wrapping differently:
- Wrong or fallback font (should match Windows, not Helvetica/Times):
- Missing icons or glyphs (⋯, chevrons, empty boxes):
- Colours differ between light and dark:
- Blurry rendering:
- Anything else:
EOF

echo
cat "$OUT/summary.json"
echo
echo "Now open $OUT/screenshots, fill in $OUT/notes.md, then press Enter to make the zip."
read -r _
(cd "$ROOT" && zip -qr "mac-test-$STAMP.zip" "mac-test-$STAMP")
echo "Done. Send back: $ROOT/mac-test-$STAMP.zip"
