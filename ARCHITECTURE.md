# Architettura

eDEX-UI è un terminale a schermo intero nativo scritto in .NET 11, organizzato in architettura modulare disaccoppiata tra logica di dominio e sottosistema grafico.

```text
+--------------------------------------------------------------+
|                        src/Windows                           |
|       (Host WPF, Finestra Borderless, Rendering Direct2D)    |
+------------------------------+-------------------------------+
                               | richiama
                               v
+--------------------------------------------------------------+
|                         src/Core                             |
|  - Scene & Layout (UiScene, TerminalKeys, Hit-Test)          |
|  - Terminal Engine (ConPTY / POSIX PTY Stream Bridge)        |
|  - Monitor di Sistema (CPU, RAM, Rete, Processi, FS)         |
|  - Input Engine (Tastiera su schermo, Mappatura scancode)    |
|  - Tema & Configurazione (Safe.cs, JSON Parser nativo)       |
+--------------------------------------------------------------+
```

| Modulo | Percorso | Responsabilità |
|---|---|---|
| `Edex.Core` | `src/Core/` | Modello puro della scena a pannelli, elaborazione sequenze ANSI, acquisizione metriche hardware da OS, emulazione tastiera virtuale e gestione configurazione. Zero dipendenze esterne. |
| `Edex.Windows` | `src/Windows/` | Implementazione host su piattaforma Windows: finestra full-screen, binding a ConPTY (`CreatePseudoConsole`), rendering della scena tramite DrawingContext WPF ad alte prestazioni. |
| `Edex.Core.Tests` | `tests/Core.Tests/` | Suite di test unitari MSTest eseguiti con Microsoft.Testing.Platform. |
| `Edex.SmokeTest` | `tools/` e CI | Verifica automatica di avvio shell reale, echo su ConPTY, cattura screenshot della scena a 1280x800 e uscita pulita con codice 0. |

## Principi di progettazione

1. **Zero Garbage Collection overhead nel render loop**: strutture dati a dimensione fissa per buffer video e metriche di monitoraggio storiche circolari.
2. **Isolamento della PTY**: l'esecuzione della shell avviene in processo separato controllato via handle anonimi e pipe I/O asincrone.
3. **Indipendenza dal framework grafico**: `Edex.Core` calcola coordinate, rettangoli e comandi di disegno astratti; l'adapter di piattaforma si limita alla rasterizzazione.
