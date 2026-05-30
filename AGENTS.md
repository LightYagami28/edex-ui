# AGENTS.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Repository facts to know first
- This is an Electron app (`main: src/_boot.js`) with most runtime logic in plain JS under `src/`.
- There is no conventional unit-test suite in this repo; `npm test` is a security-oriented Snyk check run on a prebuilt copy of sources.
- No `WARP.md`, `CLAUDE.md`, `.cursorrules`, `.cursor/rules/*`, or `.github/copilot-instructions.md` were found in this repository at time of writing.

## Common commands
Run commands from repository root.

### Install / run
- Linux/macOS dev install (rebuilds native modules):
  - `npm run install-linux`
- Windows dev install (rebuilds native modules):
  - `npm run install-windows`
- Start app from source:
  - `npm run start`

### Lint / format
- Lint all JS (fails on warnings):
  - `npm run lint`
- Auto-fix lint issues:
  - `npm run lint:fix`
- Format source and root JS/JSON:
  - `npm run format`
- Check formatting without writing:
  - `npm run format:check`
- Lint a single file:
  - `npx eslint src/classes/terminal.class.js --max-warnings=0`

### Tests / validation
- Repo test command (security scan path, not unit tests):
  - `npm test`
  - Note: this runs `snyk test` inside a generated `prebuild-src` directory and may require Snyk auth.
- There is no script for running a single unit test because no unit-test framework is configured in `package.json`.

### Build distributables
- Build for host OS only (after `npm install`):
  - Linux: `npm run build-linux`
  - macOS: `npm run build-darwin`
  - Windows: `npm run build-windows`
- Build output goes to `dist/`.

### File-icon asset refresh
- Initialize icon submodule:
  - `npm run init-file-icons`
- Pull updates and regenerate icon assets:
  - `npm run update-file-icons`

## High-level architecture

### 1) Electron main process bootstraps app state and backend services
- Entry: `src/_boot.js`.
- Responsibilities:
  - Creates default user config files in Electron `userData` (`settings.json`, `shortcuts.json`, `lastWindowState.json`).
  - Mirrors bundled themes, keyboard layouts, and fonts from `src/assets/*` into `userData` on startup.
  - Resolves user shell and constructs clean shell environment.
  - Starts primary terminal backend and extra tab backends (up to 4 additional PTYs) using `Terminal` class in server role.
  - Creates BrowserWindow and loads `src/ui.html`.
  - Hosts IPC channels for logging, terminal tab spawning, and theme/keyboard hot-switch overrides.

### 2) Renderer builds UI dynamically and wires module classes
- Entry script loaded by `src/ui.html`: `src/_renderer.js`.
- `ui.html` mostly loads CSS + class scripts; `_renderer.js` composes runtime DOM and module instances.
- Renderer flow:
  1. Load settings/shortcuts from `userData`.
  2. Load theme and fonts, handle intro animation.
  3. Initialize UI panels and instantiate module classes (clock/sysinfo/cpu/ram/toplist/netstat/globe/etc.).
  4. Initialize terminal client tab 0, then lazy-create additional tabs via IPC (`ttyspawn`).
  5. Register global shortcuts and settings/shortcuts modal logic.

### 3) Terminal transport layer is a single class with dual roles
- File: `src/classes/terminal.class.js`.
- Same class is used for:
  - **Server role (main process):** spawns PTY via `node-pty`, exposes WS server (`ws`), tracks cwd/subprocess, relays state over Electron IPC.
  - **Client role (renderer):** creates xterm instance with addons (attach, fit, ligatures, webgl), connects to WS backend, forwards resize and receives cwd/process updates.
- This class is the core boundary between shell execution and visual terminal rendering.

### 4) System metrics are proxied to a worker cluster
- Files: `src/_multithread.js` + renderer proxy in `_renderer.js` (`window.si`).
- Main process launches cluster workers (capped CPU usage) dedicated to `systeminformation` calls.
- Renderer never calls `systeminformation` directly; module classes call `window.si.*`, which goes through IPC and returns async replies.
- Most dashboard modules (`sysinfo`, `ramwatcher`, `toplist`, `netstat`, `locationGlobe`) poll on intervals and update DOM incrementally.

### 5) Build pipeline uses a prebuild copy/minify step
- Build/test scripts create `prebuild-src/` from `src/`, run `prebuild-minify.js`, install deps there, then package.
- `prebuild-minify.js` minifies JS/CSS/JSON (with selective exclusions), so source readability and packaged output behavior may differ.
- Packaging configuration and artifact naming live in root `package.json` (`build` section, electron-builder).

## Important implementation details for edits
- Styling/theme behavior is strongly data-driven by JSON theme files in `src/assets/themes` and mirrored userData copies.
- Filesystem panel + terminal cwd tracking depends on server-side cwd detection in `Terminal` server role and renderer `FilesystemDisplay.followTab()`.
- Network/globe modules are coupled:
  - `Netstat` resolves connectivity and GeoIP data.
  - `LocationGlobe` consumes `window.mods.netstat` data for pin/marker updates.
- New UI modules should follow existing pattern: class under `src/classes`, CSS under `src/assets/css`, loaded by `src/ui.html`, then instantiated in `_renderer.js`.
