# Linux: Nix

Stato: NON VERIFICATO. Nix non è installato su questo PC e non si installa software qui.

## Cosa produce
Un flake (`flake.nix`) con `packages.<sistema>.default` per `x86_64-linux` e `aarch64-linux`: scarica il tar.gz della release, patcha l'ELF con `autoPatchelfHook`, installa `bin/edex-ui`, il `.desktop` e l'icona. `dontStrip = true` perché lo strip corrompe un eseguibile single-file .NET. L'hash è in formato SRI (`sha256-<base64>`) calcolato da `build.sh`.

## Prerequisiti
Nix con le funzioni sperimentali `nix-command flakes`; la release GitHub con i tar.gz già pubblicata. Il flake usa `nixpkgs` `nixos-unstable`; `nix flake lock` crea `flake.lock`, da committare per fissare la revisione.

## Costruire
```
bash packaging/build.sh                        # genera dist/linux/nix/flake.nix con URL e hash reali
cd packaging/dist/linux/nix
nix flake lock
nix build .#default
./result/bin/edex-ui --version
```

## Installare, aggiornare, disinstallare
```
nix profile install .#default                  # oppure, dal repository: nix profile install github:LightYagami28/edex-ui?dir=packaging/linux/nix
nix profile upgrade edex-ui
nix profile remove edex-ui
```
Dove finiscono i file: `/nix/store/<hash>-edex-ui-<versione>/`, link in `~/.nix-profile/bin`.

## Verificare la somma SHA-256
`hash` in `flake.nix` è la SRI del tar.gz. Per confrontarla: `nix hash convert --hash-algo sha256 --to sri <hex da *.sha256>`. Nix rifiuta il download se non coincide.

## Pubblicare (passi del proprietario)
- Come flake: committare `flake.nix` e `flake.lock` nel repository (o in uno dedicato); nessun account.
- In nixpkgs: pull request a https://github.com/NixOS/nixpkgs con `pkgs/by-name/ed/edex-ui/package.nix`; nixpkgs richiede di norma la compilazione dai sorgenti per il software libero, quindi un binario precompilato può non essere accettato.
