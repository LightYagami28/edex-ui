# eDEX-UI nativo (.NET)

[![CI](https://github.com/LightYagami28/edex-ui/actions/workflows/ci.yml/badge.svg)](https://github.com/LightYagami28/edex-ui/actions/workflows/ci.yml)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=LightYagami28_edex-ui&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=LightYagami28_edex-ui)
[![Licenza](https://img.shields.io/github/license/LightYagami28/edex-ui)](LICENSE)

eDEX-UI riscritto da zero in .NET 11: un terminale a schermo intero con
pannelli di sistema (CPU, RAM, rete, processi, file, globo) e tastiera su
schermo. Un'applicazione nativa per ogni sistema, senza librerie di terzi:
solo .NET e le API del sistema operativo.

Il codice Electron originale è nella storia di `master` e nel tag
`archivio/electron`.

## Stato

- `src/Core` (terminale, PTY, monitor, file, tema, tastiera, impostazioni,
  avvio, aggiornamenti): fatto, con test MSTest.
- App Windows (WPF): da fare.
- App Linux (X11): da fare.
- App macOS (Swift): da fare.

Dettagli e piano in [SPEC.md](SPEC.md).

## Requisiti

- .NET SDK 11 (versione in `global.json`).
- Windows 10 1809 o successivo (ConPTY), Linux con glibc o musl, macOS.

## Compilare e provare

```sh
dotnet build -c Release
dotnet test -c Release
dotnet test --coverage --coverage-output-format cobertura \
  --coverage-output cov.cobertura.xml
```

`global.json` attiva il test runner Microsoft.Testing.Platform. I test usano
MSTest. Gli script di `tools/` si provano con
`python3 -m unittest discover -s tests/tools`.

## Struttura

```text
src/Core/         libreria Edex.Core (nessuna dipendenza)
tests/Core.Tests/ test di Core
tests/tools/      test degli script di sviluppo
assets/           temi, layout tastiera, audio, caratteri
tools/            script di sviluppo
docs/             architettura e sviluppo
```

## Contribuire

Leggi [CONTRIBUTING.md](CONTRIBUTING.md), il
[codice di condotta](CODE_OF_CONDUCT.md) e, per le vulnerabilità,
[SECURITY.md](SECURITY.md). Per aiuto vedi [SUPPORT.md](SUPPORT.md).

## Licenza

GPL-3.0-only, vedi [LICENSE](LICENSE).
