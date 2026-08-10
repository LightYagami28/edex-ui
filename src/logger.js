"use strict";

// Minimal drop-in replacement for the console-logging subset of `signale`
// (github.com/klauscfhq/signale) that this app actually uses. signale is
// unmaintained and its only dependency, pkg-conf@5.0.0, was renamed upstream
// to package-config with no newer pkg-conf release ever published, so the
// deprecation warning could not be silenced by bumping a version - the fix is
// to drop the dependency instead.

const COLOR_CODES = {
    red: "31",
    green: "32",
    yellow: "33",
    blue: "34",
    magenta: "35",
    cyan: "36",
};

const TYPES = {
    error: { badge: "✖", color: "red", label: "error" },
    fatal: { badge: "✖", color: "red", label: "fatal" },
    info: { badge: "ℹ", color: "blue", label: "info" },
    success: { badge: "✔", color: "green", label: "success" },
    warn: { badge: "⚠", color: "yellow", label: "warning" },
    complete: { badge: "☑", color: "cyan", label: "complete" },
    pending: { badge: "☐", color: "magenta", label: "pending" },
    note: { badge: "•", color: "blue", label: "note" },
    start: { badge: "▶", color: "green", label: "start" },
    debug: { badge: "●", color: "red", label: "debug" },
    watch: { badge: "…", color: "yellow", label: "watching" },
};

function paint(color, text) {
    const code = COLOR_CODES[color];
    return code ? `[${code}m${text}[39m` : text;
}

function consoleMethodFor(type) {
    if (type === "error" || type === "fatal") return console.error;
    if (type === "warn") return console.warn;
    return console.log;
}

function log(type, args) {
    const meta = TYPES[type] ?? TYPES.info;
    const prefix = paint(meta.color, `${meta.badge} ${meta.label}`);
    consoleMethodFor(type)(prefix, ...args);
}

const timers = new Map();

const logger = {};
for (const type of Object.keys(TYPES)) {
    logger[type] = (...args) => log(type, args);
}
logger.time = (label) => {
    timers.set(label, Date.now());
};
logger.timeEnd = (label) => {
    const startedAt = timers.get(label);
    timers.delete(label);
    const elapsedMs = startedAt === undefined ? 0 : Date.now() - startedAt;
    log("info", [`${label}: ${elapsedMs}ms`]);
};

module.exports = logger;
