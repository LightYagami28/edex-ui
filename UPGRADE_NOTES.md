# eDEX-UI Upgrade Notes

## Overview
This document describes the major upgrades made to the eDEX-UI codebase to modernize dependencies, improve security, and ensure compatibility with current Node.js and Electron versions.

## Date
February 14, 2026

## Major Changes

### 1. Electron Upgrade (12.1.0 → 35.7.5)
- **Why**: Electron 12 reached end-of-life and had multiple security vulnerabilities
- **Impact**: Brings 3+ years of security patches, performance improvements, and bug fixes
- **Breaking Changes**:
  - Removed deprecated `enableRemoteModule` from BrowserWindow preferences
  - Migrated from built-in `electron.remote` to `@electron/remote` module
  - Required initialization of `@electron/remote/main` in main process

### 2. Terminal Emulator Upgrade (xterm 4.14.1 → @xterm/xterm 5.5.0)
- **Why**: Original `xterm` package was deprecated in favor of scoped `@xterm/*` packages
- **Impact**: Access to latest terminal features, bug fixes, and performance improvements
- **Breaking Changes**:
  - Changed package names:
    - `xterm` → `@xterm/xterm`
    - `xterm-addon-attach` → `@xterm/addon-attach`
    - `xterm-addon-fit` → `@xterm/addon-fit`
    - `xterm-addon-ligatures` → `@xterm/addon-ligatures`
    - `xterm-addon-webgl` → `@xterm/addon-webgl`
  - Updated all import statements in `src/classes/terminal.class.js`

### 3. Build Tools Upgrade
- **electron-rebuild** → **@electron/rebuild** (4.0.3)
  - New official package with better maintenance
  - Fixes tar vulnerabilities in dependencies
- **electron-builder**: 22.14.5 → 26.8.0
  - 3 major versions upgrade
  - Better support for modern Electron versions
- **terser**: 5.9.0 → 5.37.0
- **clean-css**: 5.2.1 → 5.3.3
- **node-abi**: 2.30.1 → 3.71.0
- **node-json-minify**: 1.0.0 → 3.0.0

### 4. Application Dependencies
| Package | Old Version | New Version | Notes |
|---------|-------------|-------------|-------|
| @electron/remote | 1.2.2 | 2.1.2 | Required for Electron 35+ |
| systeminformation | 5.9.7 | 5.23.18 | System monitoring |
| ws | 7.5.5 | 8.18.0 | WebSocket security updates |
| pdfjs-dist | 2.11.338 | 4.9.155 | 2+ years of updates |
| node-pty | 0.10.1 | 1.1.0 | Terminal process handling |
| augmented-ui | 1.1.2 | 2.0.0 | UI framework |
| color | 3.2.1 | 4.2.3 | Color manipulation |
| nanoid | 3.1.30 | 3.3.8 | ID generation |
| pretty-bytes | 5.6.0 | 7.1.0 | Byte formatting |
| shell-env | 3.0.1 | 4.0.3 | Environment variables |
| username | 5.1.0 | 7.0.0 | User detection |
| which | 2.0.2 | 6.0.1 | Path resolution |

### 5. CI/CD Infrastructure
- **Node.js**: 14 → 18 LTS
  - Node 14 reached end-of-life in April 2023
  - Node 18 is current LTS with support until April 2025
- **npm**: v7 → v10
- **GitHub Actions**:
  - `actions/checkout`: v2 → v4
  - `actions/cache`: v2 → v4
  - `actions/upload-artifact`: v2 → v4
  - `docker/setup-qemu-action`: v1 → v3
  - `github/codeql-action/*`: v1 → v3
  - `actions/setup-node`: v1 → v4
- **Docker Images**: node:14-buster → node:18-bookworm
- **Deprecated GitHub Actions syntax fixed**:
  - `::set-output name=...` → `>> $GITHUB_OUTPUT`

### 6. Code Quality Tools Added
- **ESLint** (9.18.0): JavaScript linting
  - Configuration in `.eslintrc.json`
  - Run with `npm run lint` or `npm run lint:fix`
- **Prettier** (3.4.2): Code formatting
  - Configuration in `.prettierrc.json`
  - Run with `npm run format` or `npm run format:check`

## Security Improvements

### Vulnerabilities Fixed
- **Before**: 6 vulnerabilities (1 moderate, 5 high)
- **After**: 0 vulnerabilities

### Key Security Fixes
1. Electron ASAR integrity bypass (GHSA-vmqv-hx8q-j7mg) - Fixed by upgrading to 35.7.5+
2. Multiple tar vulnerabilities - Fixed by upgrading @electron/rebuild to 4.0.3+
3. WebSocket (ws) vulnerabilities - Fixed by upgrading to 8.18.0
4. Outdated dependencies with known CVEs

## API Changes

### electron.remote → @electron/remote
All instances of `electron.remote` have been replaced with the `@electron/remote` module:

**Before:**
```javascript
const electron = require("electron");
electron.remote.app.getVersion();
electron.remote.getCurrentWindow();
```

**After:**
```javascript
const remote = require("@electron/remote");
remote.app.getVersion();
remote.getCurrentWindow();
```

### xterm → @xterm/xterm
Terminal import paths updated:

**Before:**
```javascript
const {Terminal} = require("xterm");
const {AttachAddon} = require("xterm-addon-attach");
```

**After:**
```javascript
const {Terminal} = require("@xterm/xterm");
const {AttachAddon} = require("@xterm/addon-attach");
```

## Compatibility Notes

### Node.js
- **Minimum Required**: Node.js 18.x
- **Recommended**: Node.js 18.x LTS or 20.x LTS

### Operating Systems
- **Linux**: Tested with Ubuntu 22.04+ (bookworm)
- **macOS**: Should work with macOS 10.15+
- **Windows**: Should work with Windows 10+

### Build Requirements
- For ARM builds (ARM32/ARM64), QEMU is required
- Cross-compilation for x86/x64 requires appropriate toolchains

## Migration Guide

### For Developers

1. **Update Node.js**:
   ```bash
   # Install Node 18 LTS
   nvm install 18
   nvm use 18
   ```

2. **Clean Install**:
   ```bash
   rm -rf node_modules package-lock.json
   rm -rf src/node_modules src/package-lock.json
   npm install
   cd src && npm install
   ```

3. **Rebuild Native Modules** (Linux):
   ```bash
   npm run install-linux
   ```

4. **Run Linting**:
   ```bash
   npm run lint
   npm run format:check
   ```

### For Users

The upgrade is transparent for end users. Pre-built binaries will include all updates.

## Testing Recommendations

1. **Unit Tests**: Run existing test suite (if available)
2. **Integration Tests**: 
   - Test terminal functionality
   - Test file browser
   - Test system monitoring displays
   - Test keyboard shortcuts
   - Test theme switching
3. **Platform Tests**: Test on Linux, macOS, and Windows
4. **Performance**: Monitor memory usage and rendering performance

## Known Issues

1. **Deprecated npm warnings**: Some transitive dependencies still use deprecated packages (glob, inflight, rimraf) - these are in sub-dependencies and don't affect functionality
2. **Boolean package deprecation**: The `boolean` package (used by sub-dependencies) is deprecated but still functional

## Rollback Plan

If issues arise, you can rollback by:
```bash
git checkout <previous-commit-hash>
rm -rf node_modules src/node_modules
npm install
cd src && npm install
```

## Future Recommendations

1. **Consider TypeScript migration** for better type safety
2. **Add Jest or Vitest** for proper unit testing
3. **Consider migrating to Electron Forge** for better build tooling
4. **Update file-icons submodule** dependencies
5. **Regular dependency updates** (quarterly review recommended)

## References

- [Electron 35 Release Notes](https://www.electronjs.org/blog/electron-35-0)
- [@electron/remote Documentation](https://github.com/electron/remote)
- [xterm.js Migration Guide](https://github.com/xtermjs/xterm.js/releases)
- [Node.js 18 Release Notes](https://nodejs.org/en/blog/release/v18.0.0)

## Contact

For questions or issues related to this upgrade, please open an issue on the GitHub repository.
