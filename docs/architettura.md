# Architettura

`Edex.Core` non disegna nulla e non chiama librerie esterne. Ogni applicazione di sistema usa Core per i dati e disegna con le proprie API.

| Cartella | Contenuto |
| --- | --- |
| `Pty` | pseudo terminale: ConPTY su Windows, `forkpty` su Linux e macOS (`IPtyConnection`, `PtyFactory`) |
| `Terminal` | emulatore VT/xterm (`TerminalBuffer`), sessione shell (`TerminalSession`), selezione, mouse |
| `Input` | tastiera su schermo: layout, modificatori, sequenze da inviare |
| `Files` | elenco cartelle, tipi di file, anteprima testo, ricerca fuzzy |
| `Monitoring` | CPU, memoria, dischi, processi, hardware |
| `Networking` | interfaccia principale, traffico, ping, connessioni TCP |
| `Geo` | maschera delle terre, proiezione ortografica, geolocalizzazione dell'IP pubblico |
| `Themes` | temi JSON |
| `Settings` | impostazioni e scorciatoie in JSON |
| `Audio` | effetti sonori sintetizzati (WAV) |
| `Boot` | testo e tempi della schermata di avvio |
| `Updates` | confronto versioni e controllo release GitHub |

## Codice per sistema

Le letture dal sistema stanno dietro `Safe.Run`: se il sistema rifiuta la lettura si usa un valore di ripiego, senza eccezioni verso la UI.
`NetworkMonitor` legge il sistema tramite `INetworkProbe`, così la logica si prova con dati finti.
`TerminalSession.Attach` accetta una connessione PTY finta per i test.

Il codice che esiste solo su un sistema (contatori Linux, `sysctl` di macOS, registro di Windows) è coperto dal job di test di quel sistema.
