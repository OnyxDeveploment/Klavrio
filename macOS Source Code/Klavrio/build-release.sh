#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
BUILD_ROOT="$SCRIPT_DIR/build"
APP="$BUILD_ROOT/Klavrio.app"
DMG="$BUILD_ROOT/Klavrio-1.0.0-macOS-universal.dmg"
IDENTITY="${KLAVRIO_SIGNING_IDENTITY:--}"

cd "$SCRIPT_DIR"
echo "Building universal Klavrio binary…"
swift build -c release --arch arm64 --arch x86_64
BIN_DIR="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/Klavrio" "$APP/Contents/MacOS/Klavrio"
cp "$SCRIPT_DIR/Info.plist" "$APP/Contents/Info.plist"

echo "Copying dictionaries and assets…"
rsync -a --delete "$REPO_ROOT/Data/" "$APP/Contents/Resources/Data/"
cp "$REPO_ROOT/Assets/Klavrio.png" "$APP/Contents/Resources/Klavrio.png"
cp "$REPO_ROOT/LICENSE" "$APP/Contents/Resources/LICENSE"
cp "$REPO_ROOT/THIRD_PARTY_NOTICES.txt" "$APP/Contents/Resources/THIRD_PARTY_NOTICES.txt"
rsync -a --delete "$REPO_ROOT/ThirdParty/" "$APP/Contents/Resources/ThirdParty/"

ICONSET="$BUILD_ROOT/AppIcon.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
for spec in "16:16x16" "32:16x16@2x" "32:32x32" "64:32x32@2x" "128:128x128" "256:128x128@2x" "256:256x256" "512:256x256@2x" "512:512x512" "1024:512x512@2x"; do
    pixels="${spec%%:*}"; name="${spec#*:}"
    sips -z "$pixels" "$pixels" "$REPO_ROOT/Assets/Klavrio.png" --out "$ICONSET/icon_${name}.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

echo "Signing app with identity: $IDENTITY"
codesign --force --deep --options runtime --entitlements "$SCRIPT_DIR/Klavrio.entitlements" --sign "$IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

echo "Creating DMG…"
STAGING="$BUILD_ROOT/dmg"
rm -rf "$STAGING" "$DMG"
mkdir -p "$STAGING"
ditto "$APP" "$STAGING/Klavrio.app"
ln -s /Applications "$STAGING/Applications"
hdiutil create -volname "Klavrio 1.0.0" -srcfolder "$STAGING" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGING"

echo "Created:"
echo "  $APP"
echo "  $DMG"
if [[ "$IDENTITY" == "-" ]]; then
    echo "Note: this is an ad-hoc build. Set KLAVRIO_SIGNING_IDENTITY to a Developer ID Application identity for distribution."
fi
