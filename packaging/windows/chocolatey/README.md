# Windows: Chocolatey

Stato: NON VERIFICATO. Chocolatey non è installato su questo PC e non si installa software qui. I file sono scritti seguendo la documentazione ufficiale (`Install-ChocolateyZipPackage`, nuspec); l'XML del nuspec è controllato solo come XML ben formato.

## Cosa produce
Un pacchetto `edex-ui.<versione>.nupkg` che scarica lo zip x64 della release GitHub, ne verifica la SHA-256 (`Checksum64`) e lo estrae in `tools`; Chocolatey crea lo shim `edex-ui` da solo. Su Windows arm64 gira lo zip x64 in emulazione (Chocolatey non ha un campo arm64).
File: `edex-ui.nuspec`, `tools/chocolateyInstall.ps1`, `tools/chocolateyUninstall.ps1`; generati con URL e somma reali in `dist/windows/chocolatey/`.

## Prerequisiti
Chocolatey (`choco`), versione 1.x o successiva, e la release GitHub con lo zip già pubblicata.

## Costruire
```
pwsh packaging/build.ps1
cd packaging/dist/windows/chocolatey
choco pack
```

## Installare, aggiornare, disinstallare
Prova locale: `choco install edex-ui --source . -y`. Dopo la pubblicazione:
```
choco install edex-ui
choco upgrade edex-ui
choco uninstall edex-ui
```
Dove finiscono i file: `C:\ProgramData\chocolatey\lib\edex-ui\tools\edex-ui-<versione>-win-x64\`, shim in `C:\ProgramData\chocolatey\bin\edex-ui.exe`.

## Verificare la somma SHA-256
`Checksum64` in `chocolateyInstall.ps1` deve coincidere con `<zip>.sha256`; Chocolatey la controlla in installazione e fallisce se non torna.

## Pubblicare (passi del proprietario)
1. Pubblicare la release GitHub con gli zip usati per generare i file.
2. Creare un account su https://community.chocolatey.org e copiare la chiave API dal profilo (non metterla in file versionati).
3. `choco apikey --key <CHIAVE> --source https://push.chocolatey.org/` poi `choco push edex-ui.<versione>.nupkg --source https://push.chocolatey.org/`.
4. Il pacchetto passa moderazione e verifica automatica sul sito di Chocolatey.
