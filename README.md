# eDEX-UI nativo (.NET)

[![CI](https://github.com/LightYagami28/edex-ui/actions/workflows/ci.yml/badge.svg)](https://github.com/LightYagami28/edex-ui/actions/workflows/ci.yml)
[![Quality
Gate](https://sonarcloud.io/api/project_badges/measure?project=LightYagami28_edex-ui&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=LightYagami28_edex-ui)
[![.NET](https://img.shields.io/badge/.NET-11.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Licenza](https://img.shields.io/github/license/LightYagami28/edex-ui)](LICENSE)

eDEX-UI riscritto da zero in .NET 11: terminale a schermo intero con pannelli
di sistema (CPU, RAM, rete, processi, file, globo) e tastiera su schermo.
L'applicazione è nativa su ogni piattaforma e non dipende da Electron, Node.js
o librerie UI di terze parti: solo .NET e le API del sistema operativo.

Il codice Electron originale resta nella storia di `main` e nel tag
`archivio/electron`.

## Stato dell'implementazione

| Componente | Stato |
| --- | --- |
| `src/Core`: terminale, PTY, monitor, file, temi, tastiera, impostazioni, avvio, aggiornamenti, scena | pronto, coperto da MSTest |
| `src/Windows` (WPF): app a schermo intero e smoke test in CI | pronto |
| Applicazione Linux nativa (X11/Wayland, PTY POSIX) | da fare |
| Applicazione macOS nativa (Swift/Cocoa) | da fare |

Piano tecnico e regole di progetto: [SPEC.md](SPEC.md). Architettura:
[ARCHITECTURE.md](ARCHITECTURE.md). Priorità: [ROADMAP.md](ROADMAP.md).

## Requisiti

- .NET SDK 11 (versione esatta in `global.json`).
- Windows 10 1809 o successivo per ConPTY; Linux con glibc o musl; macOS.

## Compilazione e test

```sh
dotnet build EdexUI.slnx -c Release
dotnet test EdexUI.slnx -c Release
dotnet test EdexUI.slnx --coverage --coverage-output-format cobertura \
  --coverage-output cov.cobertura.xml
```

Applicazione Windows e smoke test:

```sh
dotnet build src/Windows/Edex.Windows.csproj -c Release
dotnet test tests/Windows.Tests/ -c Release
```

La build tratta gli avvisi come errori: il risultato atteso è zero errori e zero
avvisi, senza soppressioni di analisi.

## Sicurezza

- Nessun segreto nel repository; le impostazioni sensibili restano fuori dal
codice.
- `AppSettings.Validate()` e `SettingsStore.LoadStrict()` rifiutano valori non
  validi e segnalano gli errori di lettura senza mascherarli.
- `Safe.Run` e `UpdateChecker` registrano su `Trace` le eccezioni attese; i log
  non contengono dati sensibili.
- Segnalazioni private: vedere [SECURITY.md](SECURITY.md). Le GitHub Action sono
  pinnate a commit SHA completo.

## Contribuire

Leggere [CONTRIBUTING.md](CONTRIBUTING.md) e il
[codice di condotta](CODE_OF_CONDUCT.md).

## Licenza

GPL-3.0-only. Il testo integrale è in [LICENSE](LICENSE); le attribuzioni e le
licenze dei componenti di terze parti sono in [NOTICE.md](NOTICE.md).
