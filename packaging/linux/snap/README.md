# Linux: Snap

Stato: NON VERIFICATO. Serve Linux con `snapcraft`, che non c'è su questo PC. La sintassi `platforms` (core24) e la grammatica `on <arch>` per `source` e `source-checksum` sono riprese dalla documentazione di Snapcraft ma non sono mai state eseguite.

## Cosa produce
`edex-ui_<versione>_amd64.snap` e `_arm64.snap` (base `core24`) con il binario dal tar.gz della release. Confinamento `classic`: un terminale deve poter avviare la shell dell'utente, e `classic` richiede l'approvazione manuale dello Snap Store.

## Prerequisiti
Linux con `snapcraft` (`sudo snap install snapcraft --classic`) e LXD o Multipass; la release GitHub con i tar.gz già pubblicata.

## Costruire
```
bash packaging/build.sh                        # genera dist/linux/snap/snap/snapcraft.yaml con URL e somme reali
cd packaging/dist/linux/snap
snapcraft pack
```

## Installare, aggiornare, disinstallare
```
sudo snap install --classic --dangerous ./edex-ui_<versione>_amd64.snap     # prova locale
sudo snap install --classic edex-ui                                          # dallo Store, dopo la pubblicazione
sudo snap refresh edex-ui
sudo snap remove edex-ui
```
Dove finiscono i file: `/snap/edex-ui/current/`.

## Verificare la somma SHA-256
`source-checksum: sha256/...` nello YAML è quella dei tar.gz (`*.sha256`); Snapcraft ferma la build se non coincide.

## Pubblicare (passi del proprietario)
1. Account Ubuntu One e `snapcraft login` (le credenziali non vanno in file versionati).
2. `snapcraft register edex-ui` per prenotare il nome.
3. `snapcraft upload --release=stable edex-ui_<versione>_amd64.snap` (e arm64).
4. Richiesta di confinamento `classic` sul forum https://forum.snapcraft.io (categoria "store-requests"): senza approvazione la pubblicazione `stable` è rifiutata.
