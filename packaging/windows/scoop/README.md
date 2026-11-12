# Windows: Scoop

Stato: il manifest generato è validato contro lo schema ufficiale di Scoop su questo PC (vedi sotto). Scoop non è installato qui, quindi l'installazione non è provata.

## Cosa produce
`edex-ui.json`, manifest di un bucket Scoop: zip x64 (`64bit`) e arm64, `bin: edex-ui.exe`, `checkver: github` e `autoupdate` con hash letto da `<zip>.sha256`. Generato con URL e somme reali in `dist/windows/scoop/`.

## Prerequisiti
Scoop (https://scoop.sh) e un bucket proprio (repository git, es. `LightYagami28/scoop-bucket`) con la cartella `bucket/`.

## Costruire e validare
```
pwsh packaging/build.ps1
Test-Json -Path packaging/dist/windows/scoop/edex-ui.json -SchemaFile <schema.json di ScoopInstaller/Scoop>
```

## Installare, aggiornare, disinstallare
Prova locale: `scoop install packaging/dist/windows/scoop/edex-ui.json`. Con il bucket pubblicato:
```
scoop bucket add lightyagami28 https://github.com/LightYagami28/scoop-bucket
scoop install edex-ui
scoop update edex-ui
scoop uninstall edex-ui
```
Dove finiscono i file: `~\scoop\apps\edex-ui\<versione>\`, shim in `~\scoop\shims`.

## Verificare la somma SHA-256
`hash` nel manifest deve coincidere con `<zip>.sha256`; Scoop la verifica in installazione.

## Pubblicare (passi del proprietario)
1. Pubblicare la release GitHub con gli zip usati per generare il manifest.
2. Creare il repository del bucket, copiare `edex-ui.json` in `bucket/`, fare push.
3. Gli aggiornamenti successivi li fa `checkver -u` / l'azione `Excavator` del bucket grazie a `autoupdate`.
