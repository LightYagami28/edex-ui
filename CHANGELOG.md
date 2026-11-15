# Changelog

Le modifiche rilevanti sono elencate qui. Il formato segue
[Keep a Changelog](https://keepachangelog.com/it/1.1.0/) e il progetto usa
[Semantic Versioning](https://semver.org/lang/it/).

## [Unreleased]

Versione 3.0.0 in preparazione (`Directory.Build.props`).

### Aggiunto

- Riscrittura nativa in .NET 11 (`src/Core`): terminale VT/xterm con PTY
  (ConPTY e `forkpty`), monitor di CPU, memoria, rete e processi, file
  browser con ricerca fuzzy, globo, temi, tastiera su schermo, impostazioni,
  scorciatoie, audio sintetizzato, sequenza di avvio, controllo aggiornamenti.
- Test MSTest con copertura unita di Windows, Linux e macOS per SonarCloud.
- CI GitHub Actions su tre sistemi, SonarCloud, Dependabot giornaliero.
- Modelli di issue come moduli GitHub, modello di pull request, file
  `SUPPORT.md` e `SECURITY.md` per il progetto nativo.

### Modificato

- SDK .NET 11 in versione GA (`11.0.100`).
- Core C#: graffe obbligatorie, costanti al posto dei numeri magici e parser
  VT con tabelle di dispatch (complessità ridotta).
- Script di `tools/` rinominati in `coverage_to_sonar.py` e
  `gen_landmask.py`; l'unione della copertura non usa più `xml.etree`.

### Corretto

- Layout tastiera `da-DK` e `sv-SE`: chiave `alt_cmd` duplicata sul tasto E.

### Rimosso

- Codice Electron, archiviato nel tag `archivio/electron`.

## [2.3.0] - 2026-08-10

Ultima versione Electron: dipendenze aggiornate e vulnerabilità corrette.

[Unreleased]: https://github.com/LightYagami28/edex-ui/compare/v2.3.0...HEAD
[2.3.0]: https://github.com/LightYagami28/edex-ui/releases/tag/v2.3.0
