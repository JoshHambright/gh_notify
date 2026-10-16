#!/bin/bash
# Packs gh_notify for macOS with Velopack: one .pkg, portable zip and update feed per RID, each on its own channel
# (osx-arm64, osx-x64) so Windows and the two Mac builds never see each other's updates.
# Usage: build/pack-mac.sh <version> <outdir> [--prev <dir>] [--notes <file>] [rid...]   (default rids: osx-arm64 osx-x64)
# Needs macOS with the .NET 10 SDK, the Xcode command line tools and vpk (dotnet tool install -g vpk).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$1"; OUT="$2"; shift 2
PREV=""; NOTES=""; RIDS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --prev) PREV="$2"; shift 2 ;;
    --notes) NOTES="$2"; shift 2 ;;
    *) RIDS+=("$1"); shift ;;
  esac
done
[ ${#RIDS[@]} -eq 0 ] && RIDS=(osx-arm64 osx-x64)
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

"$ROOT/native/mac/build.sh"

for RID in "${RIDS[@]}"; do
  PUB="$ROOT/artifacts/publish-mac/$VERSION/$RID"
  rm -rf "$PUB"
  dotnet publish "$ROOT/src/GhNotify.App.Mac" -c Release -r "$RID" --self-contained -p:Version="$VERSION" -o "$PUB"
  test -f "$PUB/libGhNotifyMac.dylib" || { echo "::error::libGhNotifyMac.dylib missing from $PUB"; exit 1; }

  # The app icon, rendered by the app itself (the same artwork as the Windows .ico).
  ICONSET="$ROOT/artifacts/publish-mac/$VERSION/GhNotify.iconset"
  rm -rf "$ICONSET"
  if [ "$RID" = "osx-$( [ "$(uname -m)" = arm64 ] && echo arm64 || echo x64 )" ]; then
    "$PUB/GhNotify" --write-iconset "$ICONSET"
  else
    # Can't run the other architecture's executable without Rosetta; render with dotnet run instead.
    dotnet run --project "$ROOT/src/GhNotify.App.Mac" -c Release -- --write-iconset "$ICONSET"
  fi
  ICNS="$ROOT/artifacts/publish-mac/$VERSION/GhNotify.icns"
  iconutil -c icns "$ICONSET" -o "$ICNS"

  CHANNEL_OUT="$OUT"
  if [ -n "$PREV" ] && [ -d "$PREV" ]; then
    # Previous releases of this channel let vpk build delta packages.
    find "$PREV" -maxdepth 1 -type f \( -name "*-$RID-*.nupkg" -o -name "*-$RID-full.nupkg" -o -name "releases.$RID.json" \) \
      -exec cp {} "$CHANNEL_OUT/" \;
  fi
  # vpk copies a custom Info.plist verbatim, so it carries every key vpk would write (OsxBundleCommandRunner) plus ours:
  # LSUIElement (menu-bar app, no Dock icon), LSMinimumSystemVersion (the shim needs 13) and NSAppleEventsUsageDescription
  # (without it macOS refuses the osascript calls that open Terminal or iTerm, error -1743, and never asks).
  PLIST="$ROOT/artifacts/publish-mac/$VERSION/Info-$RID.plist"
  cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>gh_notify</string>
  <key>CFBundleDisplayName</key><string>gh_notify</string>
  <key>CFBundleExecutable</key><string>GhNotify</string>
  <key>CFBundleIdentifier</key><string>com.joshhambright.ghnotify</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION%%-*}</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleSignature</key><string>????</string>
  <key>CFBundleIconFile</key><string>GhNotify.icns</string>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSUIElement</key><true/>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSAppleEventsUsageDescription</key><string>gh_notify opens Terminal or iTerm in a repo folder when you choose an agent action such as Open in Claude.</string>
</dict>
</plist>
PLIST
  plutil -lint "$PLIST"
  NOTES_ARGS=()
  [ -n "$NOTES" ] && [ -f "$NOTES" ] && NOTES_ARGS=(--releaseNotes "$NOTES")
  # Ad-hoc signing of the whole bundle ("-" is codesign's ad-hoc identity; without it only the executable is signed and the bundle
  # does not verify). SIGNING: replace "-" with a Developer ID and add --signInstallIdentity / --notaryProfile when one exists.
  vpk pack --packId GhNotify --packTitle gh_notify --packVersion "$VERSION" --packDir "$PUB" --mainExe GhNotify \
    --icon "$ICNS" --plist "$PLIST" --signAppIdentity "-" --bundleId com.joshhambright.ghnotify --channel "$RID" --runtime "$RID" --outputDir "$CHANNEL_OUT" \
    ${NOTES_ARGS[@]+"${NOTES_ARGS[@]}"}
done
ls -la "$OUT"
