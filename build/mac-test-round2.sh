#!/bin/bash
# macOS check, round 2: install the packaged app and walk through it by hand. See docs/macos-testing.md.
# Usage, from the repo root:  ./build/mac-test.sh --round 2 <path-to.pkg>
# Writes mac-test-r2-<timestamp>.zip in the repo root. Send that one file back.
# Uses scratch config and data folders, so it never touches a real gh_notify setup. GitHub is on only if `gh` is signed in. Works with the bash 3.2 that ships with macOS.

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

# The package must come from the commit this checkout is on: every CI build is called 0.0.2-ci, so an older download looks the same.
HEAD_SHA="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null)"
APP_SHA="$([ -n "$APP" ] && "$APP/Contents/MacOS/GhNotify" --commit 2>/dev/null | tail -1)"
echo "    Installed build: commit ${APP_SHA:-unknown}; this checkout: ${HEAD_SHA:-unknown}"
if [ -n "$APP" ] && [ "${APP_SHA:-x}" != "${HEAD_SHA:-y}" ]; then
  echo
  echo "    This .pkg is not from the commit you are on. Download the newest gh_notify-macos-arm64 artifact from the PR"
  echo "    (Checks → CI → Artifacts), delete the old download first, and run this again with the new .pkg."
  printf '    Test this package anyway? [y/N] '
  read -r go
  [ "$go" = y ] || exit 1
fi

# Settings in a scratch folder. GitHub is on when `gh` is signed in, so the inbox shows your real items (nothing is sent
# anywhere; gh_notify only reads). Azure DevOps stays off. Without gh the inbox is empty and the icon stays plain.
if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then GITHUB=true; else GITHUB=false; fi
echo "{\"github\":{\"enabled\":$GITHUB},\"ado\":{\"enabled\":false}}" > "$SCRATCH/config/config.json"
echo "    GitHub source: $GITHUB (gh signed in: $GITHUB)"

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
  # `open` launches it the way Finder does, so macOS treats it as the app (started directly, it may not ask for notifications).
  open -n "$APP" --env GHNOTIFY_CONFIG_DIR="$SCRATCH/config" --env GHNOTIFY_DATA_DIR="$SCRATCH/data" \
    --stdout "$OUT/logs/stdout.log" --stderr "$OUT/logs/stdout.log" --args --test-notification --test-terminal
  sleep 6

  ask launched     "Did gh_notify start without an error or an 'unidentified developer' block? (If macOS blocked it: System Settings → Privacy & Security → Open Anyway, then answer n and describe it.)"
  ask permission   "Did macOS ask to allow notifications for gh_notify, and did you allow it?"
  ask notification "Did a 'gh_notify test notification' appear? (If you allowed it late, it may not have.)"
  ask notifsettings "Open System Settings → Notifications. Is gh_notify in the list with notifications allowed? (Add what you see: not listed / listed but off / on.)"
  ask click       "Click the notification (in Notification Center if it is gone). Did the gh_notify popup open?"
  ask terminal     "About 10 seconds after starting, did a Terminal window open showing 'gh_notify terminal test'? (macOS may first ask to let gh_notify control Terminal: allow it.)"
  ask menubar      "Is the gh_notify icon in the menu bar and sharp (not blurry)? With GitHub on and something waiting, is it red with a count?"
  ask menu         "Click the menu-bar icon. Does a menu open with Open, Settings…, What's new, Check for updates and Quit?"
  ask popup        "Choose Open. Does the popup appear at the top right, under the menu bar, and stay open?"
  ask hotkey       "Click somewhere else so the popup closes, then press Control+Option+G twice. Does the popup open, then close?"
  ask theme        "Open the popup again, then System Settings → Appearance: switch Light/Dark. Does the popup follow?"
  ask settings     "Menu-bar icon → Settings… → General: turn 'Open at login' on. Did it stay on (or show a hint about approving it)? Turn it off again."
  ask folder       "Settings → Advanced → Open logs folder. Did Finder open a folder?"
  ask quit         "Menu-bar icon → Quit. Did gh_notify quit (icon gone)?"
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
  echo "  \"commit\": \"${APP_SHA:-}\","
  echo "  \"checkout\": \"${HEAD_SHA:-}\","
  echo "  \"utc\":\"$STAMP\","
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
