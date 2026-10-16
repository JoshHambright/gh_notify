#!/bin/bash
# Builds native/mac/out/libGhNotifyMac.dylib: universal (arm64 + x86_64), macOS 13+, ad-hoc signed.
# Needs the Xcode command line tools (swiftc, lipo, codesign). GhNotify.Platform.Mac.csproj copies the result when it exists.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p out
for arch in arm64 x86_64; do
  swiftc -emit-library -O -module-name GhNotifyMac -target "$arch-apple-macos13.0" \
    -Xlinker -install_name -Xlinker @rpath/libGhNotifyMac.dylib \
    -o "out/libGhNotifyMac-$arch.dylib" GhNotifyMac.swift
done
lipo -create -output out/libGhNotifyMac.dylib out/libGhNotifyMac-arm64.dylib out/libGhNotifyMac-x86_64.dylib
rm out/libGhNotifyMac-arm64.dylib out/libGhNotifyMac-x86_64.dylib
codesign --force --sign - out/libGhNotifyMac.dylib
lipo -info out/libGhNotifyMac.dylib
