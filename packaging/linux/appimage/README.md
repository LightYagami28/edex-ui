# Linux: AppImage

Stato: NON VERIFICATO. Serve Linux con `appimagetool`, che non c'è su questo PC.

## Cosa produce
`dist/edex-ui-<versione>-x86_64.AppImage` e `-aarch64.AppImage`: un file eseguibile unico. Dentro: `AppRun`, `edex-ui.desktop`, icona e il binario in `usr/bin/edex-ui`, presi dal tar.gz standalone.

## Prerequisiti
Linux dell'architettura da costruire (o QEMU), `bash`, `tar`, [`appimagetool`](https://github.com/AppImage/appimagetool/releases) nel `PATH`. I tar.gz in `packaging/dist` (da `build.sh`). FUSE 2 sull'utente finale, oppure `--appimage-extract-and-run`.

## Costruire
```
bash packaging/build.sh
bash packaging/dist/linux/appimage/build-appimage.sh x86_64      # oppure aarch64
```
Il comando esegue `ARCH=<arch> VERSION=<versione> appimagetool edex-ui.AppDir edex-ui-<versione>-<arch>.AppImage`.

## Installare, aggiornare, disinstallare
```
chmod +x edex-ui-<versione>-x86_64.AppImage && ./edex-ui-<versione>-x86_64.AppImage --version
```
Installare = copiare il file dove si vuole (es. `~/.local/bin`). Aggiornare = sostituire il file. Disinstallare = cancellarlo. Nessun altro file viene scritto (l'integrazione con il menu è opzionale, tramite AppImageLauncher).

## Verificare la somma SHA-256
`sha256sum edex-ui-<versione>-x86_64.AppImage` e confrontare con il valore pubblicato nella release (l'AppImage non è generato da `build.sh`, il proprietario pubblica la somma accanto al file).

## Pubblicare (passi del proprietario)
Caricare gli AppImage e le loro somme nella release GitHub. Per il catalogo appimage.github.io: pull request con il file di descrizione del repository AppImage/appimage.github.io. Per gli aggiornamenti in-place si può incorporare `--updateinformation` (zsync) con `gh-releases-zsync|LightYagami28|edex-ui|latest|edex-ui-*-x86_64.AppImage.zsync` (non incluso).
