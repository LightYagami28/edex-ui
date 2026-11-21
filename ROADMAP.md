# Roadmap

Stato al 2026-11-21. Il piano tecnico completo è documentato in SPEC.md.

Il documento riassume il lavoro candidato in linea con [SPEC.md](SPEC.md). Per
l'implementazione corrente vedere [README](README.md) e
[architettura](ARCHITECTURE.md).

## Stato attuale

- Solution `EdexUI.slnx` su .NET 11, comprendente `Edex.Core`, `Edex.Cli` e
  `Edex.Core.Tests`; build con zero errori e zero warning.
- Test: 226 totali, 224 superati e 2 ignorati per piattaforma (Windows/OSX).
- `AppSettings` espone `Validate()` con soglie numeriche e controllo dei percorsi;
  `SettingsStore` offre `LoadStrict()` e `SaveValidated()` per non nascondere
  errori di lettura o valori non validi, oltre a `Load()` best-effort con traccia.
- `Safe.Run` e `UpdateChecker` registrano con `Trace` le eccezioni attese, senza
  soppressioni del compilatore.
- SDK MSTest aggiornato a 4.5.3 con runner Microsoft.Testing.Platform.

## Fatto di recente

Convalida delle impostazioni, lettura e scrittura rigorose, logging delle
eccezioni attese, documentazione XML delle impostazioni e nuovi test dedicati.

## Prossimi passi in ordine di priorità

1. **Copertura test al 100%**: completare i test unitari in `tests/Core.Tests` per i rami di errore di PTY, parser ANSI e monitor di sistema.
2. **Quality Gate Codacy & SonarCloud Grade A**: mantenimento di 0 issue e complessità ciclomatica controllata su tutti i sorgenti C# senza l'uso di soppressori di codice (`#pragma` o `[SuppressMessage]`).
3. **Client nativo Linux**: implementazione di `src/Linux` con binding diretti X11/Wayland e PTY POSIX nativa (`forkpty`), azzerando ogni dipendenza da Electron.
4. **Client nativo macOS**: implementazione di `src/Mac` tramite Swift / Metal o host nativo Cocoa.
5. **Distribuzione e packaging**: generazione manifest Winget automatizzato (schema 1.10.0), pacchetto MSIX per Windows e tarball binari Linux deterministici.
6. **Supporto temi e tastiere personalizzate**: estensione del parser JSON interno per caricare layout ANSI/ISO e palette colori senza allocazioni dinamiche nel ciclo di rendering.

## Fuori ambito

- Ritorno a runtime Web/Electron o inclusione di Chromium/Node.js.
- Dipendenze NuGet pesanti di terze parti per UI o controlli complessi.
