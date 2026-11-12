# Linux: Arch (AUR)

Stato: NON VERIFICATO. Serve Arch Linux con `makepkg`, che non c'è su questo PC. Lo schema segue le linee guida del pacchetto binario dell'Arch Wiki (`source_<arch>`, `sha256sums_<arch>`, `options=('!strip')`).

## Cosa produce
Il pacchetto `edex-ui-bin-<versione>-1-<arch>.pkg.tar.zst` (x86_64 e aarch64, quest'ultimo per Arch Linux ARM). Il nome `-bin` è la convenzione AUR per i binari precompilati; `provides`/`conflicts` = `edex-ui`.

## Prerequisiti
Arch Linux (o container `archlinux`) con `base-devel`; la release GitHub con i tar.gz già pubblicata (il PKGBUILD li scarica).

## Costruire
```
bash packaging/build.sh                          # genera dist/linux/arch/PKGBUILD con URL e somme reali
cd packaging/dist/linux/arch
makepkg -f                                       # scarica, verifica la somma, impacchetta
makepkg --printsrcinfo > .SRCINFO                # richiesto dall'AUR
namcap PKGBUILD *.pkg.tar.zst
```

## Installare, aggiornare, disinstallare
```
sudo pacman -U edex-ui-bin-<versione>-1-x86_64.pkg.tar.zst     # installa / aggiorna
sudo pacman -R edex-ui-bin                                      # disinstalla
```
Con un helper AUR dopo la pubblicazione: `yay -S edex-ui-bin`. Dove finiscono i file: `pacman -Ql edex-ui-bin`.

## Verificare la somma SHA-256
`sha256sums_x86_64` e `sha256sums_aarch64` nel PKGBUILD sono quelle dei tar.gz (stesse di `*.sha256`); `makepkg` le verifica da solo e si ferma se non coincidono. Per saltare i controlli di firma non serve nulla: non c'è firma.

## Pubblicare (passi del proprietario)
1. Pubblicare la release GitHub con i tar.gz identici a quelli usati per le somme.
2. Account su https://aur.archlinux.org con chiave SSH caricata nel profilo.
3. `git clone ssh://aur@aur.archlinux.org/edex-ui-bin.git`, copiare `PKGBUILD` e `.SRCINFO`, `git add`, `git commit`, `git push`.
