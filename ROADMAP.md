# Roadmap

Stato reale al 2026-11-21. Il piano tecnico completo è in [SPEC.md](SPEC.md);
l'architettura in [ARCHITECTURE.md](ARCHITECTURE.md).

## Stato attuale

- Solution `EdexUI.slnx` su .NET 11 con `Edex.Core`, `Edex.Cli` ed
  `Edex.Core.Tests`; build con zero errori e zero avvisi.
- Test MSTest: 226 totali, 224 superati e 2 ignorati per piattaforma (Windows
  e macOS).
- `AppSettings.Validate()` verifica soglie numeriche e percorsi assoluti;
  `SettingsStore.LoadStrict()` e `SaveValidated()` segnalano errori di lettura e
  valori non validi, mentre `Load()` resta best-effort ma tracciato.
- `Safe.Run` e `UpdateChecker` registrano su `Trace` le eccezioni attese, senza
  soppressioni del compilatore.
- SDK MSTest 4.5.3 con runner Microsoft.Testing.Platform; GitHub Action pinnate
  a SHA completo.

## Obiettivi

1. Mantenere build e test con zero errori e zero avvisi, senza soppressioni.
2. Completare la copertura dei rami di errore di PTY, parser ANSI e monitor.
3. Realizzare i client nativi Linux e macOS, eliminando ogni dipendenza da
   Electron.
4. Completare packaging e distribuzione (Winget, MSIX, tarball deterministici).
5. Rafforzare la postura di sicurezza secondo NIST CSF 2.0 / SSDF: validazione
   input, output encoding, audit log senza dati sensibili, errori senza leak.

## Prossimi passi in ordine di priorità

1. **Copertura test al 100%**: completare i test in `tests/Core.Tests` per i
rami
   di errore di PTY, parser ANSI e monitor di sistema.
2. **Quality Gate Codacy e SonarCloud Grade A**: mantenere zero issue e
   complessità ciclomatica controllata senza soppressori di codice.
3. **Client nativo Linux**: implementare `src/Linux` con binding X11/Wayland e
   PTY POSIX nativa (`forkpty`).
4. **Client nativo macOS**: implementare `src/Mac` tramite Swift / Metal o host
   Cocoa nativo.
5. **Distribuzione e packaging**: manifest Winget (schema 1.10.0), MSIX per
   Windows e tarball Linux deterministici con hash.
6. **Temi e tastiere personalizzate**: estendere il parser JSON interno per
   layout ANSI/ISO e palette senza allocazioni nel ciclo di rendering.

## Sicurezza e catena di fornitura

- Verificare `dotnet list package --vulnerable --include-transitive` a ogni PR.
- Nessun segreto hardcoded; token e credenziali restano fuori dal repository.
- Log e messaggi di errore non devono contenere contenuto del terminale o dati
  personali.
- Aggiornare le dipendenze solo a versioni stabili, documentando le migrazioni.

## Fuori ambito

- Ritorno a runtime Web/Electron o inclusione di Chromium/Node.js.
- Dipendenze NuGet pesanti di terze parti per UI o controlli complessi.
