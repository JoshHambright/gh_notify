#!/bin/bash
# macOS check, round 2: install the packaged app and walk through it by hand. See docs/macos-testing.md.
# Usage, from the repo root:  ./build/mac-test.sh --round 2 <path-to.pkg>
# Writes mac-test-r2-<timestamp>.zip in the repo root. Send that one file back.
# Uses scratch config and data folders with sample items and both sources off: it never touches a real gh_notify setup
# and needs no GitHub or Azure DevOps login. Works with the bash 3.2 that ships with macOS.

set -u
PKG="${1:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$ROOT/mac-test-r2-$STAMP"
SCRATCH="$OUT/scratch"
mkdir -p "$OUT/logs" "$SCRATCH/config" "$SCRATCH/data"

if [ -z "$PKG" ] || [ ! -f "$PKG" ]; then
  echo "Usage: ./build/mac-test.sh --round 2 <path-to.pkg>   (the .pkg from the gh_notify-macos-arm64 artifact)"
  exit 1
fi

app_path() {
  for d in /Applications "$HOME/Applications"; do
    for n in gh_notify GhNotify; do [ -d "$d/$n.app" ] && { echo "$d/$n.app"; return; }; done
  done
}

echo "==> Installing $(basename "$PKG") (macOS asks for your password)"
sudo installer -pkg "$PKG" -target / 2>&1 | sed "s|$HOME|~|g" > "$OUT/logs/install.log"
APP="$(app_path)"
if [ -z "$APP" ]; then echo "    FAILED: no app was installed. See logs/install.log"; fi
pkill -x GhNotify 2>/dev/null; sleep 1

# Sample setup: both sources off, two review requests in the saved snapshot, so the menu-bar icon turns red.
echo '{"github":{"enabled":false},"ado":{"enabled":false}}' > "$SCRATCH/config/config.json"
cat > "$SCRATCH/data/state.json" <<'EOF'
{"snapshot":{"GitHub":[
 {"key":"gh:review:acme/api#12","provider":"gitHub","kind":"reviewRequested","repo":"acme/api","title":"Add retry with jitter to the payment client","url":"https://github.com/acme/api/pull/12","author":"alice","updatedAt":"2026-10-16T07:00:00+00:00","headSha":"abc1234","changedLines":40},
 {"key":"gh:review:contoso/web#31","provider":"gitHub","kind":"reviewRequested","repo":"contoso/web","title":"Update dependency versions","url":"https://github.com/contoso/web/pull/31","author":"bob","updatedAt":"2026-10-16T04:00:00+00:00","headSha":"def5678","changedLines":12}
]}}
EOF

ANSWERS="$OUT/answers.txt"
: > "$ANSWERS"
# ask "<id>" "<question>": records "id: y|n | note".
ask() {
  local a note
  printf '\n%s\n  [y/n] ' "$2"
  read -r a
  printf '  Anything to add? (Enter to skip) '
  read -r note
  echo "$1: ${a:-?} | $note" >> "$ANSWERS"
}

if [ -n "$APP" ]; then
  echo "==> Starting gh_notify from $APP (a test notification follows after about 5 seconds, and a Terminal window after about 10)"
  GHNOTIFY_CONFIG_DIR="$SCRATCH/config" GHNOTIFY_DATA_DIR="$SCRATCH/data" \
    "$APP/Contents/MacOS/GhNotify" --test-notification --test-terminal > "$OUT/logs/stdout.log" 2>&1 &
  sleep 6

  ask launched     "Did gh_notify start without an error or an 'unidentified developer' block? (If macOS blocked it: System Settings → Privacy & Security → Open Anyway, then answer n and describe it.)"
  ask permission   "Did macOS ask to allow notifications for gh_notify, and did you allow it?"
  ask notification "Did a 'gh_notify test notification' appear? (If you allowed it late, it may not have.)"
  ask click        "Click the notification (in Notification Center if it is gone). Did the gh_notify popup open?"
  ask menubar      "Is the gh_notify icon in the menu bar, red with a 2 badge (two items are waiting)?"
  ask popup        "Click the menu-bar icon. Does the popup open just below it at the top right, showing two review requests?"
  ask hotkey       "Press Control+Option+G twice. Does the popup open, then close?"
  ask theme        "System Settings → Appearance: switch Light/Dark. Does the popup follow?"
  ask settings     "Popup ⋯ → Settings… → General: turn 'Open at login' on. Did it stay on (or show a hint about approving it)? Turn it off again."
  ask folder       "Settings → Advanced → Open logs folder. Did Finder open a folder?"
  ask terminal     "About 10 seconds after starting, did a Terminal window open showing 'gh_notify terminal test'? (macOS may first ask to let gh_notify control Terminal: allow it.)"
  ask quit         "Popup ⋯ → Quit. Did gh_notify quit (icon gone)?"
  pkill -x GhNotify 2>/dev/null
else
  echo "app-installed: n | no app found after installing" >> "$ANSWERS"
fi

# Logs and system details; home paths become ~.
cp "$SCRATCH/data/logs/"*.log "$OUT/logs/" 2>/dev/null
for f in "$OUT/logs/"*.log; do [ -f "$f" ] && sed -i '' "s|$HOME|~|g" "$f"; done
{
  echo "{"
  echo "  \"schema\": 1,"
  echo "  \"round\": 2,"
  echo "  \"package\": \"$(basename "$PKG")\","
  echo "  \"app\": \"$(echo "${APP:-}" | sed "s|$HOME|~|g")\","
  echo "  \"version\": \"$([ -n "${APP:-}" ] && "$APP/Contents/MacOS/GhNotify" --version 2>/dev/null | tail -1)\","
  echo "  \"utc\": \"$STAMP\","
  echo "  \"macos\": \"$(sw_vers -productVersion)\","
  echo "  \"arch\": \"$(uname -m)\","
  echo "  \"owner\": \"$([ -n "${APP:-}" ] && stat -f %Su "$APP")\","
  echo "  \"signature\": \"$([ -n "${APP:-}" ] && codesign -dv "$APP" 2>&1 | grep -o 'Signature=[a-z]*' | head -1)\""
  echo "}"
} > "$OUT/summary.json"
rm -rf "$SCRATCH"

echo
cat "$ANSWERS"
(cd "$ROOT" && zip -qr "mac-test-r2-$STAMP.zip" "mac-test-r2-$STAMP")
echo
echo "Done. Send back: $ROOT/mac-test-r2-$STAMP.zip"
echo "gh_notify stays installed. To remove it: quit it, delete $APP, and delete"
echo "~/Library/Application Support/GhNotify-data and ~/Library/Logs/GhNotify if they exist."
