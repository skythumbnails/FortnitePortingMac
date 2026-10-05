#!/bin/bash
# Builds FortnitePorting.app and its dmg for Apple Silicon from this repo.
#
#   macos/build-dmg.sh [output-dir]          (default: macos/out)
#
# DOTNET=/path/to/dotnet picks the SDK (.NET 10). Build from an APFS volume: on exFAT, macOS writes
# "._*" files the compilers trip over.
set -euo pipefail

# the real path: MSBuild treats /tmp/... and /private/tmp/... as two projects and builds both at once (CS0006)
ROOT="$(cd "$(dirname "$0")/.." && pwd -P)"
OUT="${1:-$ROOT/macos/out}"
DOTNET="${DOTNET:-dotnet}"
VERSION="$(sed -n 's#.*<Version>\(.*\)</Version>.*#\1#p' "$ROOT/src/FortnitePorting/FortnitePorting.csproj" | head -1)"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# _EnableMacOSCodeSign=false: the hosts are signed (or not) below, on purpose
"$DOTNET" publish "$ROOT/src/FortnitePorting/FortnitePorting.csproj" -c Release -r osx-arm64 --self-contained true \
    -p:PublishSingleFile=false -p:_EnableMacOSCodeSign=false -o "$WORK/publish"

APP="$WORK/stage/FortnitePorting.app"
CONTENTS="$APP/Contents"
RUNTIME="$CONTENTS/Resources/runtime"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"
ditto "$WORK/publish" "$RUNTIME"

# two hosts, see macos/launcher.c
cp "$RUNTIME/FortnitePorting" "$RUNTIME/FortnitePorting-unsigned"
codesign --remove-signature "$RUNTIME/FortnitePorting-unsigned" 2>/dev/null || true
clang -O2 -arch arm64 -mmacosx-version-min=11.0 -o "$CONTENTS/MacOS/FortnitePortingLauncher" "$ROOT/macos/launcher.c"
sed -e "s/@VERSION@/$VERSION/g" "$ROOT/macos/Info.plist" > "$CONTENTS/Info.plist"
cp "$ROOT/macos/AppIcon.icns" "$CONTENTS/Resources/"
find "$WORK/stage" \( -name '._*' -o -name '.DS_Store' \) -delete

# ad-hoc sign every Mach-O in the runtime except the unsigned host, then the launcher, then seal the bundle
find "$RUNTIME" -type f ! -name FortnitePorting-unsigned -print0 | while IFS= read -r -d '' f; do
    if file -b "$f" | grep -q Mach-O; then codesign --force --sign - "$f"; fi
done
codesign --force --sign - "$CONTENTS/MacOS/FortnitePortingLauncher"
codesign --force --sign - "$APP"
codesign --verify --deep --strict "$APP"

mkdir -p "$OUT"
rm -rf "$OUT/FortnitePorting.app"
ditto "$APP" "$OUT/FortnitePorting.app"
ln -s /Applications "$WORK/stage/Applications"
hdiutil create -volname "FortnitePorting" -srcfolder "$WORK/stage" -ov -format UDZO \
    "$OUT/FortnitePorting-$VERSION-arm64.dmg"
echo "$OUT/FortnitePorting-$VERSION-arm64.dmg"
