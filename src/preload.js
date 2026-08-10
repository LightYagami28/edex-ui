const { contextBridge, ipcRenderer, webFrame } = require("electron");
const path = require("path");
const os = require("os");

// Channels the renderer is allowed to send/listen on through ipc.send/on below.
// Keeps this bridge from becoming a generic "call anything over IPC" hole.
const ALLOWED_SEND_CHANNELS = new Set([
    "log",
    "ttyspawn",
    "getThemeOverride",
    "getKbOverride",
    "setThemeOverride",
    "setKbOverride",
]);
const ALLOWED_RECEIVE_PREFIXES = ["terminal_channel-", "systeminformation-reply-"];
const ALLOWED_RECEIVE_CHANNELS = new Set(["ttyspawn-reply", "getThemeOverride", "getKbOverride"]);

function isAllowedReceiveChannel(channel) {
    return ALLOWED_RECEIVE_CHANNELS.has(channel) || ALLOWED_RECEIVE_PREFIXES.some(p => channel.startsWith(p));
}

contextBridge.exposeInMainWorld("eDEX", {
    // ---- App / window info & controls (replaces @electron/remote) ----
    app: {
        getVersion: () => ipcRenderer.invoke("app:getVersion"),
        getPath: name => ipcRenderer.invoke("app:getPath", name),
        getArgv: () => ipcRenderer.invoke("app:getArgv"),
        focus: () => ipcRenderer.send("app:focus"),
        relaunch: () => ipcRenderer.send("app:relaunch"),
        quit: () => ipcRenderer.send("app:quit"),
    },
    win: {
        minimize: () => ipcRenderer.send("win:minimize"),
        isFullScreen: () => ipcRenderer.invoke("win:isFullScreen"),
        setFullScreen: state => ipcRenderer.send("win:setFullScreen", state),
        isMaximized: () => ipcRenderer.invoke("win:isMaximized"),
        unmaximize: () => ipcRenderer.send("win:unmaximize"),
        getSize: () => ipcRenderer.invoke("win:getSize"),
        setSize: (w, h) => ipcRenderer.send("win:setSize", w, h),
        toggleDevTools: () => ipcRenderer.send("win:toggleDevTools"),
        onResize: cb => ipcRenderer.on("win:resize", cb),
        onLeaveFullScreen: cb => ipcRenderer.on("win:leave-full-screen", cb),
    },
    screen: {
        getAllDisplays: () => ipcRenderer.invoke("screen:getAllDisplays"),
    },
    globalShortcut: {
        register: (accelerator, id) => ipcRenderer.invoke("globalShortcut:register", accelerator, id),
        unregisterAll: () => ipcRenderer.send("globalShortcut:unregisterAll"),
        onTriggered: cb => ipcRenderer.on("globalShortcut:triggered", cb),
    },
    webFrame: {
        setVisualZoomLevelLimits: (min, max) => webFrame.setVisualZoomLevelLimits(min, max),
    },
    clipboard: {
        readText: () => ipcRenderer.invoke("clipboard:readText"),
    },
    shell: {
        openExternal: url => ipcRenderer.send("shell:openExternal", url),
        openPath: filePath => ipcRenderer.send("shell:openPath", filePath),
    },

    // ---- Platform info (read-only, safe to expose directly) ----
    platform: process.platform,
    osType: os.type(),

    // ---- Config file IO (settings/shortcuts/themes/keyboard layouts) ----
    config: {
        getPaths: () => ipcRenderer.invoke("config:getPaths"),
        getSettings: () => ipcRenderer.invoke("config:getSettings"),
        getShortcuts: () => ipcRenderer.invoke("config:getShortcuts"),
        getLastWindowState: () => ipcRenderer.invoke("config:getLastWindowState"),
        getTheme: name => ipcRenderer.invoke("config:getTheme", name),
        getKeyboardLayout: name => ipcRenderer.invoke("config:getKeyboardLayout", name),
        writeSettings: settings => ipcRenderer.invoke("config:writeSettings", settings),
    },

    // ---- Filesystem browsing (the app is a file manager tied to the
    // terminal's cwd, so this intentionally accepts arbitrary paths - the
    // user already has a real shell, this isn't a sandbox boundary, it's
    // about removing incidental raw Node access from the renderer) ----
    fs: {
        readdir: dirPath => ipcRenderer.invoke("fs:readdir", dirPath),
        lstat: filePath => ipcRenderer.invoke("fs:lstat", filePath),
        readFile: (filePath, encoding) => ipcRenderer.invoke("fs:readFile", filePath, encoding),
        writeFile: (filePath, data) => ipcRenderer.invoke("fs:writeFile", filePath, data),
        exists: filePath => ipcRenderer.invoke("fs:exists", filePath),
        watch: dirPath => ipcRenderer.invoke("fs:watch", dirPath),
        unwatch: dirPath => ipcRenderer.invoke("fs:unwatch", dirPath),
        onWatchEvent: cb => ipcRenderer.on("fs:watchEvent", cb),
    },

    // ---- Path helpers (pure string manipulation, safe to run locally) ----
    path: {
        join: (...args) => path.join(...args),
        resolve: (...args) => path.resolve(...args),
        extname: p => path.extname(p),
    },

    // ---- GeoIP (geolite2-redist + maxmind live in the main process) ----
    geoip: {
        lookup: ip => ipcRenderer.invoke("geoip:lookup", ip),
    },

    // ---- Network helpers that need raw Node sockets/HTTPS ----
    net: {
        ping: (target, port, localAddress) => ipcRenderer.invoke("net:ping", target, port, localAddress),
        getExternalIp: localAddress => ipcRenderer.invoke("net:getExternalIp", localAddress),
        checkForUpdate: () => ipcRenderer.invoke("net:checkForUpdate"),
    },

    // ---- Misc identity lookups ----
    username: () => ipcRenderer.invoke("os:username"),

    // ---- mime-types is CJS-only, can't be require()'d or import()'d here ----
    mime: {
        lookup: extOrPath => ipcRenderer.invoke("mime:lookup", extOrPath),
        charset: mimeType => ipcRenderer.invoke("mime:charset", mimeType),
    },

    // ---- Raw IPC pass-through, restricted to the channels this app
    // actually uses (terminal websocket handshake, tty spawn, theme/kb
    // hot-swap, logging, systeminformation proxy replies) ----
    ipc: {
        send: (channel, ...args) => {
            if (!ALLOWED_SEND_CHANNELS.has(channel) && !channel.startsWith("terminal_channel-") && !channel.startsWith("systeminformation-call")) {
                throw new Error(`eDEX bridge: send on disallowed channel "${channel}"`);
            }
            ipcRenderer.send(channel, ...args);
        },
        on: (channel, listener) => {
            if (!isAllowedReceiveChannel(channel)) {
                throw new Error(`eDEX bridge: listen on disallowed channel "${channel}"`);
            }
            ipcRenderer.on(channel, listener);
        },
        once: (channel, listener) => {
            if (!isAllowedReceiveChannel(channel)) {
                throw new Error(`eDEX bridge: listen on disallowed channel "${channel}"`);
            }
            ipcRenderer.once(channel, listener);
        },
    },
});
