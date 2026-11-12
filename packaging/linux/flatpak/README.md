# Linux: Flatpak

Stato: NON VERIFICATO. Serve Linux con `flatpak-builder`, che non c'è su questo PC.

## Cosa produce
Il manifest `io.github.lightyagami28.EdexUI.yml` (runtime `org.freedesktop.Platform` 25.08) e i metadati AppStream `io.github.lightyagami28.EdexUI.metainfo.xml`. Costruisce l'app dal tar.gz della release (x86_64 e aarch64) senza strip. Permessi: X11/Wayland, GPU, rete, home (un terminale deve vedere i file dell'utente).

## Prerequisiti
Linux con `flatpak` e `flatpak-builder`, il runtime e lo SDK: `flatpak install flathub org.freedesktop.Platform//25.08 org.freedesktop.Sdk//25.08`. La release GitHub con i tar.gz già pubblicata.

## Costruire
```
bash packaging/build.sh                        # genera dist/linux/flatpak/ con URL e somme reali
cd packaging/dist/linux/flatpak
flatpak-builder --force-clean --user --install build-dir io.github.lightyagami28.EdexUI.yml
flatpak run io.github.lightyagami28.EdexUI --version
```
Pacchetto in file: `flatpak-builder --repo=repo --force-clean build-dir ...` poi `flatpak build-bundle repo edex-ui.flatpak io.github.lightyagami28.EdexUI`.

## Installare, aggiornare, disinstallare
```
flatpak install --user edex-ui.flatpak           # oppure, da Flathub: flatpak install flathub io.github.lightyagami28.EdexUI
flatpak update io.github.lightyagami28.EdexUI
flatpak uninstall io.github.lightyagami28.EdexUI
```
Dove finiscono i file: `~/.local/share/flatpak/app/io.github.lightyagami28.EdexUI` (utente) o `/var/lib/flatpak/app/...` (sistema).

## Verificare la somma SHA-256
I campi `sha256` delle sorgenti sono quelli di `*.sha256`; `flatpak-builder` si ferma se il download non coincide.

## Pubblicare (passi del proprietario)
1. Pubblicare la release GitHub con i tar.gz usati per le somme.
2. Flathub: aprire una pull request a https://github.com/flathub/flathub (ramo `new-pr`) con manifest e metainfo; account GitHub, il dominio `io.github.lightyagami28` è verificato dal nome utente GitHub. Flathub richiede per le app non compilate dai sorgenti una revisione e ha regole sugli eseguibili precompilati.
3. Il linter: `flatpak-builder-lint manifest io.github.lightyagami28.EdexUI.yml` e `appstreamcli validate io.github.lightyagami28.EdexUI.metainfo.xml` (quest'ultimo è eseguito dalla CI).
