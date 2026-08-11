const signale = require("./logger.js");
const { app, BrowserWindow, dialog, shell } = require("electron");

process.on("uncaughtException", (e) => {
    signale.fatal(e);
    dialog.showErrorBox("eDEX-UI crashed", e.message || "Cannot retrieve error message.");
    if (tty) {
        tty.close();
    }
    if (extraTtys) {
        Object.keys(extraTtys).forEach((key) => {
            if (extraTtys[key] !== null) {
                extraTtys[key].close();
            }
        });
    }
    process.exit(1);
});

signale.start(`Starting eDEX-UI v${app.getVersion()}`);
signale.info(`With Node ${process.versions.node} and Electron ${process.versions.electron}`);
signale.info(`Renderer is Chrome ${process.versions.chrome}`);

const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
    signale.fatal("Error: Another instance of eDEX is already running. Cannot proceed.");
    app.exit(1);
}

signale.time("Startup");

const electron = require("electron");
const ipc = electron.ipcMain;
const path = require("node:path");
const url = require("node:url");
const fs = require("node:fs");
const which = require("which");
const Terminal = require("./classes/terminal.class.js").Terminal;

ipc.on("log", (e, type, content) => {
    if (typeof signale[type] === "function") signale[type](content);
});

let win, tty, extraTtys;
const settingsFile = path.join(electron.app.getPath("userData"), "settings.json");
const shortcutsFile = path.join(electron.app.getPath("userData"), "shortcuts.json");
const lastWindowStateFile = path.join(electron.app.getPath("userData"), "lastWindowState.json");
const themesDir = path.join(electron.app.getPath("userData"), "themes");
const innerThemesDir = path.join(__dirname, "assets/themes");
const kblayoutsDir = path.join(electron.app.getPath("userData"), "keyboards");
const innerKblayoutsDir = path.join(__dirname, "assets/kb_layouts");
const fontsDir = path.join(electron.app.getPath("userData"), "fonts");
const innerFontsDir = path.join(__dirname, "assets/fonts");

// Unset proxy env variables to avoid connection problems on the internal websockets
// See #222
if (process.env.http_proxy) delete process.env.http_proxy;
if (process.env.https_proxy) delete process.env.https_proxy;

// Bypass GPU acceleration blocklist, trading a bit of stability for a great
// deal of performance, mostly on Linux. NOT applied on Windows: Chromium's
// blocklist exists to catch genuinely broken driver/GPU combos, and forcing
// it off there is what causes some AMD driver versions to hard-fail
// DirectComposition (ui\gl\direct_composition_support.cc:
// "AMD VideoProcessorGetOutputExtension failed") - the window is created but
// never actually gets anything composited onto it, i.e. a black window.
// Windows' default (blocklist-respecting) GPU path doesn't hit this.
if (process.platform !== "win32") {
    app.commandLine.appendSwitch("ignore-gpu-blocklist");
    app.commandLine.appendSwitch("enable-gpu-rasterization");
    app.commandLine.appendSwitch("enable-video-decode");
}

// Fix userData folder not setup on Windows
try {
    fs.mkdirSync(electron.app.getPath("userData"));
    signale.info(`Created config dir at ${electron.app.getPath("userData")}`);
} catch {
    signale.info(`Base config dir is ${electron.app.getPath("userData")}`);
}
// Atomically create a file only if it doesn't already exist - avoids the
// check-then-write race a plain `if (!existsSync) writeFileSync` has
// between the check and the write (see CodeQL: "file may have changed
// since it was checked").
function writeIfMissing(path, data) {
    try {
        fs.writeFileSync(path, data, { flag: "wx" });
        return true;
    } catch (e) {
        if (e.code === "EEXIST") return false;
        throw e;
    }
}

// Create default settings file
if (
    writeIfMissing(
        settingsFile,
        JSON.stringify(
            {
                shell: process.platform === "win32" ? "powershell.exe" : "bash",
                shellArgs: "",
                cwd: electron.app.getPath("userData"),
                keyboard: "en-US",
                theme: "tron",
                termFontSize: 15,
                audio: true,
                audioVolume: 1.0,
                disableFeedbackAudio: false,
                clockHours: 24,
                pingAddr: "1.1.1.1",
                port: 3000,
                nointro: false,
                nocursor: false,
                forceFullscreen: true,
                allowWindowed: false,
                excludeThreadsFromToplist: true,
                hideDotfiles: false,
                fsListView: false,
                experimentalGlobeFeatures: false,
                experimentalFeatures: false,
            },
            "",
            4
        )
    )
) {
    signale.info(`Default settings written to ${settingsFile}`);
}
// Create default shortcuts file
if (
    writeIfMissing(
        shortcutsFile,
        JSON.stringify(
            [
                { type: "app", trigger: "Ctrl+Shift+C", action: "COPY", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+V", action: "PASTE", enabled: true },
                { type: "app", trigger: "Ctrl+Tab", action: "NEXT_TAB", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+Tab", action: "PREVIOUS_TAB", enabled: true },
                { type: "app", trigger: "Ctrl+X", action: "TAB_X", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+S", action: "SETTINGS", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+K", action: "SHORTCUTS", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+F", action: "FUZZY_SEARCH", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+L", action: "FS_LIST_VIEW", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+H", action: "FS_DOTFILES", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+P", action: "KB_PASSMODE", enabled: true },
                { type: "app", trigger: "Ctrl+Shift+I", action: "DEV_DEBUG", enabled: false },
                { type: "app", trigger: "Ctrl+Shift+F5", action: "DEV_RELOAD", enabled: true },
                { type: "shell", trigger: "Ctrl+Shift+Alt+Space", action: "neofetch", linebreak: true, enabled: false },
            ],
            "",
            4
        )
    )
) {
    signale.info(`Default keymap written to ${shortcutsFile}`);
}
//Create default window state file
if (
    writeIfMissing(
        lastWindowStateFile,
        JSON.stringify(
            {
                useFullscreen: true,
            },
            "",
            4
        )
    )
) {
    signale.info(`Default last window state written to ${lastWindowStateFile}`);
}

// Copy default themes & keyboard layouts & fonts
signale.pending("Mirroring internal assets...");
try {
    fs.mkdirSync(themesDir);
} catch {
    // Folder already exists
}
fs.readdirSync(innerThemesDir).forEach((e) => {
    fs.writeFileSync(path.join(themesDir, e), fs.readFileSync(path.join(innerThemesDir, e), { encoding: "utf-8" }));
});
try {
    fs.mkdirSync(kblayoutsDir);
} catch {
    // Folder already exists
}
fs.readdirSync(innerKblayoutsDir).forEach((e) => {
    fs.writeFileSync(
        path.join(kblayoutsDir, e),
        fs.readFileSync(path.join(innerKblayoutsDir, e), { encoding: "utf-8" })
    );
});
try {
    fs.mkdirSync(fontsDir);
} catch {
    // Folder already exists
}
fs.readdirSync(innerFontsDir).forEach((e) => {
    fs.writeFileSync(path.join(fontsDir, e), fs.readFileSync(path.join(innerFontsDir, e)));
});

// Version history logging
const versionHistoryPath = path.join(electron.app.getPath("userData"), "versions_log.json");
// Read directly instead of existsSync()-then-require(): avoids a TOCTOU gap
// where the file could be deleted between the check and the read, and skips
// require()'s module cache (this path is only ever read once per boot, but
// caching would be wrong if that ever changed).
let versionHistory;
try {
    versionHistory = JSON.parse(fs.readFileSync(versionHistoryPath, "utf-8"));
} catch {
    versionHistory = {};
}
let version = app.getVersion();
if (versionHistory[version] === undefined) {
    versionHistory[version] = {
        firstSeen: Date.now(),
        lastSeen: Date.now(),
    };
} else {
    versionHistory[version].lastSeen = Date.now();
}
fs.writeFileSync(versionHistoryPath, JSON.stringify(versionHistory, 0, 2), { encoding: "utf-8" });

function createWindow(settings) {
    signale.info("Creating window...");

    let display;
    if (!Number.isNaN(settings.monitor)) {
        display = electron.screen.getAllDisplays()[settings.monitor] || electron.screen.getPrimaryDisplay();
    } else {
        display = electron.screen.getPrimaryDisplay();
    }
    let { x, y, width, height } = display.bounds;
    width++;
    height++;
    win = new BrowserWindow({
        title: "eDEX-UI",
        x,
        y,
        width,
        height,
        show: false,
        resizable: true,
        movable: settings.allowWindowed || false,
        fullscreen: settings.forceFullscreen || false,
        autoHideMenuBar: true,
        frame: settings.allowWindowed || false,
        backgroundColor: "#000000",
        webPreferences: {
            devTools: true,
            contextIsolation: true,
            backgroundThrottling: false,
            webSecurity: true,
            nodeIntegration: false,
            nodeIntegrationInSubFrames: false,
            allowRunningInsecureContent: false,
            experimentalFeatures: settings.experimentalFeatures || false,
            preload: path.join(__dirname, "preload.js"),
        },
    });

    require("./ipc-handlers.js").register({
        win,
        paths: {
            userData: electron.app.getPath("userData"),
            settingsFile,
            shortcutsFile,
            lastWindowStateFile,
            themesDir,
            kblayoutsDir,
            fontsDir,
        },
    });

    win.loadURL(
        url.format({
            pathname: path.join(__dirname, "ui.html"),
            protocol: "file:",
            slashes: true,
        })
    );

    signale.complete("Frontend window created!");
    win.show();
    if (!settings.allowWindowed) {
        win.setResizable(false);
    } else if (!JSON.parse(fs.readFileSync(lastWindowStateFile, "utf-8")).useFullscreen) {
        win.setFullScreen(false);
    }

    signale.watch("Waiting for frontend connection...");
}

app.on("ready", async () => {
    signale.pending("Loading settings file...");
    let settings = JSON.parse(fs.readFileSync(settingsFile, "utf-8"));
    signale.pending("Resolving shell path...");
    settings.shell = await which(settings.shell).catch((e) => {
        throw e;
    });
    signale.info(`Shell found at ${settings.shell}`);
    signale.success("Settings loaded!");

    if (!fs.existsSync(settings.cwd)) throw new Error("Configured cwd path does not exist.");

    // See #366
    // "shell-env" is ESM-only, so it can't be require()'d from this CJS main process.
    let cleanEnv = await (await import("shell-env")).shellEnv(settings.shell).catch((e) => {
        throw e;
    });

    Object.assign(
        cleanEnv,
        {
            TERM: "xterm-256color",
            COLORTERM: "truecolor",
            TERM_PROGRAM: "eDEX-UI",
            TERM_PROGRAM_VERSION: app.getVersion(),
        },
        settings.env
    );

    signale.pending(`Creating new terminal process on port ${settings.port || "3000"}`);
    tty = new Terminal({
        role: "server",
        shell: settings.shell,
        params: settings.shellArgs || "",
        cwd: settings.cwd,
        env: cleanEnv,
        port: settings.port || 3000,
    });
    signale.success("Terminal back-end initialized!");
    tty.onclosed = (code, signal) => {
        tty.ondisconnected = () => {};
        signale.complete("Terminal exited", code, signal);
        app.quit();
    };
    tty.onopened = () => {
        signale.success("Connected to frontend!");
        signale.timeEnd("Startup");
    };
    tty.onresized = (cols, rows) => {
        signale.info("Resized TTY to ", cols, rows);
    };
    tty.ondisconnected = () => {
        signale.error("Lost connection to frontend");
        signale.watch("Waiting for frontend connection...");
    };

    // Support for multithreaded systeminformation calls
    signale.pending("Starting multithreaded calls controller...");
    require("./_multithread.js");

    createWindow(settings);

    // Support for more terminals, used for creating tabs (currently limited to 4 extra terms)
    extraTtys = {};
    let basePort = settings.port || 3000;
    basePort = Number(basePort) + 2;

    for (let i = 0; i < 4; i++) {
        extraTtys[basePort + i] = null;
    }

    ipc.on("ttyspawn", (e) => {
        let port = null;
        Object.keys(extraTtys).forEach((key) => {
            if (extraTtys[key] === null && port === null) {
                extraTtys[key] = {};
                port = key;
            }
        });

        if (port === null) {
            signale.error("TTY spawn denied (Reason: exceeded max TTYs number)");
            e.sender.send("ttyspawn-reply", "ERROR: max number of ttys reached");
        } else {
            signale.pending(`Creating new TTY process on port ${port}`);
            let term = new Terminal({
                role: "server",
                shell: settings.shell,
                params: settings.shellArgs || "",
                cwd: tty.tty._cwd || settings.cwd,
                env: cleanEnv,
                port: port,
            });
            signale.success(`New terminal back-end initialized at ${port}`);
            term.onclosed = (code, signal) => {
                term.ondisconnected = () => {};
                term.wss.close();
                signale.complete(`TTY exited at ${port}`, code, signal);
                extraTtys[term.port] = null;
                term = null;
            };
            term.onopened = (pid) => {
                signale.success(`TTY ${port} connected to frontend (process PID ${pid})`);
            };
            term.onresized = () => {};
            term.ondisconnected = () => {
                term.onclosed = () => {};
                term.close();
                term.wss.close();
                extraTtys[term.port] = null;
                term = null;
            };

            extraTtys[port] = term;
            e.sender.send("ttyspawn-reply", "SUCCESS: " + port);
        }
    });

    // Backend support for theme and keyboard hotswitch
    let themeOverride = null;
    let kbOverride = null;
    ipc.on("getThemeOverride", (e) => {
        e.sender.send("getThemeOverride", themeOverride);
    });
    ipc.on("getKbOverride", (e) => {
        e.sender.send("getKbOverride", kbOverride);
    });
    ipc.on("setThemeOverride", (e, arg) => {
        themeOverride = arg;
    });
    ipc.on("setKbOverride", (e, arg) => {
        kbOverride = arg;
    });
});

app.on("web-contents-created", (e, contents) => {
    // Prevent creating more than one window. Same http(s)-only validation as
    // the shell:openExternal IPC handler in ipc-handlers.js: an unvalidated
    // scheme reaching shell.openExternal can be abused via crafted file://
    // or custom-protocol-handler URLs on some platforms.
    contents.on("new-window", (e, url) => {
        e.preventDefault();
        let parsed;
        try {
            parsed = new URL(url);
        } catch {
            return;
        }
        if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return;
        shell.openExternal(parsed.href);
    });

    // Prevent loading something else than the UI
    contents.on("will-navigate", (e, url) => {
        if (url !== contents.getURL()) e.preventDefault();
    });
});

app.on("window-all-closed", () => {
    signale.info("All windows closed");
    app.quit();
});

app.on("before-quit", () => {
    tty.close();
    Object.keys(extraTtys).forEach((key) => {
        if (extraTtys[key] !== null) {
            extraTtys[key].close();
        }
    });
    signale.complete("Shutting down...");
});
