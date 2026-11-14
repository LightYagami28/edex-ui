# Linux: tar.gz standalone

Stato: costruiti da questo PC (Windows) in compilazione incrociata con `bash packaging/build.sh`; l'esecuzione su Linux NON VERIFICATO (nessun Linux su questo PC, vedi il riepilogo della PR per l'esito della build).

## Cosa produce
Per ogni RID `linux-x64`, `linux-arm64`, `linux-musl-x64` (Alpine) e `linux-musl-arm64`: `dist/edex-ui-<versione>-<rid>.tar.gz` + `<file>.sha256`. Contenuto: `edex-ui-<versione>-<rid>/edex-ui` (single-file self-contained, nessun .NET da installare), `LICENSE`, `share/applications/edex-ui.desktop`, `share/icons/hicolor/256x256/apps/edex-ui.png`. Tutti gli altri pacchetti Linux partono da questi archivi.

## Prerequisiti
.NET SDK 11 (versione in `global.json`), `bash`, `tar`, `gzip`, `sha256sum`, `base64`. Libreria di sistema: glibc 2.17+ con `libstdc++`, `zlib`, `libssl` (o musl con `libstdc++`, `libgcc`, `zlib`, `openssl` su Alpine). Il globalization è invariante, `libicu` non serve.

## Costruire
```
bash packaging/build.sh
```
Per ogni RID esegue `dotnet publish src/Cli/Edex.Cli.csproj -c Release -r <rid> --self-contained true -p:Version=<versione>`, poi crea il tar.gz con permessi espliciti (eseguibile 0755, resto 0644, proprietario root).

## Installare, aggiornare, disinstallare
```
sudo tar -xzf edex-ui-<versione>-linux-x64.tar.gz -C /opt
sudo ln -sf /opt/edex-ui-<versione>-linux-x64/edex-ui /usr/local/bin/edex-ui        # installa
edex-ui --version
```
Aggiornare: estrarre la nuova versione e rifare il link. Disinstallare: `sudo rm /usr/local/bin/edex-ui && sudo rm -r /opt/edex-ui-*`.
Senza root: estrarre in `~/.local/opt` e linkare in `~/.local/bin`. Dove finiscono i file: dove si estrae.

## Verificare la somma SHA-256
```
sha256sum -c edex-ui-<versione>-linux-x64.tar.gz.sha256
```

## Pubblicare (passi del proprietario)
Caricare i quattro tar.gz e i `.sha256` nella release GitHub `v<versione>` (stessi file prodotti da `build.sh`, senza ricostruirli).
