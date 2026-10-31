# eDEX-UI Next (.NET 11)

A from-scratch rewrite of eDEX-UI on .NET 11 and [Avalonia UI](https://avaloniaui.net/), without Electron.
It is a native desktop app: one shared core, one shared UI, and a thin launcher per OS.

> Status: **alpha**. The Windows build has been compiled and launched for real; the Linux build has been
> launched under X11; the macOS build is only cross-compiled (not run yet).

## Layout

| Folder | What it contains |
| --- | --- |
| `core/Edex.Core` | Platform-independent logic: PTY (Windows ConPTY, Unix `forkpty`), VT/xterm terminal emulator with scrollback, system monitor (CPU, memory, processes, disks), network monitor, file browser, themes, settings |
| `ui/Edex.UI` | Avalonia UI shared by every OS: main window, terminal control, charts, theme switcher |
| `win/Edex.Win` | Windows launcher (`eDEX-UI.exe`, per-monitor DPI manifest) |
| `linux/Edex.Linux` | Linux launcher (`edex-ui`) and `.desktop` file |
| `macos/Edex.MacOS` | macOS launcher (`eDEX-UI`), `Info.plist` and a script that builds `eDEX-UI.app` |
| `tests/Edex.Core.Tests` | xUnit tests for the core (terminal parser, PTY, themes, settings, parsers) |

## Why this stack

- **.NET 11** gives one language and runtime for all three OSes, self-contained single-file publishing and native interop
  for ConPTY / `forkpty` without extra helper processes.
- **Avalonia** (MIT) renders the same UI on Windows, Linux (X11) and macOS with GPU acceleration; .NET MAUI has no Linux
  desktop support and WinUI is Windows-only.
- The terminal emulator and the PTY layer are our own code, so there is no dependency on xterm.js or node-pty and no
  licensing ambiguity.

## Build

Requires the .NET 11 SDK (see `global.json`; currently a release candidate because .NET 11 is not GA yet).

```sh
cd app
dotnet build EdexUI.slnx
dotnet test --project tests/Edex.Core.Tests/Edex.Core.Tests.csproj
dotnet run --project win/Edex.Win        # or linux/Edex.Linux, macos/Edex.MacOS

# self-contained single-file builds
dotnet publish win/Edex.Win       -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o out/win-x64
dotnet publish linux/Edex.Linux   -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o out/linux-x64
dotnet publish macos/Edex.MacOS   -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o out/osx-arm64
sh macos/Edex.MacOS/packaging/make-bundle.sh out/osx-arm64 out/bundle
```

`--smoke-test <dir>` starts the app, types `echo EDEX_SMOKE_OK` through the on-screen keyboard, checks that a
select-all of the terminal contains the marker, opens the built-in viewer on a generated text file, PNG and PDF,
saves `screenshot.png`, `pdf-page.png`, `terminal.txt` and `report.json` to `<dir>` and exits with 0 on success.
CI runs it on Windows, Linux (xvfb) and macOS.

## Configuration

Settings live in `%APPDATA%\eDEX-UI-Next\settings.json` (Windows) or `~/.config/eDEX-UI-Next/settings.json`
(override with `EDEX_CONFIG_DIR`). Extra themes can be dropped as JSON files in the `themes` subfolder;
built-in themes: tron, amber, matrix, crimson, glacier.

## Keyboard

| Action | Windows / Linux | macOS |
| --- | --- | --- |
| Copy screen text | Ctrl+Shift+C | Cmd+C |
| Paste | Ctrl+Shift+V | Cmd+V |
| Scroll back | Shift+PgUp / Shift+PgDn, mouse wheel | same |
| Select all | Ctrl+Shift+A | Cmd+A |
| Select text | drag; double click = word, triple click = line (Shift+drag when an app captures the mouse) | same |
| Paste | middle click also pastes | same |

## What works

- Multiple terminal tabs on a real PTY with 256-color/truecolor, alternate screen (vim, htop, less), bracketed paste,
  scrollback, mouse selection and copy, mouse reporting to terminal apps (X10, normal, button and any-event tracking,
  SGR and legacy encodings).
- Live CPU/memory/process/disk/network panels with charts.
- On-screen keyboard (`KeyboardLayout`: `auto`, `en-US`, `it-IT`; `ShowKeyboard`) that types into the shell and lights
  up when physical keys are pressed.
- Sound effects synthesized at startup (no audio files): winmm on Windows, `pw-play`/`paplay`/`aplay` on Linux,
  `afplay` on macOS. `Sounds`, `KeyboardSounds` and `SoundVolume` control them.
- World view: a dotted globe drawn from an offline land mask with the approximate location of the public IP from
  ipwho.is (fallback ipapi.co), keyless. `OnlineGeolocation: false` keeps the app fully offline.
- File browser with category icons (native shell icons on Windows); clicking a file opens the built-in viewer for
  text, images and PDF, and hands audio, video and office documents to the system application. Right click offers
  "Insert path in terminal".
- Theme switching saved to settings.

## License

GPL-3.0-only, the same license as the original eDEX-UI (see `../LICENSE`). Third-party packages used: Avalonia
(MIT), SkiaSharp/HarfBuzzSharp (MIT, pulled in by Avalonia), Docnet.Core (MIT) with its bundled PDFium build
(BSD-3-Clause / Apache-2.0), xUnit (Apache-2.0, tests only). All are GPL-3.0-compatible. The globe land mask
(`core/Edex.Core/Geo/land-1deg.bin`) is generated by `tools/gen-landmask.py` from Natural Earth 1:110m land
polygons, which are in the public domain. No code was copied from the Electron version or from other terminal projects.
