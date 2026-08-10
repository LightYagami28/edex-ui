// Main-process IPC handlers backing the window.eDEX bridge exposed by preload.js.
// Registered once from _boot.js after `app` is ready and config paths are known.

const { app, ipcMain, shell, clipboard, screen, globalShortcut } = require("electron");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const https = require("node:https");
const net = require("node:net");

function register({ win, paths }) {
    const { settingsFile, shortcutsFile, lastWindowStateFile, themesDir, kblayoutsDir } = paths;

    // ---- App / window ----
    ipcMain.handle("app:getVersion", () => app.getVersion());
    ipcMain.handle("app:getElectronVersion", () => process.versions.electron);
    ipcMain.handle("app:getPath", (e, name) => app.getPath(name));
    ipcMain.handle("app:getArgv", () => process.argv);
    ipcMain.on("app:focus", () => app.focus());
    ipcMain.on("app:relaunch", () => {
        app.relaunch();
        app.quit();
    });
    ipcMain.on("app:quit", () => app.quit());

    ipcMain.on("win:minimize", () => win.minimize());
    ipcMain.handle("win:isFullScreen", () => win.isFullScreen());
    ipcMain.on("win:setFullScreen", (e, state) => win.setFullScreen(state));
    ipcMain.handle("win:isMaximized", () => win.isMaximized());
    ipcMain.on("win:unmaximize", () => win.unmaximize());
    ipcMain.handle("win:getSize", () => win.getSize());
    ipcMain.on("win:setSize", (e, w, h) => win.setSize(w, h));
    ipcMain.on("win:toggleDevTools", () => win.webContents.toggleDevTools());
    win.on("resize", () => {
        if (!win.isDestroyed()) win.webContents.send("win:resize");
    });
    win.on("leave-full-screen", () => {
        if (!win.isDestroyed()) win.webContents.send("win:leave-full-screen");
    });

    ipcMain.handle("clipboard:readText", () => clipboard.readText());

    // Restrict to http(s): shell.openExternal with an unvalidated scheme can
    // be abused via crafted file:// or custom-protocol-handler URLs on some
    // platforms. The only caller (updateChecker.class.js) only ever passes
    // a github.com release URL from GitHub's own API response.
    ipcMain.on("shell:openExternal", (e, url) => {
        try {
            const parsed = new URL(url);
            if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return;
        } catch {
            return;
        }
        shell.openExternal(url);
    });
    // shell:openPath/fs:* intentionally accept arbitrary paths - see note
    // below. This app bundles a real terminal (a real shell, via node-pty),
    // so a user (or anything already running in that terminal) already has
    // unrestricted OS-level file access; these handlers don't grant new
    // capability beyond what the embedded terminal already provides.
    ipcMain.on("shell:openPath", (e, filePath) => shell.openPath(filePath));

    ipcMain.handle("screen:getAllDisplays", () => screen.getAllDisplays());

    ipcMain.handle("globalShortcut:register", (e, accelerator, id) => {
        return globalShortcut.register(accelerator, () => {
            if (!win.isDestroyed()) win.webContents.send("globalShortcut:triggered", id);
        });
    });
    ipcMain.on("globalShortcut:unregisterAll", () => globalShortcut.unregisterAll());

    // ---- Config file IO ----
    ipcMain.handle("config:getPaths", () => paths);

    ipcMain.handle("config:getSettings", () => JSON.parse(fs.readFileSync(settingsFile, "utf-8")));
    ipcMain.handle("config:getShortcuts", () => JSON.parse(fs.readFileSync(shortcutsFile, "utf-8")));
    ipcMain.handle("config:getLastWindowState", () => JSON.parse(fs.readFileSync(lastWindowStateFile, "utf-8")));

    ipcMain.handle("config:getTheme", (e, name) => {
        return JSON.parse(fs.readFileSync(path.join(themesDir, `${name}.json`), "utf-8"));
    });
    ipcMain.handle("config:getKeyboardLayout", (e, name) => {
        return JSON.parse(fs.readFileSync(path.join(kblayoutsDir, `${name}.json`), "utf-8"));
    });
    ipcMain.handle("config:writeSettings", (e, settings) => {
        fs.writeFileSync(settingsFile, JSON.stringify(settings, "", 4));
    });

    // ---- Filesystem browsing ----
    // Intentionally accepts arbitrary paths: this app is a file manager
    // driven by the terminal's own cwd, and the user already has a real
    // shell. The point of this bridge is removing incidental raw Node
    // access from the renderer, not sandboxing the user from their own
    // filesystem.
    ipcMain.handle("fs:readdir", (e, dirPath) => fs.promises.readdir(dirPath));
    ipcMain.handle("fs:lstat", async (e, filePath) => {
        const s = await fs.promises.lstat(filePath);
        return {
            isDirectory: s.isDirectory(),
            isFile: s.isFile(),
            isSymbolicLink: s.isSymbolicLink(),
            size: s.size,
            mtimeMs: s.mtimeMs,
        };
    });
    ipcMain.handle("fs:readFile", (e, filePath, encoding) => fs.promises.readFile(filePath, encoding));
    ipcMain.handle("fs:writeFile", (e, filePath, data) => fs.promises.writeFile(filePath, data));
    ipcMain.handle("fs:exists", (e, filePath) => fs.existsSync(filePath));

    const watchers = new Map();
    ipcMain.handle("fs:watch", (e, dirPath) => {
        if (watchers.has(dirPath)) return;
        const sender = e.sender;
        const watcher = fs.watch(dirPath, (eventType) => {
            if (!sender.isDestroyed()) sender.send("fs:watchEvent", dirPath, eventType);
        });
        watchers.set(dirPath, watcher);
    });
    ipcMain.handle("fs:unwatch", (e, dirPath) => {
        const watcher = watchers.get(dirPath);
        if (watcher) {
            watcher.close();
            watchers.delete(dirPath);
        }
    });

    // ---- GeoIP ----
    let geoLookup = null;
    (async () => {
        try {
            const geolite2 = await import("geolite2-redist");
            const maxmind = require("maxmind");
            const geoIPcachePath = path.join(app.getPath("userData"), "geoIPcache");
            await geolite2.downloadDbs({ path: geoIPcachePath });
            geoLookup = await geolite2.open("GeoLite2-City", (p) => maxmind.open(p), geoIPcachePath);
        } catch (err) {
            console.warn("GeoIP database unavailable:", err);
        }
    })();
    ipcMain.handle("geoip:lookup", (e, ip) => {
        if (!geoLookup) return null;
        return geoLookup.get(ip);
    });

    // ---- Network helpers ----
    ipcMain.handle("net:ping", (e, target, port, localAddress) => {
        return new Promise((resolve, reject) => {
            const s = new net.Socket();
            const start = process.hrtime();
            s.connect({ port, host: target, localAddress, family: 4 }, () => {
                const timeArr = process.hrtime(start);
                resolve((timeArr[0] * 1e9 + timeArr[1]) / 1e6);
                s.destroy();
            });
            s.on("error", (err) => {
                s.destroy();
                reject(err);
            });
            s.setTimeout(1900, () => {
                s.destroy();
                reject(new Error("Socket timeout"));
            });
        });
    });

    ipcMain.handle("net:getExternalIp", (e, localAddress) => {
        return new Promise((resolve, reject) => {
            https
                .get({ host: "myexternalip.com", port: 443, path: "/json", localAddress }, (res) => {
                    let rawData = "";
                    res.on("data", (chunk) => (rawData += chunk));
                    res.on("end", () => {
                        try {
                            resolve(JSON.parse(rawData).ip);
                        } catch (err) {
                            reject(err);
                        }
                    });
                })
                .on("error", reject);
        });
    });

    ipcMain.handle("net:checkForUpdate", () => {
        return new Promise((resolve, reject) => {
            https
                .get(
                    {
                        protocol: "https:",
                        host: "api.github.com",
                        path: "/repos/lightyagami28/edex-ui/releases/latest",
                        headers: { "User-Agent": "eDEX-UI UpdateChecker" },
                    },
                    (res) => {
                        if (res.statusCode !== 200) {
                            reject(new Error(`GitHub API returned ${res.statusCode}`));
                            return;
                        }
                        let rawData = "";
                        res.on("data", (chunk) => (rawData += chunk));
                        res.on("end", () => {
                            try {
                                resolve(JSON.parse(rawData));
                            } catch (err) {
                                reject(err);
                            }
                        });
                    }
                )
                .on("error", reject);
        });
    });

    // ---- Misc ----
    ipcMain.handle("os:uptime", () => os.uptime());
    ipcMain.handle("os:username", async () => {
        try {
            return await (await import("username")).username();
        } catch {
            return null;
        }
    });

    const mime = require("mime-types");
    ipcMain.handle("mime:lookup", (e, extOrPath) => mime.lookup(extOrPath));
    ipcMain.handle("mime:charset", (e, mimeType) => mime.charset(mimeType));
}

module.exports = { register };
