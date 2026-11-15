# Windows: zip portatile

Stato: costruito e avviato su questo PC (win-x64), vedi sotto. win-arm64
costruito, non avviato (nessuna macchina arm64).

## Cosa produce

`dist/edex-ui-<versione>-win-x64.zip` e `dist/edex-ui-<versione>-win-arm64.zip`:
eseguibile unico self-contained (nessun .NET da installare) in una cartella
`edex-ui-<versione>-<rid>/` con `edex-ui.exe` e `LICENSE`. Accanto a ogni zip
c'è `<zip>.sha256`.

## Prerequisiti

.NET SDK 11 (versione in `global.json`), PowerShell 7. Le dipendenze di runtime
non servono.

## Costruire

```powershell
pwsh packaging/build.ps1
```

Equivale, per ogni RID, a questo comando più la creazione dello zip:

```powershell
dotnet publish src/Cli/Edex.Cli.csproj -c Release -r <rid> `
  --self-contained true -p:Version=<versione> -o <cartella>
```

## Installare, aggiornare, disinstallare

- Installare: estrarre lo zip dove si vuole, ad esempio
  `Expand-Archive edex-ui-<versione>-win-x64.zip $env:LOCALAPPDATA\Programs`.
- Aggiornare: estrarre il nuovo zip sopra o accanto al vecchio.
- Disinstallare: cancellare la cartella.
- Dove finiscono i file: solo dove si estrae. Nessuna voce di registro, nessun
  PATH modificato.

## Verificare la somma SHA-256

```powershell
# deve coincidere con il contenuto di <zip>.sha256
(Get-FileHash edex-ui-<versione>-win-x64.zip -Algorithm SHA256).Hash.ToLower()
```

## Pubblicare (passi del proprietario)

1. Eseguire `build.ps1` e `build.sh` (o scaricare gli artefatti della CI) sullo
   stesso commit.
2. Creare il tag `v<versione>` e la release GitHub; caricare zip, tar.gz e tutti
   i `.sha256` di `dist/` senza ricostruirli.
