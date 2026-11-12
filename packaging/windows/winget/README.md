# Windows: winget

Stato: il manifest generato supera `winget validate` su questo PC (vedi sotto). L'installazione reale non è provata: lo zip non è ancora in una release.

## Cosa produce
Manifest a tre file (formato multi-file, `ManifestVersion` 1.12.0): `LightYagami28.EdexUI.yaml` (versione), `.installer.yaml` (zip con installer annidato `portable`, alias `edex-ui`, x64 e arm64), `.locale.it-IT.yaml` (locale predefinito). URL e SHA-256 vengono dagli zip di `build.ps1`, in `dist/windows/winget/`.

## Prerequisiti
Windows con winget 1.4 o successivo. Per validare: `winget validate`.

## Costruire e validare
```
pwsh packaging/build.ps1
winget validate --manifest packaging/dist/windows/winget
```

## Installare, aggiornare, disinstallare
Dopo la pubblicazione nel repository winget-pkgs:
```
winget install LightYagami28.EdexUI
winget upgrade LightYagami28.EdexUI
winget uninstall LightYagami28.EdexUI
```
Prova locale prima dell'invio (richiede `winget settings --enable LocalManifestFiles`):
```
winget install --manifest packaging/dist/windows/winget
```
Dove finiscono i file: pacchetto portatile in `%LOCALAPPDATA%\Microsoft\WinGet\Packages\LightYagami28.EdexUI_*`, con link in `%LOCALAPPDATA%\Microsoft\WinGet\Links`.

## Verificare la somma SHA-256
`InstallerSha256` nel file `.installer.yaml` deve coincidere con `(Get-FileHash <zip> -Algorithm SHA256).Hash` e con `<zip>.sha256`. Winget la verifica da solo in installazione.

## Pubblicare (passi del proprietario)
1. Pubblicare la release GitHub `v<versione>` con gli zip identici a quelli usati per generare il manifest.
2. Fare il fork di `microsoft/winget-pkgs`, copiare i tre file in `manifests/l/LightYagami28/EdexUI/<versione>/`.
3. Aprire la pull request (oppure `wingetcreate submit` con un token GitHub personale); la validazione automatica e la revisione sono del repository winget-pkgs.
