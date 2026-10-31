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

`--smoke-test <dir>` starts the app, runs `echo EDEX_SMOKE_OK` in the terminal, saves `screenshot.png`,
`terminal.txt` and `report.json` to `<dir>` and exits with 0 on success. CI uses it.

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

## What works / what is missing

Works: multiple terminal tabs on a real PTY with 256-color/truecolor, alternate screen (vim, htop, less), bracketed
paste and scrollback; live CPU/memory/process/disk/network panels with charts; file browser that follows clicks into
the shell; theme switching saved to settings.

Not ported yet from the Electron app: on-screen keyboard, globe/geolocation view, sound effects, mouse text selection,
built-in file viewers (PDF, images, media), mouse reporting to terminal apps.

## License

GPL-3.0-only, the same license as the original eDEX-UI (see `../LICENSE`). Third-party packages used: Avalonia
(MIT), SkiaSharp/HarfBuzzSharp (MIT, pulled in by Avalonia), xUnit (Apache-2.0, tests only). All are
GPL-3.0-compatible. No code was copied from the Electron version or from other terminal projects.
