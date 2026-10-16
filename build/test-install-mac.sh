#!/bin/bash
# macOS install / upgrade / uninstall test, CI only (it installs a real package with sudo).
# Usage: build/test-install-mac.sh <old-outdir> <new-outdir> <rid>
#   old/new are build/pack-mac.sh output folders (two versions of the same channel).
# Checks: the .pkg installs an ad-hoc signed app that prints its version and starts; config.json and state.json survive an
# update through Velopack's updater; the documented manual uninstall leaves nothing behind.
set -euo pipefail
OLD="$1"; NEW="$2"; RID="$3"
fail() { echo "::error::$*"; exit 1; }
step() { echo "== $*"; }

CFG="$HOME/Library/Application Support/GhNotify"
DATA="$HOME/Library/Application Support/GhNotify-data"
LOGS="$HOME/Library/Logs/GhNotify"

pkg_in() { find "$1" -maxdepth 1 -name "*$RID*.pkg" | head -1; }
full_in() { find "$1" -maxdepth 1 -name "*-$RID-full.nupkg" | sort | tail -1; }
app_path() {
  for d in /Applications "$HOME/Applications"; do
    for n in gh_notify GhNotify; do [ -d "$d/$n.app" ] && { echo "$d/$n.app"; return; }; done
  done
}
version_of() { "$1/Contents/MacOS/GhNotify" --version | tail -1; }
wait_started() {
  for _ in $(seq 1 40); do
    grep -qs "gh_notify started" "$LOGS"/*.log && return 0
    sleep 0.5
  done
  return 1
}
stop_app() { pkill -x GhNotify || true; sleep 1; }

OLD_PKG="$(pkg_in "$OLD")"; NEW_FULL="$(full_in "$NEW")"
[ -n "$OLD_PKG" ] || fail "no .pkg for $RID in $OLD"
[ -n "$NEW_FULL" ] || fail "no full .nupkg for $RID in $NEW"
rm -rf "$CFG" "$DATA" "$LOGS"

step "install $(basename "$OLD_PKG")"
sudo installer -pkg "$OLD_PKG" -target / >/dev/null
APP="$(app_path)"; [ -n "$APP" ] || fail "the .pkg did not install an app in /Applications or ~/Applications"
echo "installed at $APP"
codesign --verify --deep --strict "$APP" || fail "the app's signature does not verify"
codesign -dv "$APP" 2>&1 | grep -q "Signature=adhoc" || echo "::notice::the app is not ad-hoc signed (has a Developer ID?)"
OLD_VERSION="$(version_of "$APP")"; echo "version $OLD_VERSION"
[ -n "$OLD_VERSION" ] || fail "--version printed nothing"
test -f "$APP/Contents/MacOS/libGhNotifyMac.dylib" || fail "libGhNotifyMac.dylib is not in the bundle"

step "launch"
pkill -x GhNotify || true # the installer may have launched it
mkdir -p "$CFG"
echo '{"github":{"enabled":false},"ado":{"enabled":false},"filters":{"hide":["gh:fabrikam/sandbox"]}}' > "$CFG/config.json"
rm -rf "$LOGS"
open "$APP"
wait_started || { tail -50 "$LOGS"/*.log 2>/dev/null || true; fail "the app did not log 'gh_notify started'"; }
grep -qs "libGhNotifyMac could not be loaded" "$LOGS"/*.log && fail "the shim did not load in the installed app"
stop_app
# Seed state.json too, so the update has something to preserve (the app may not have written one yet).
[ -f "$DATA/state.json" ] || { mkdir -p "$DATA"; echo '{"themeMode":"Dark","textScale":1.15}' > "$DATA/state.json"; }
CFG_HASH="$(shasum "$CFG/config.json")"; STATE_HASH="$(shasum "$DATA/state.json")"

step "update to $(basename "$NEW_FULL")"
UPDATER=""
for n in UpdateMac Update; do [ -x "$APP/Contents/MacOS/$n" ] && UPDATER="$APP/Contents/MacOS/$n" && break; done
[ -n "$UPDATER" ] || { ls -la "$APP/Contents/MacOS"; fail "no Velopack updater in the bundle"; }
"$UPDATER" apply --package "$NEW_FULL" --norestart --silent || fail "the updater exited $?"
APP="$(app_path)"; NEW_VERSION="$(version_of "$APP")"; echo "version $NEW_VERSION"
[ "$NEW_VERSION" != "$OLD_VERSION" ] || fail "the version did not change after the update ($NEW_VERSION)"
[ "$(shasum "$CFG/config.json")" = "$CFG_HASH" ] || fail "config.json changed during the update"
[ "$(shasum "$DATA/state.json")" = "$STATE_HASH" ] || fail "state.json changed during the update"
rm -rf "$LOGS"
open "$APP"
wait_started || fail "the updated app did not start"
stop_app

step "uninstall (the documented manual steps)"
sudo rm -rf "$APP"
rm -rf "$CFG" "$DATA" "$LOGS"
[ -z "$(app_path)" ] || fail "an app is still installed after uninstall"
pkgutil --pkgs | grep -i ghnotify | while read -r id; do sudo pkgutil --forget "$id" >/dev/null; done
echo "install / upgrade / uninstall ok ($OLD_VERSION -> $NEW_VERSION)"
