#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dist="$(cd "$here/../.." && pwd)"
arch="${1:?architettura: amd64 o arm64}"
case "$arch" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "architettura non supportata: $arch" >&2; exit 1 ;; esac
version="$(sed -n 's/^Version: \(.*\)-1$/\1/p' "$here/control")"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
tar -xzf "$dist/edex-ui-$version-$rid.tar.gz" -C "$tmp"
src="$tmp/edex-ui-$version-$rid"
pkg="$tmp/pkg"
install -Dm755 "$src/edex-ui" "$pkg/usr/bin/edex-ui"
install -Dm644 "$src/LICENSE" "$pkg/usr/share/doc/edex-ui/copyright"
install -Dm644 "$src/share/applications/edex-ui.desktop" "$pkg/usr/share/applications/edex-ui.desktop"
install -Dm644 "$src/share/icons/hicolor/256x256/apps/edex-ui.png" "$pkg/usr/share/icons/hicolor/256x256/apps/edex-ui.png"
mkdir -p "$pkg/DEBIAN"
size="$(du -sk "$pkg/usr" | cut -f1)"
sed -e "s/^Architecture:.*/Architecture: $arch/" "$here/control" > "$pkg/DEBIAN/control"
echo "Installed-Size: $size" >> "$pkg/DEBIAN/control"
dpkg-deb --root-owner-group --build "$pkg" "$dist/edex-ui_${version}-1_${arch}.deb"
