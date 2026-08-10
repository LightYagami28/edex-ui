const js = require("@eslint/js");
const globals = require("globals");

module.exports = [
    {
        ignores: [
            "**/node_modules/**",
            "dist/**",
            "prebuild-src/**",
            "file-icons/**",
            // Vendored third-party bundle and a generated data/lookup table - not maintained here.
            "src/assets/vendor/**",
            "src/assets/misc/file-icons-match.js",
        ],
    },
    js.configs.recommended,
    {
        languageOptions: {
            ecmaVersion: "latest",
            sourceType: "commonjs",
            globals: {
                ...globals.browser,
                ...globals.commonjs,
                ...globals.node,
                // src/ui.html loads every class as a separate classic <script>, all sharing
                // the renderer's global scope instead of using module imports/exports.
                AudioManager: "readonly",
                Clock: "readonly",
                Conninfo: "readonly",
                Cpuinfo: "readonly",
                DocReader: "readonly",
                FilesystemDisplay: "readonly",
                FuzzyFinder: "readonly",
                HardwareInspector: "readonly",
                LocationGlobe: "readonly",
                MediaPlayer: "readonly",
                Modal: "readonly",
                Netstat: "readonly",
                RAMwatcher: "readonly",
                Sysinfo: "readonly",
                Terminal: "readonly",
                Toplist: "readonly",
                UpdateChecker: "readonly",
                // Top-level consts/functions defined in _renderer.js, used by files loaded
                // before it (relying on classic <script> top-level let/const sharing one
                // global lexical scope, evaluated only after _renderer.js itself has run).
                path: "readonly",
                remote: "readonly",
                settings: "readonly",
                settingsDir: "readonly",
                themesDir: "readonly",
                keyboardsDir: "readonly",
                electronWin: "readonly",
                _delay: "readonly",
                _escapeHtml: "readonly",
                _loadTheme: "readonly",
                // Loaded via the pdf.mjs module bridge in ui.html
                pdfjsLib: "readonly",
            },
        },
        rules: {
            // Formatting (indent, quotes, semi, ...) is owned by Prettier, not ESLint -
            // ESLint's own stylistic rules were deprecated in favor of dedicated formatters.
            // Leading underscore marks an intentionally-unused binding (e.g. a
            // `new Modal(...)` kept only for its constructor side effects).
            "no-unused-vars": ["warn", { varsIgnorePattern: "^_", argsIgnorePattern: "^_" }],
            "no-console": "off",
        },
    },
];
