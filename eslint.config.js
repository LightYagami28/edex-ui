"use strict";

const js = require("@eslint/js");
const globals = require("globals");

// Renderer scripts are loaded as classic <script> tags from src/ui.html and
// share the same global scope: classes, helpers and constants declared in one
// file (mostly src/_renderer.js) are used by the others.
const rendererGlobals = Object.fromEntries(
    [
        // classes from src/classes/*.class.js
        "AudioManager",
        "Clock",
        "Conninfo",
        "Cpuinfo",
        "DocReader",
        "FilesystemDisplay",
        "FuzzyFinder",
        "HardwareInspector",
        "Keyboard",
        "LocationGlobe",
        "MediaPlayer",
        "Modal",
        "Netstat",
        "RAMwatcher",
        "Sysinfo",
        "Terminal",
        "Toplist",
        "UpdateChecker",
        // window helpers defined in src/_renderer.js
        "_delay",
        "_escapeHtml",
        "_loadTheme",
        "settings",
        // top-level constants declared in src/_renderer.js
        "electron",
        "fontsDir",
        "ipc",
        "keyboardsDir",
        "lastWindowStateFile",
        "remote",
        "settingsDir",
        "settingsFile",
        "shortcutsFile",
        "themesDir",
        // libraries exposed on window
        "pdfjsLib",
        "Howl",
        "ENCOM",
    ].map((name) => [name, "readonly"])
);

module.exports = [
    {
        ignores: [
            "node_modules/",
            "src/node_modules/",
            "dist/",
            "prebuild-src/",
            "file-icons/",
            // third-party / generated code
            "src/assets/vendor/",
            "src/assets/misc/file-icons-match.js",
        ],
    },
    js.configs.recommended,
    {
        files: ["**/*.js"],
        languageOptions: {
            ecmaVersion: 2022,
            sourceType: "commonjs",
            globals: {
                ...globals.browser,
                ...globals.node,
            },
        },
        // Formatting is handled by Prettier (see .prettierrc.json).
        rules: {
            "no-unused-vars": ["warn", { args: "none", caughtErrors: "none" }],
            "no-console": "off",
        },
    },
    {
        files: ["src/_renderer.js", "src/classes/**/*.js"],
        languageOptions: {
            globals: rendererGlobals,
        },
    },
];