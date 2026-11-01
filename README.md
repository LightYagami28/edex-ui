# eDEX-UI nativo (.NET)

eDEX-UI riscritto da zero in .NET 10: un terminale a schermo intero con pannelli di sistema (CPU, RAM, rete, processi, file, globo) e tastiera su schermo.
Un'applicazione nativa per ogni sistema, senza librerie di terzi: solo .NET e le API del sistema operativo.

Il codice Electron originale è nella storia di `master` e nel tag `archivio/electron`.

## Stato

| Parte | Stato |
| --- | --- |
| `src/Core` (terminale, PTY, monitor, file, tema, tastiera, impostazioni, avvio, aggiornamenti) | fatto, 128 test |
| `native/windows` (WPF) | da fare |
| `native/linux` (X11) | da fare |
| `native/macos` (AppKit) | da fare |

Dettagli e piano in [SPEC.md](SPEC.md).

## Requisiti

- .NET SDK 10
- Windows 10 1809 o successivo (ConPTY), Linux con glibc o musl, macOS

## Compilare e provare

```
dotnet build
dotnet test
dotnet test --coverage --coverage-output-format cobertura --coverage-output cov.cobertura.xml
```

`global.json` attiva il test runner Microsoft.Testing.Platform. I test usano MSTest.

## Struttura

```
src/Core/        libreria Edex.Core (nessuna dipendenza)
tests/Core.Tests test di Core
assets/          temi, layout tastiera, audio, caratteri
tools/           script di sviluppo
docs/            architettura e sviluppo
```

Rami: `native/base` (Core, test, CI), poi `native/windows`, `native/linux`, `native/macos`.

## Licenza

GPL-3.0-only, vedi [LICENSE](LICENSE).
