#!/bin/sh
# Wraps a `dotnet publish -r osx-*` output folder into eDEX-UI.app (unsigned).
# Usage: make-bundle.sh <publish-dir> <output-dir>
set -eu
PUB="$1"; OUT="$2"
APP="$OUT/eDEX-UI.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
cp "$(dirname "$0")/Info.plist" "$APP/Contents/Info.plist"
rm -f "$APP/Contents/MacOS/Info.plist" "$APP/Contents/MacOS/make-bundle.sh"
chmod +x "$APP/Contents/MacOS/eDEX-UI"
echo "$APP"
