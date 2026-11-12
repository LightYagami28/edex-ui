# Windows: MSIX

Stato: NON VERIFICATO. `makeappx.exe` (Windows SDK) non è disponibile su questo PC e non si installa software qui. Il manifest non è stato mai passato a `makeappx`.

## Cosa produce
`AppxManifest.xml` per un pacchetto MSIX full-trust (`runFullTrust`) di `edex-ui.exe`, solo x64. Il pacchetto `.msix` va firmato per essere installabile fuori dallo Store.
Limiti noti: i loghi usano le PNG del repository (`media/linuxIcons`), non le misure esatte richieste dallo Store (44x44, 150x150, 50x50): per lo Store servono loghi dedicati.

## Prerequisiti
Windows SDK 10.0.17763 o successivo (`makeappx.exe`, `signtool.exe`), uno zip x64 costruito con `build.ps1`, un certificato di firma il cui Subject è `CN=Light Yagami` (o cambiare `Publisher` nel manifest).

## Costruire
```
pwsh packaging/build.ps1
$w = "packaging/dist/msix-layout"
Expand-Archive packaging/dist/edex-ui-<versione>-win-x64.zip $w
Copy-Item "$w/edex-ui-<versione>-win-x64/*" $w -Recurse; Remove-Item "$w/edex-ui-<versione>-win-x64" -Recurse
Copy-Item packaging/dist/windows/msix/AppxManifest.xml $w
New-Item -ItemType Directory "$w/Assets" | Out-Null
Copy-Item media/linuxIcons/48x48.png  "$w/Assets/Square44x44Logo.png"
Copy-Item media/linuxIcons/256x256.png "$w/Assets/Square150x150Logo.png"
Copy-Item media/linuxIcons/48x48.png  "$w/Assets/StoreLogo.png"
makeappx pack /d $w /p packaging/dist/edex-ui-<versione>-x64.msix
signtool sign /fd SHA256 /a /f <certificato.pfx> /p <password> packaging/dist/edex-ui-<versione>-x64.msix
```

## Installare, aggiornare, disinstallare
```
Add-AppxPackage packaging/dist/edex-ui-<versione>-x64.msix      # installa e aggiorna (stessa Identity, versione maggiore)
Get-AppxPackage LightYagami28.EdexUI | Remove-AppxPackage       # disinstalla
```
Dove finiscono i file: `C:\Program Files\WindowsApps\LightYagami28.EdexUI_<versione>_x64__*`.

## Verificare la somma SHA-256
`(Get-FileHash packaging/dist/edex-ui-<versione>-x64.msix -Algorithm SHA256).Hash`; l'MSIX non è generato dagli script, la somma si calcola dopo la firma.

## Pubblicare (passi del proprietario)
- Fuori dallo Store: caricare l'`.msix` firmato nella release GitHub; chi lo installa deve fidarsi del certificato.
- Microsoft Store: account sviluppatore Microsoft Partner Center (a pagamento), prenotare il nome, cambiare `Identity` e `Publisher` con i valori assegnati dal Partner Center, caricare il pacchetto non firmato (lo firma lo Store).
