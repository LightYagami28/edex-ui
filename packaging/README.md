# Distribuzione .NET 11

Una cartella per ogni formato, ciascuna con il proprio `README.md` (cosa produce, prerequisiti, build, installazione, verifica SHA-256, pubblicazione).

## Cosa si pubblica

Oggi il repository ha solo la libreria `src/Core`; le app native (`native/windows`, `native/linux`, `native/macos`, vedi [SPEC.md](../SPEC.md)) non ci sono ancora.
I pacchetti contengono quindi `src/Cli` (`edex-ui`, accetta solo `--version` e `--help`): serve a rendere reali build, archivi, somme e manifest.
Quando arrivano le app native si cambia il progetto in `build.ps1` / `build.sh`, il resto non cambia.

## Build

| Script | Dove | Produce |
| --- | --- | --- |
| `pwsh packaging/build.ps1` | Windows | `dist/edex-ui-<versione>-win-x64.zip`, `-win-arm64.zip`, somme `.sha256`, manifest winget/Chocolatey/Scoop/MSIX con URL e somme reali in `dist/windows/` |
| `bash packaging/build.sh` | Linux (o Git Bash) | `dist/edex-ui-<versione>-linux-{x64,arm64,musl-x64,musl-arm64}.tar.gz`, somme `.sha256`, file di definizione dei pacchetti Linux con URL e somme reali in `dist/linux/` |

- La versione si legge da `Directory.Build.props` (`<Version>`): è l'unico posto in cui si scrive.
- Nei file versionati URL, versione e somme sono segnaposto (`@VERSION@`, `@URL_BASE@`, `@SHA256_WIN_X64@`, `@SHA256_LINUX_X64@`, `@SRI_LINUX_X64@`, ...). Lo script li sostituisce nella copia in `dist/` e **fallisce se ne resta uno**.
- Gli URL puntano alla futura release GitHub `v<versione>`: le somme sono quelle degli archivi appena costruiti, quindi vanno caricati in quella release gli stessi file di `dist/`, senza ricostruirli.
- `dist/` è ignorato da git.

## Formati

Stato: **verificato** = costruito o validato su questo PC con un comando riportato nel README della cartella; **non verificato** = richiede Linux o strumenti non disponibili qui.

### Linux

| Formato | Cartella | Stato |
| --- | --- | --- |
| tar.gz standalone (x64, arm64, musl) | [linux/standalone](linux/standalone) | vedi README |
| AppImage | [linux/appimage](linux/appimage) | non verificato |
| .deb (Debian, Ubuntu) | [linux/deb](linux/deb) | vedi README |
| .rpm (Fedora, openSUSE) | [linux/rpm](linux/rpm) | non verificato |
| Arch (AUR) | [linux/arch](linux/arch) | non verificato |
| Alpine (musl, APKBUILD) | [linux/alpine](linux/alpine) | non verificato |
| Flatpak | [linux/flatpak](linux/flatpak) | non verificato |
| Snap | [linux/snap](linux/snap) | non verificato |
| Nix (flake) | [linux/nix](linux/nix) | non verificato |

### Windows

| Formato | Cartella | Stato |
| --- | --- | --- |
| zip portatile (x64, arm64) | [windows/standalone](windows/standalone) | vedi README |
| MSIX | [windows/msix](windows/msix) | non verificato |
| winget | [windows/winget](windows/winget) | vedi README |
| Chocolatey | [windows/chocolatey](windows/chocolatey) | vedi README |
| Scoop | [windows/scoop](windows/scoop) | vedi README |

macOS è fuori da questa cartella (Swift, vedi SPEC.md sezione 7.2).

## CI

`.github/workflows/packaging.yml` a ogni PR costruisce gli standalone di tutti i RID con i due script e valida i manifest dove esiste un validatore (winget, schema Scoop, `desktop-file-validate`, AppStream, `dpkg-deb`). Non pubblica nulla.

## Regole

Nessuna libreria di terzi. Nessun invio ai gestori di pacchetti e nessun tag/release da qui: sono passi del proprietario, descritti in ogni README.
