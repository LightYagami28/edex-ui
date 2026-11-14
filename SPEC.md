# eDEX-UI nativo (.NET) - SPEC

Riscrittura da zero di eDEX-UI. Il vecchio codice Electron (JavaScript, `src/`) resta solo nella storia git (`master`).
Questa è l'unica fonte di verità per funzioni, architettura e piano.

## 1. Regole

- Runtime: **.NET 11** (`net11.0`), SDK fissato in `global.json`.
- **Nessuna libreria di terzi**: niente Avalonia, Electron, xterm.js, NuGet di terzi. Ammessi solo BCL/.NET e le API del sistema operativo (Win32, libc, X11, Cocoa, ecc. via P/Invoke o interop).
  Unica eccezione di sviluppo (non entra nel prodotto): framework di test MSTest e strumenti di coverage Microsoft.
- Codice con zero issue su SonarCloud (chiave `LightYagami28_edex-ui`) e Codacy A, coverage 100% dove c'è codice testabile, nessuna soppressione (`NOSONAR`, `#pragma warning disable`, `SuppressMessage`, esclusioni fittizie).
- Un ramo per sistema: `native/windows`, `native/linux`, `native/macos`, tutti nati da `native/base`. Nessuna unione in `master` senza decisione di Light.

## 2. Funzioni da riprodurre (dal codice Electron)

Mappa `classe JS -> modulo .NET`. Tra parentesi la parte che sta in Core (portabile) e quella per sistema (UI/native).

| Funzione eDEX-UI | Classe Electron | Core | Per sistema |
| --- | --- | --- | --- |
| Terminale con PTY, fino a 5 schede, copia/incolla, selezione, mouse, scrollback, cwd | `terminal.class.js`, `_boot.js` (node-pty) | `Pty` (ConPTY / forkpty), `Terminal` (emulatore VT/xterm, buffer, selezione, mouse) | disegno della griglia, input tastiera |
| Tastiera su schermo, 19 layout, suono tasto, modalità password, tasti modificatori e ripetizione | `keyboard.class.js`, `kb_layouts/*.json` | `Input` (layout, stato Shift/Ctrl/Alt/Caps, ripetizione) | disegno, hit-test del mouse/tocco |
| File browser che segue il cwd del terminale, icone, dotfile, vista lista/griglia, apertura file | `filesystem.class.js` | `Files` (listing, tipi, ordinamento, hidden) | disegno, apertura con l'app di sistema |
| Ricerca fuzzy dei file | `fuzzyFinder.class.js` | `Files.FuzzyFinder` | finestra di ricerca |
| Visualizzatori: testo (codifica rilevata, hex), immagini, PDF, audio/video | `docReader.class.js`, `mediaPlayer.class.js` | `Files.TextPreview` | immagini con decoder del sistema, PDF e media con l'app di sistema |
| Orologio (12/24 h), data, uptime, tipo OS, batteria | `clock.class.js`, `sysinfo.class.js` | `Monitoring.SystemInfo` | lettura batteria (API di sistema) |
| Hardware inspector (produttore, modello, chassis) | `hardwareInspector.class.js` | `Monitoring.HardwareInfo` | sorgente dati nativa (WMI/registro, `/sys/class/dmi`, `sysctl`) |
| CPU: carico per core, temperatura, frequenza, task | `cpuinfo.class.js` | `Monitoring.CpuCounters` | contatori nativi (`GetSystemTimes`, `/proc/stat`, `host_processor_info`) |
| RAM e swap, grafico a punti | `ramwatcher.class.js` | `Monitoring.MemoryInfo` | `GlobalMemoryStatusEx`, `/proc/meminfo`, `vm_stat`/`sysctl` |
| Top processi per CPU/RAM | `toplist.class.js` | `Monitoring.ProcessList` | enumerazione processi |
| Rete: interfacce, ping, traffico su/giù, stato online | `netstat.class.js`, `conninfo.class.js` | `Networking` | - |
| Globo con posizione, connessioni, satelliti | `locationGlobe.class.js` | `Geo.Globe` (maschera terre offline, proiezione ortografica, geolocalizzazione IP) | disegno |
| Temi (21 JSON, temi utente), caratteri, colori | `assets/themes/*.json`, `fonts/` | `Themes` | caricamento caratteri |
| Impostazioni e scorciatoie (JSON in cartella utente), dialoghi di modifica | `_boot.js`, `_renderer.js`, `modal.class.js` | `Settings`, `Shortcuts` | finestre modali |
| Audio: effetti (granted, denied, panels, keyboard, stdin/stdout, theme...) | `audiofx.class.js`, `assets/audio/*.wav` | `Audio` (decodifica WAV, sintesi) | riproduzione con API audio del sistema |
| Sequenza di avvio con log finto (`boot_log.txt`) e saluto | `_boot.js`, `boot_screen.css` | `Boot` (sequenza temporizzata, testo) | disegno |
| Controllo aggiornamenti | `updateChecker.class.js` | `Updates` (confronto versioni, controllo release GitHub) | - |
| Finestra a schermo intero, istanza unica, ultimo stato finestra | `_boot.js` | `Settings.WindowState` | finestra nativa |

Impostazioni (`settings.json`): `shell`, `shellArgs`, `cwd`, `keyboard`, `theme`, `termFontSize`, `audio`, `audioVolume`,
`disableFeedbackAudio`, `clockHours`, `pingAddr`, `nointro`, `nocursor`, `forceFullscreen`, `allowWindowed`,
`excludeThreadsFromToplist`, `hideDotfiles`, `fsListView`. I nomi restano quelli di eDEX-UI per poter riusare i file esistenti.
Scorciatoie predefinite (`shortcuts.json`): Ctrl+Shift+C/V copia/incolla, Ctrl+Tab e Ctrl+Shift+Tab cambio scheda, Ctrl+1..5 scheda,
Ctrl+Shift+S impostazioni, Ctrl+Shift+K scorciatoie, Ctrl+Shift+F ricerca fuzzy, Ctrl+Shift+L vista lista, Ctrl+Shift+H dotfile,
Ctrl+Shift+P modalità password; scorciatoie "shell" che scrivono un comando nel terminale.

Fuori scopo: moduli Node/Electron (`multithread`, `ipc`), `--nointro` via riga di comando oltre al campo impostazione, plugin di terze parti.

## 3. Architettura

```
Core (netstandard-free, net11.0, nessuna dipendenza)
  Pty, Terminal, Input, Files, Monitoring, Networking, Geo, Themes, Settings, Shortcuts, Audio, Boot, Updates
  Ui (modello di scena senza disegno): layout a pannelli, stato di ogni modulo, hit-test, eventi
  Render (rasterizzatore software 2D su bitmap: rettangoli, linee, testo con font bitmap incorporato)
Per sistema (solo finestra, input, blit/disegno, chiamate native)
  Windows : WPF (incluso in .NET), disegno con DrawingContext e caratteri di sistema, ConPTY
  Linux   : X11 via P/Invoke (libX11), blit dei pixel del Render con XPutImage, forkpty (libc), /proc, /sys
  macOS   : AppKit via runtime Objective-C (objc_msgSend), CoreGraphics, forkpty (libSystem), sysctl
```

Il modello di scena (`Core.Ui`) decide cosa si vede in ogni pannello e a che dimensione; il sistema lo disegna.
Su Windows si usa WPF direttamente. Su Linux e macOS, dove non c'è un toolkit incluso in .NET, la scena viene rasterizzata da `Core.Render`
(stesso aspetto su tutte le distribuzioni, nessuna libreria grafica di terzi, solo la finestra nativa).

Test: tutto Core è testabile senza finestra. Il codice per sistema è sottile (finestra, eventi, blit) ed è coperto da test di fumo
(`--smoke-test`: avvia, scrive un comando nel terminale, controlla l'output, salva uno screenshot, esce con 0).

## 4. Struttura cartelle (uguale in ogni ramo, salvo le parti del sistema)

```
SPEC.md  README.md  Directory.Build.props  EdexUI.slnx  sonar-project.properties
assets/              temi, layout tastiera, audio, caratteri, boot_log.txt (dati condivisi)
src/Core/            libreria comune Edex.Core
src/Windows/         [solo native/windows]  app WPF
src/Linux/           [solo native/linux]    app X11
src/MacOS/           [solo native/macos]    app AppKit
tests/Core.Tests/    test di Core
tools/               script di sviluppo
.github/workflows/   CI

windows/   [native/windows]  winget/  chocolatey/  scoop/  msix/
linux/     [native/linux]    debian/  ubuntu/  fedora/  arch/  opensuse/  alpine/   (ognuna: packaging nativo + README)
macos/     [native/macos]    homebrew/  macports/  dmg/
```

Rami: `native/base` (SPEC, Core, test, CI comune) -> `native/windows`, `native/linux`, `native/macos`.
Ogni ramo di sistema contiene Core (copia identica, aggiornata dal base) più la sua UI e le sue cartelle di pacchetto.

Pacchetti per cartella:

| Cartella | Formato | Dove si costruisce |
| --- | --- | --- |
| windows/winget | manifest YAML + zip self-contained | CI windows |
| windows/chocolatey | `.nuspec` + `chocolateyInstall.ps1` | CI windows |
| windows/scoop | `edex-ui.json` (bucket) | CI windows |
| windows/msix | `AppxManifest.xml`, pacchetto `.msix` | CI windows |
| linux/debian, linux/ubuntu | `.deb` (`dpkg-deb`), `control`, `.desktop` | container `debian:stable` / `ubuntu:latest` |
| linux/fedora, linux/opensuse | `.rpm` (`.spec`) | container `fedora:latest` / `opensuse/tumbleweed` |
| linux/arch | `PKGBUILD` | container `archlinux` |
| linux/alpine | `APKBUILD` (musl: build `linux-musl-x64`) | container `alpine:latest` |
| macos/homebrew | cask/formula `edex-ui.rb` | CI macos |
| macos/macports | `Portfile` | CI macos |
| macos/dmg | `.app` + `.dmg` (`hdiutil`) | CI macos |

## 5. Piano e stato

| Fase | Contenuto | Verifica |
| --- | --- | --- |
| A | Questa SPEC, struttura, pulizia del vecchio codice | revisione |
| B | `src/Core` completo + test MSTest, coverage 100%, CI + SonarCloud | `dotnet test` con coverage, issue Sonar a 0 |
| C1 | `native/windows`: app WPF che parte, terminale e pannelli funzionanti; pacchetti winget/choco/scoop/msix | build e avvio su Windows 11, smoke test |
| C2 | `native/linux`: app X11 + 6 pacchetti | build e smoke test in CI (ubuntu, xvfb) e nei container |
| C3 | `native/macos`: app AppKit + 3 pacchetti | build in CI (macos); avvio solo se il runner lo permette |

Stato fase B (Core): fatto. Contiene Pty, Terminal, Input (con `TerminalKeys` per le sequenze di controllo), Files (con FuzzyFinder), Monitoring (con HardwareInfo),
Networking, Geo, Themes, Settings (con Shortcuts), Audio, Boot, Updates e `Ui` (scena, layout, hit-test). 215 test, copertura di riga completa su Windows + Linux;
restano non misurabili qui solo le righe macOS (contatori `host_statistics`, `sysctl`), misurate dal job macOS della CI.

Stato fase C1 (Windows): fatto. `src/Windows` è l'app WPF che disegna la scena di Core con `DrawingContext`: terminale ConPTY, file, tastiera su schermo,
CPU, memoria, rete, processi e globo. `--smoke-test` avvia la shell, esegue un comando, ne verifica l'output e salva uno screenshot; la CI lo esegue su Windows.
Restano da fare nella fase C1 i pacchetti winget/choco/scoop/msix con il contenitore reale al posto dell'eseguibile minimo provvisorio.

Differenze rispetto alla tabella delle funzioni:
- Le impostazioni usano nomi propri (`AppSettings`: Shell, Theme, FontSize, ...), non i campi di eDEX-UI; non c'è import dei vecchi `settings.json`.
- Layout tastiera: i 19 JSON di `assets/kb_layouts`, incorporati in Core.
- `Ui` (scena) è in Core (`src/Core/Ui`): layout dei pannelli, scena con hit-test, tastiera su schermo posizionata, globo proiettato.
  Il `Render` (rasterizzatore software per Linux e macOS) non è ancora in Core: si scrive nelle fasi C2 e C3 insieme alle finestre.
- Processi: l'elenco dei più attivi è in `SystemMonitor.Sample`; `HardwareInfo` legge produttore, modello e chassis.

## 6. Rischi e decisioni aperte

- Licenza: il repo è un fork di eDEX-UI (GPL-3.0). Il nuovo codice è scritto da zero ma vive nello stesso repo: se Light vuole una licenza proprietaria
  serve un repo nuovo senza la storia GPL. Per ora i file portano `SPDX-License-Identifier: GPL-3.0-only`.
- Test: MSTest (Microsoft) al posto di xUnit (terzi). Coverage con `Microsoft.CodeCoverage`.
- Linux e macOS non sono eseguibili su questa macchina Windows: il loro stato "funziona" è quello dei runner CI e dei container, non dimostrabile in locale.
- .NET 11 è in GA dal 2026-11-10: `global.json` punta all'SDK `11.0.100` senza prerelease.

## 7. Evoluzione: .NET 11 definitivo e versione macOS in Swift (2026-11-10)

Decisione di Light: portare tutto su .NET 11 in versione definitiva e sviluppare la versione macOS in Swift. Questa sezione descrive il lavoro; lo sviluppo lo fa chi prende in carico il ramo.

Stato verificato il 2026-11-10:
- `global.json` usa l'SDK `11.0.100-rc.3.26522.148` con `allowPrerelease: true`; `Directory.Build.props` ha `TargetFramework` `net11.0`.
- .NET 11 è ancora release candidate «go-live» (`11.0.0-rc.3`); l'ultima versione stabile è .NET 10 LTS (`10.0.14`). Fonte: `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json`.

Stato verificato il 2026-11-14:
- `global.json` usa l'SDK GA `11.0.100` con `allowPrerelease: false` e `rollForward: latestFeature`; `Directory.Build.props` ha `TargetFramework` `net11.0`.
- .NET 11 è in GA (`11.0.0`, rilasciata il 2026-11-10, canale STS, fine supporto 2028-11-14); l'ultima LTS è .NET 10 (`10.0.15`). Fonte: `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json`.
- Build Release con .NET SDK `11.0.100`: 0 avvisi, 0 errori.

### 7.1 .NET 11 definitivo

- Fatto (2026-11-14): `global.json` sulla versione GA `11.0.100`, `allowPrerelease: false`, `rollForward: latestFeature`.
- CI: `actions/setup-dotnet` su `11.0.x`, stesse verifiche di oggi.
- Pacchetti di sviluppo (MSTest, `Microsoft.CodeCoverage`) all'ultima versione compatibile con .NET 11.
- Build senza avvisi (`dotnet build -warnaserror`), nessuna API obsoleta (CS0618, CS0612, SYSLIB*).
- Fatto quando: `dotnet test` verde con copertura del Core invariata, 0 issue su SonarCloud, smoke test Windows e Linux verdi in CI.

### 7.2 Versione macOS in Swift

Sostituisce la riga «macOS: AppKit via runtime Objective-C» della sezione 3 e la fase C3 della sezione 5.

- App nativa in Swift, solo framework Apple (SwiftUI e/o AppKit, CoreGraphics, Foundation): resta valida la regola «nessuna libreria di terzi».
- Ramo `native/macos`, codice in `platform/macos/` (Swift Package o progetto Xcode), test con Swift Testing o XCTest.
- Logica condivisa, due strade da scegliere:
  1. **Core .NET come libreria nativa**: Core compilato con NativeAOT in una libreria dinamica per macOS con funzioni esportate (`UnmanagedCallersOnly`), chiamata da Swift. Una sola fonte di logica e test esistenti riusati.
  2. **Core riscritto in Swift**: nessun runtime .NET nell'app, ma doppia logica da mantenere e da testare.
- Toolchain: l'interfaccia (SwiftUI/AppKit) si compila solo su macOS con Xcode. Lo Swift per Windows (swift.org) serve solo a compilare e testare codice Swift senza interfaccia. Build e test dell'app vanno sul runner `macos-latest` di GitHub Actions o su un Mac.
- Distribuzione: `.app` firmata e notarizzata (serve un account Apple Developer), immagine `.dmg`, cask Homebrew.
- Fatto quando: build e test verdi sul runner macOS, smoke test (`--smoke-test`: avvia, scrive un comando nel terminale, controlla l'output, salva uno screenshot, esce con 0), app firmata e notarizzata.

### 7.3 Decisioni aperte

- Core condiviso via NativeAOT oppure riscritto in Swift.
- SwiftUI, AppKit o entrambi.
- Versione minima di macOS da supportare.
- Account Apple Developer per firma e notarizzazione.
- Licenza GPL-3.0 del repo (vedi sezione 6).
