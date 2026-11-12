#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Costruisce l'AppImage dal tar.gz standalone. Uso: bash build-appimage.sh x86_64|aarch64
# Da eseguire da packaging/dist/linux/appimage. Richiede appimagetool nel PATH (https://github.com/AppImage/appimagetool/releases).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dist="$(cd "$here/../.." && pwd)"
arch="${1:?architettura: x86_64 o aarch64}"
case "$arch" in x86_64) rid=linux-x64 ;; aarch64) rid=linux-arm64 ;; *) echo "architettura non supportata: $arch" >&2; exit 1 ;; esac
version="@VERSION@"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
tar -xzf "$dist/edex-ui-$version-$rid.tar.gz" -C "$tmp"
src="$tmp/edex-ui-$version-$rid"
app="$tmp/edex-ui.AppDir"
install -Dm755 "$src/edex-ui" "$app/usr/bin/edex-ui"
install -Dm644 "$src/share/applications/edex-ui.desktop" "$app/edex-ui.desktop"
install -Dm644 "$src/share/icons/hicolor/256x256/apps/edex-ui.png" "$app/edex-ui.png"
ln -s edex-ui.png "$app/.DirIcon"
printf '#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/usr/bin/edex-ui" "$@"\n' > "$app/AppRun"
chmod +x "$app/AppRun"
ARCH="$arch" VERSION="$version" appimagetool "$app" "$dist/edex-ui-$version-$arch.AppImage"
