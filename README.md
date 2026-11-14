# eDEX-UI nativo (.NET)

eDEX-UI riscritto da zero in .NET 11: un terminale a schermo intero con pannelli di sistema (CPU, RAM, rete, processi, file, globo) e tastiera su schermo.
Un'applicazione nativa per ogni sistema, senza librerie di terzi: solo .NET e le API del sistema operativo.

Il codice Electron originale è nella storia di `master` e nel tag `archivio/electron`.

## Stato

| Parte | Stato |
| --- | --- |
| `src/Core` (terminale, PTY, monitor, file, tema, tastiera, impostazioni, avvio, aggiornamenti, scena) | fatto, 215 test |
| `src/Windows` (WPF) | fatto: app a schermo intero con smoke test in CI |
| `native/linux` (X11) | da fare |
| `native/macos` (Swift) | da fare |

Dettagli e piano in [SPEC.md](SPEC.md).

## Requisiti

- .NET SDK 11 (versione in `global.json`)
- Windows 10 1809 o successivo (ConPTY), Linux con glibc o musl, macOS

## Compilare e provare

```
dotnet build
dotnet test
dotnet test --coverage --coverage-output-format cobertura --coverage-output cov.cobertura.xml
dotnet build src/Windows/Edex.Windows.csproj -c Release
src/Windows/bin/Release/net11.0-windows/win-x64/edex-ui.exe --smoke-test --screenshot smoke.png
```

`global.json` attiva il test runner Microsoft.Testing.Platform. I test usano MSTest.

Lo smoke test avvia la shell su un pseudo terminale, esegue un comando, ne verifica l'output nel terminale e salva uno screenshot della scena.

## Struttura

```
src/Core/        libreria Edex.Core (nessuna dipendenza)
src/Windows/     app WPF (finestra, rendering, input)
tests/Core.Tests test di Core
assets/          temi, layout tastiera, audio, caratteri
tools/           script di sviluppo
docs/            architettura e sviluppo
```

Rami: `native/base` (Core, test, CI), poi `native/windows`, `native/linux`, `native/macos`.

## Licenza

GPL-3.0-only, vedi [LICENSE](LICENSE).
