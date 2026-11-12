#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Costruisce gli standalone Linux (glibc e musl, x64 e arm64), calcola le somme SHA-256 e genera i file dei pacchetti in packaging/dist.
# Uso: bash packaging/build.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
repo='LightYagami28/edex-ui'
version="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$root/Directory.Build.props")"
[ -n "$version" ] || { echo 'Versione non trovata in Directory.Build.props' >&2; exit 1; }
dist="$here/dist"
# Pulisce solo i propri artefatti: build.ps1 scrive nella stessa cartella.
rm -rf "$dist/stage" "$dist/linux" "$dist"/edex-ui-*-linux-*
mkdir -p "$dist/stage"

declare -A tokens=([VERSION]="$version" [URL_BASE]="https://github.com/$repo/releases/download/v$version")
for rid in linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64; do
  name="edex-ui-$version-$rid"
  dir="$dist/stage/$name"
  dotnet publish "$root/src/Cli/Edex.Cli.csproj" -c Release -r "$rid" --self-contained true "-p:Version=$version" -o "$dir"
  cp "$root/LICENSE" "$dir/"
  mkdir -p "$dir/share/applications" "$dir/share/icons/hicolor/256x256/apps"
  cp "$here/linux/shared/edex-ui.desktop" "$dir/share/applications/"
  cp "$root/media/linuxIcons/256x256.png" "$dir/share/icons/hicolor/256x256/apps/edex-ui.png"
  # Permessi espliciti (anche se costruito da Windows): il binario eseguibile, il resto in sola lettura.
  tar -cf "$dist/$name.tar" --owner=0 --group=0 --numeric-owner --mode=0755 -C "$dist/stage" "$name/edex-ui"
  tar -rf "$dist/$name.tar" --owner=0 --group=0 --numeric-owner --mode=0644 -C "$dist/stage" "$name/LICENSE" "$name/share"
  gzip -n -9 "$dist/$name.tar"
  hex="$(sha256sum "$dist/$name.tar.gz" | cut -d' ' -f1)"
  echo "$hex  $name.tar.gz" > "$dist/$name.tar.gz.sha256"
  key="$(echo "$rid" | tr 'a-z-' 'A-Z_')"
  tokens["SHA256_$key"]="$hex"
  # Formato SRI (sha256-base64) richiesto da Nix.
  tokens["SRI_$key"]="sha256-$(printf "$(echo "$hex" | sed 's/../\x&/g')" | base64 -w0)"
done
rm -rf "$dist/stage"

sedargs=()
for k in "${!tokens[@]}"; do sedargs+=(-e "s|@${k}@|${tokens[$k]}|g"); done
while IFS= read -r -d '' f; do
  rel="${f#"$here/"}"
  mkdir -p "$dist/$(dirname "$rel")"
  sed "${sedargs[@]}" "$f" > "$dist/$rel"
  [ -x "$f" ] && chmod +x "$dist/$rel"
done < <(find "$here/linux" -type f ! -name README.md -print0)

# Fallisce se un segnaposto non è stato sostituito.
if grep -rnE '@[A-Z][A-Z0-9_]*@' "$dist/linux"; then echo 'Segnaposto non sostituiti' >&2; exit 1; fi
echo "Pronto in $dist (versione $version)"
