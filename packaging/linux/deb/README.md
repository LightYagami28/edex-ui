# Linux: .deb (Debian, Ubuntu)

Stato: vedi il riepilogo della PR; se `dpkg-deb` non era disponibile su questo PC il pacchetto risulta NON VERIFICATO. Il file `control` segue il Debian Policy Manual (campi obbligatori `Package`, `Version`, `Architecture`, `Maintainer`, `Description`).

## Cosa produce
`dist/edex-ui_<versione>-1_amd64.deb` e `_arm64.deb`. Installa `/usr/bin/edex-ui`, `/usr/share/applications/edex-ui.desktop`, l'icona 256x256 e `/usr/share/doc/edex-ui/copyright` (la licenza).

## Prerequisiti
Linux Debian/Ubuntu (o altra distribuzione con `dpkg-deb`), `bash`, `tar`. I tar.gz in `packaging/dist` (da `build.sh`).

## Costruire
```
bash packaging/build.sh
bash packaging/dist/linux/deb/build-deb.sh amd64     # oppure arm64
dpkg-deb --info packaging/dist/edex-ui_<versione>-1_amd64.deb
```
Il passo finale dello script è `dpkg-deb --root-owner-group --build <albero> edex-ui_<versione>-1_<arch>.deb`.

## Installare, aggiornare, disinstallare
```
sudo apt install ./edex-ui_<versione>-1_amd64.deb    # installa e aggiorna (versione maggiore)
sudo apt remove edex-ui                               # disinstalla (purge: sudo apt purge edex-ui)
```
Dove finiscono i file: `dpkg -L edex-ui`.

## Verificare la somma SHA-256
`sha256sum edex-ui_<versione>-1_amd64.deb`, da confrontare con il valore pubblicato (i `.deb` non sono generati da `build.sh`: il proprietario pubblica la somma accanto al file). La somma dell'eseguibile dentro il `.deb` è quella del tar.gz di partenza (`sha256sum -c edex-ui-<versione>-linux-x64.tar.gz.sha256`).

## Pubblicare (passi del proprietario)
- Release GitHub: caricare i `.deb` con le somme.
- Repository apt proprio: firma GPG del repository e `reprepro`/`aptly`; oppure Launchpad PPA (account Launchpad, pacchetto sorgente `debian/` firmato, che qui non c'è: questo è un pacchetto binario).
- Ingresso nei repository Debian ufficiali: richiede un sorgente costruibile dai sorgenti e un sponsor; non è previsto con questo pacchetto binario.
