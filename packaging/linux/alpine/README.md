# Linux: Alpine (musl, APKBUILD)

Stato: NON VERIFICATO. Serve Alpine con `abuild`, che non c'è su questo PC. Il progetto compila per `linux-musl-x64` e `linux-musl-arm64` (build incrociata riuscita con `build.sh`, avvio non provato).

## Cosa produce
Pacchetto `edex-ui-<versione>-r0.apk` per x86_64 e aarch64, dal tar.gz musl. `options="!strip"` perché lo strip corrompe un eseguibile single-file .NET.

## Prerequisiti
Alpine Linux (o container `alpine:latest`) con `alpine-sdk`; utente nel gruppo `abuild` e chiave di firma generata con `abuild-keygen -a -i`. Dipendenze di runtime: `libstdc++ libgcc zlib openssl`.

## Costruire
```
bash packaging/build.sh                        # genera dist/linux/alpine/APKBUILD con URL e somme reali
cd packaging/dist/linux/alpine
abuild checksum && abuild -r
```
(`abuild checksum` riscrive `sha256sums`: devono restare uguali a quelle di `build.sh`.)

## Installare, aggiornare, disinstallare
```
sudo apk add --allow-untrusted ./edex-ui-<versione>-r0.apk     # oppure, con la chiave pubblica installata, senza --allow-untrusted
sudo apk upgrade edex-ui
sudo apk del edex-ui
```
Dove finiscono i file: `apk info -L edex-ui`.

## Verificare la somma SHA-256
`sha256sums` nell'APKBUILD = quelle dei tar.gz musl (`*.sha256`); `abuild` le verifica al download.

## Pubblicare (passi del proprietario)
- Repository proprio: firmare con la chiave `abuild`, `apk index`, pubblicare la chiave pubblica.
- Alpine `testing`: merge request su https://gitlab.alpinelinux.org/alpine/aports (account GitLab Alpine) con l'APKBUILD in `testing/edex-ui/`; i pacchetti di Alpine devono compilare dai sorgenti, un binario precompilato può essere rifiutato.
