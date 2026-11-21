# Domande frequenti

## Perché riscrivere eDEX-UI in .NET abbandonando Electron?

L'originale Electron consumava oltre 1 GB di RAM per eseguire un terminale
grafico e soffriva di latenze di input. La riscrittura nativa in .NET 11 riduce
il consumo di memoria di oltre l'80%, azzera le vulnerabilità tipiche
dell'ecosistema Chromium/Node.js e garantisce un avvio istantaneo con controllo
diretto della ConPTY.

## Quali sono i requisiti minimi di sistema?

- **Windows**: Windows 10 versione 1809 (Build 17763) o successiva (richiesta
per il supporto nativo a ConPTY).
- **SDK di sviluppo**: .NET SDK 11.0 (versione specificata in global.json).

## Come si compila il progetto ed esegue lo smoke test?

```bash
# Compilazione ed esecuzione test unitari
dotnet build -c Release
dotnet test -c Release

# Smoke test dell'app Windows
dotnet build src/Windows/Edex.Windows.csproj -c Release
./src/Windows/bin/Release/net11.0-windows/win-x64/edex-ui.exe --smoke-test --screenshot smoke.png
```

## Il programma supporta terminali su Linux e macOS?

Il nucleo `Edex.Core` è compatibile al 100% con Linux e macOS. L'adattatore
grafico nativo per Linux (X11/Wayland) e macOS è pianificato in roadmap per
completare la parità multipiattaforma.

## Come si personalizzano scorciatoie e temi?

I file di configurazione risiedono nella directory dati locale dell'applicazione
e utilizzano formato JSON standard con convalida schema rigorosa e fail-safe
(`Safe.cs`).
