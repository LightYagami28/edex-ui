class Keyboard {
    constructor(opts) {
        if (!opts.layout || !opts.container) throw new Error("Missing options");
        this._opts = opts;
    }

    // Layout files are now loaded through the main process (see
    // ipc-handlers.js), which is inherently async - callers should call
    // `.start()` right after construction, then `await keyboard.ready`
    // before relying on the on-screen keyboard being fully built.
    start() {
        this.ready = this._init(this._opts);
        return this.ready;
    }

    async _init(opts) {
        const layout = await window.eDEX.config.getKeyboardLayout(opts.layout);
        this.ctrlseq = ["", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", ""];
        this.container = document.getElementById(opts.container);

        this.linkedToTerm = true;
        this.detach = () => {
            this.linkedToTerm = false;
        };
        this.attach = () => {
            this.linkedToTerm = true;
        };

        // Set default keyboard properties
        this.container.dataset.isShiftOn = false;
        this.container.dataset.isCapsLckOn = false;
        this.container.dataset.isAltOn = false;
        this.container.dataset.isCtrlOn = false;
        this.container.dataset.isFnOn = false;

        this.container.dataset.passwordMode = false;

        // Build arrays for enabling keyboard shortcuts
        this._shortcuts = {
            CtrlAltShift: [],
            CtrlAlt: [],
            CtrlShift: [],
            AltShift: [],
            Ctrl: [],
            Alt: [],
            Shift: [],
        };
        window.shortcuts.forEach((scut) => {
            let cut = { ...scut };
            let mods = cut.trigger.split("+");
            cut.trigger = mods.pop();

            let order = ["Ctrl", "Alt", "Shift"];
            mods.sort((a, b) => {
                return order.indexOf(a) - order.indexOf(b);
            });

            let cat = mods.join("");

            if (cut.type === "app" && cut.action === "TAB_X" && cut.trigger === "X") {
                for (let i = 1; i <= 5; i++) {
                    let ncut = { ...cut };
                    ncut.trigger = `${i}`;
                    ncut.action = `TAB_${i}`;
                    this._shortcuts[cat].push(ncut);
                }
            } else {
                this._shortcuts[cat].push(cut);
            }
        });

        // Parse keymap and create DOM
        Object.keys(layout).forEach((row) => {
            this.container.innerHTML += '<div class="keyboard_row" id="' + row + '"></div>';
            layout[row].forEach((keyObj) => {
                let key = document.createElement("div");
                key.setAttribute("class", "keyboard_key");

                if (keyObj.cmd === " ") {
                    key.setAttribute("id", "keyboard_spacebar");
                } else if (keyObj.cmd === "\r") {
                    key.setAttribute("class", "keyboard_key keyboard_enter");
                    key.innerHTML = `<h1>${keyObj.name}</h1>`;
                } else {
                    key.innerHTML = `
                        <h5>${keyObj.altshift_name || ""}</h5>
                        <h4>${keyObj.fn_name || ""}</h4>
                        <h3>${keyObj.alt_name || ""}</h3>
                        <h2>${keyObj.shift_name || ""}</h2>
                        <h1>${keyObj.name || ""}</h1>`;
                }

                // Icon support, overrides previously defined innerHTML
                // Arrow and other icons
                let icon;
                if (keyObj.name.startsWith("ESCAPED|-- ICON: ")) {
                    keyObj.name = keyObj.name.substr(17);
                    switch (keyObj.name) {
                        case "ARROW_UP":
                            icon =
                                '<svg viewBox="0 0 24.00 24.00"><path fill-opacity="1" d="m12.00004 7.99999 4.99996 5h-2.99996v4.00001h-4v-4.00001h-3z"/><path stroke-linejoin="round" fill-opacity="0.65" d="m4 3h16c1.1046 0 1-0.10457 1 1v16c0 1.1046 0.1046 1-1 1h-16c-1.10457 0-1 0.1046-1-1v-16c0-1.10457-0.10457-1 1-1zm0 1v16h16v-16z"/></svg>';
                            break;
                        case "ARROW_LEFT":
                            icon =
                                '<svg viewBox="0 0 24.00 24.00"><path fill-opacity="1" d="m7.500015 12.499975 5-4.99996v2.99996h4.00001v4h-4.00001v3z"/><path stroke-linejoin="round" fill-opacity="0.65" d="m4 3h16c1.1046 0 1-0.10457 1 1v16c0 1.1046 0.1046 1-1 1h-16c-1.10457 0-1 0.1046-1-1v-16c0-1.10457-0.10457-1 1-1zm0 1v16h16v-16z"/></svg>';
                            break;
                        case "ARROW_DOWN":
                            icon =
                                '<svg viewBox="0 0 24.00 24.00"><path fill-opacity="1" d="m12 17-4.99996-5h2.99996v-4.00001h4v4.00001h3z"/><path stroke-linejoin="round" fill-opacity="0.65" d="m4 3h16c1.1046 0 1-0.10457 1 1v16c0 1.1046 0.1046 1-1 1h-16c-1.10457 0-1 0.1046-1-1v-16c0-1.10457-0.10457-1 1-1zm0 1v16h16v-16z"/></svg>';
                            break;
                        case "ARROW_RIGHT":
                            icon =
                                '<svg viewBox="0 0 24.00 24.00"><path fill-opacity="1" d="m16.500025 12.500015-5 4.99996v-2.99996h-4.00001v-4h4.00001v-3z"/><path stroke-linejoin="round" fill-opacity="0.65" d="m4 3h16c1.1046 0 1-0.10457 1 1v16c0 1.1046 0.1046 1-1 1h-16c-1.10457 0-1 0.1046-1-1v-16c0-1.10457-0.10457-1 1-1zm0 1v16h16v-16z"/></svg>';
                            break;
                        default:
                            icon =
                                '<svg viewBox="0 0 24.00 24.00"><path fill="#ff0000" fill-opacity="1" d="M 8.27125,2.9978L 2.9975,8.27125L 2.9975,15.7275L 8.27125,21.0012L 15.7275,21.0012C 17.485,19.2437 21.0013,15.7275 21.0013,15.7275L 21.0013,8.27125L 15.7275,2.9978M 9.10125,5L 14.9025,5L 18.9988,9.10125L 18.9988,14.9025L 14.9025,18.9988L 9.10125,18.9988L 5,14.9025L 5,9.10125M 9.11625,7.705L 7.705,9.11625L 10.5912,12.0025L 7.705,14.8825L 9.11625,16.2937L 12.0025,13.4088L 14.8825,16.2937L 16.2938,14.8825L 13.4087,12.0025L 16.2938,9.11625L 14.8825,7.705L 12.0025,10.5913"/></svg>';
                    }

                    key.innerHTML = icon;
                }

                Object.keys(keyObj).forEach((property) => {
                    for (let i = 1; i < this.ctrlseq.length; i++) {
                        keyObj[property] = keyObj[property].replace("~~~CTRLSEQ" + i + "~~~", this.ctrlseq[i]);
                    }
                    if (property.endsWith("cmd")) {
                        key.dataset[property] = keyObj[property];
                    }
                });

                document.getElementById(row).appendChild(key);
            });
        });

        this.container.childNodes.forEach((row) => {
            row.childNodes.forEach((key) => {
                if (key.attributes["class"].value.endsWith("keyboard_enter")) {
                    // The enter key is divided in two dom elements, so we bind their animations here
                    this._bindEnterKeyEvents(key, document.querySelectorAll(".keyboard_enter"));
                } else {
                    this._bindRegularKeyEvents(key);
                }

                // See #229
                key.onmouseleave = () => {
                    clearTimeout(key.holdTimeout);
                    clearInterval(key.holdInterval);
                };
            });
        });

        // Tactile multi-touch support (#100)
        this.container.addEventListener("touchstart", (e) => {
            e.preventDefault();
            for (const touch of e.changedTouches) {
                let key = touch.target.parentElement;
                if (key.tagName === "svg") key = key.parentElement;
                if (key.getAttribute("class").startsWith("keyboard_key")) {
                    key.setAttribute("class", key.getAttribute("class") + " active");
                    key.onmousedown({
                        preventDefault: () => {
                            return true;
                        },
                    });
                } else {
                    key = touch.target;
                    if (key.getAttribute("class").startsWith("keyboard_key")) {
                        key.setAttribute("class", key.getAttribute("class") + " active");
                        key.onmousedown({
                            preventDefault: () => {
                                return true;
                            },
                        });
                    }
                }
            }
        });
        let dropKeyTouchHandler = (e) => {
            e.preventDefault();
            for (const touch of e.changedTouches) {
                let key = touch.target.parentElement;
                if (key.tagName === "svg") key = key.parentElement;
                if (key.getAttribute("class").startsWith("keyboard_key")) {
                    key.setAttribute("class", key.getAttribute("class").replace("active", ""));
                    key.onmouseup({
                        preventDefault: () => {
                            return true;
                        },
                    });
                } else {
                    key = touch.target;
                    if (key.getAttribute("class").startsWith("keyboard_key")) {
                        key.setAttribute("class", key.getAttribute("class").replace("active", ""));
                        key.onmouseup({
                            preventDefault: () => {
                                return true;
                            },
                        });
                    }
                }
            }
        };
        this.container.addEventListener("touchend", dropKeyTouchHandler);
        this.container.addEventListener("touchcancel", dropKeyTouchHandler);

        // Bind actual keyboard actions to on-screen animations (for use without a touchscreen)
        // Maps e.code to the selector for its keyboard_key element, for keys
        // that have no matching data-cmd/data-shift_cmd (shift, control, arrows,
        // etc). Used by findKey() below.
        const SPECIAL_KEY_SELECTOR_BY_CODE = {
            ShiftLeft: 'div.keyboard_key[data-cmd="ESCAPED|-- SHIFT: LEFT"]',
            ShiftRight: 'div.keyboard_key[data-cmd="ESCAPED|-- SHIFT: RIGHT"]',
            ControlLeft: 'div.keyboard_key[data-cmd="ESCAPED|-- CTRL: LEFT"]',
            ControlRight: 'div.keyboard_key[data-cmd="ESCAPED|-- CTRL: RIGHT"]',
            AltLeft: 'div.keyboard_key[data-cmd="ESCAPED|-- FN: ON"]',
            AltRight: 'div.keyboard_key[data-cmd="ESCAPED|-- ALT: RIGHT"]',
            CapsLock: 'div.keyboard_key[data-cmd="ESCAPED|-- CAPSLCK: ON"]',
            Escape: 'div.keyboard_key[data-cmd=""]',
            Backspace: 'div.keyboard_key[data-cmd=""]',
            ArrowUp: 'div.keyboard_key[data-cmd="OA"]',
            ArrowLeft: 'div.keyboard_key[data-cmd="OD"]',
            ArrowDown: 'div.keyboard_key[data-cmd="OB"]',
            ArrowRight: 'div.keyboard_key[data-cmd="OC"]',
        };

        let findKey = (e) => {
            // Fix incorrect querySelector error
            let physkey = e.key === '"' ? String.raw`\"` : e.key;

            // Find basic keys (typically letters, upper and lower-case)
            let key = document.querySelector('div.keyboard_key[data-cmd="' + physkey + '"]');
            if (key === null) key = document.querySelector('div.keyboard_key[data-shift_cmd="' + physkey + '"]');

            // Find special keys (shift, control, arrows, etc.)
            if (key === null && e.code === "Enter") {
                key = document.querySelectorAll("div.keyboard_key.keyboard_enter");
            } else if (key === null && SPECIAL_KEY_SELECTOR_BY_CODE[e.code]) {
                key = document.querySelector(SPECIAL_KEY_SELECTOR_BY_CODE[e.code]);
            }

            // Find "rare" keys (ctrl and alt symbols)
            if (key === null) key = document.querySelector('div.keyboard_key[data-ctrl_cmd="' + e.key + '"]');
            if (key === null) key = document.querySelector('div.keyboard_key[data-alt_cmd="' + e.key + '"]');

            return key;
        };

        this.keydownHandler = (e) => {
            // See #330
            if (e.getModifierState("AltGraph") && e.code === "AltRight") {
                document
                    .querySelector('div.keyboard_key[data-cmd="ESCAPED|-- CTRL: LEFT"]')
                    .setAttribute("class", "keyboard_key");
            }

            this._updateModifiersOnKeydown(e);

            let key = findKey(e);
            if (key === null) return;
            if (key.length) {
                key.forEach((enterElement) => {
                    enterElement.setAttribute("class", "keyboard_key active keyboard_enter");
                });
            } else {
                key.setAttribute("class", "keyboard_key active");
            }

            // See #516
            if (this._isRepeatableKeydown(e) && this.container.dataset.passwordMode === "false") {
                window.audioManager.stdin.play();
            }
        };

        document.onkeydown = this.keydownHandler;

        document.onkeyup = (e) => {
            // See #330
            if (e.key === "Control" && e.getModifierState("AltGraph")) return;

            // See #440
            if (e.code === "ControlLeft" || e.code === "ControlRight") this.container.dataset.isCtrlOn = false;
            if (e.code === "ShiftLeft" || e.code === "ShiftRight") this.container.dataset.isShiftOn = false;
            if (e.code === "AltLeft" || e.code === "AltRight") this.container.dataset.isAltOn = false;

            let key = findKey(e);
            if (key === null) return;
            if (key.length) {
                key.forEach((enterElement) => {
                    enterElement.setAttribute("class", "keyboard_key blink keyboard_enter");
                });
                setTimeout(() => {
                    key.forEach((enterElement) => {
                        enterElement.setAttribute("class", "keyboard_key keyboard_enter");
                    });
                }, 100);
            } else {
                key.setAttribute("class", "keyboard_key blink");
                setTimeout(() => {
                    key.setAttribute("class", "keyboard_key");
                }, 100);
            }

            if (this.container.dataset.passwordMode === "false" && e.key === "Enter")
                window.audioManager.granted.play();
        };

        window.addEventListener("blur", () => {
            document.querySelectorAll("div.keyboard_key.active").forEach((key) => {
                key.setAttribute("class", key.getAttribute("class").replace("active", ""));
                key.onmouseup({
                    preventDefault: () => {
                        return true;
                    },
                });
            });
        });
    }
    // Binds the press/hold/release animation and repeat-fire behavior for the
    // (two-DOM-element) enter key. Extracted out of _init() so its own
    // onmousedown/onmouseup/setTimeout/setInterval chain doesn't add to
    // _init()'s function-nesting depth.
    _bindEnterKeyEvents(key, enterElements) {
        key.onmousedown = (e) => {
            this.pressKey(key);
            key.holdTimeout = setTimeout(() => {
                key.holdInterval = setInterval(() => {
                    this.pressKey(key);
                }, 70);
            }, 400);

            enterElements.forEach((key) => {
                key.setAttribute("class", "keyboard_key active keyboard_enter");
            });

            // Keep focus on the terminal
            if (window.keyboard.linkedToTerm) window.term[window.currentTerm].term.focus();
            if (this.container.dataset.passwordMode === "false") window.audioManager.granted.play();
            e.preventDefault();
        };
        key.onmouseup = () => {
            clearTimeout(key.holdTimeout);
            clearInterval(key.holdInterval);

            enterElements.forEach((key) => {
                key.setAttribute("class", "keyboard_key blink keyboard_enter");
            });
            setTimeout(() => {
                enterElements.forEach((key) => {
                    key.setAttribute("class", "keyboard_key keyboard_enter");
                });
            }, 100);
        };
    }
    // Binds the press/hold/release animation and repeat-fire behavior for a
    // regular (non-enter) key. Extracted out of _init() for the same reason
    // as _bindEnterKeyEvents().
    _bindRegularKeyEvents(key) {
        key.onmousedown = (e) => {
            if (/^ESCAPED\|-- (CTRL|SHIFT|ALT).*/.test(key.dataset.cmd)) {
                let cmd = key.dataset.cmd.substr(11);
                if (cmd.startsWith("CTRL")) {
                    this.container.dataset.isCtrlOn = "true";
                }
                if (cmd.startsWith("SHIFT")) {
                    this.container.dataset.isShiftOn = "true";
                }
                if (cmd.startsWith("ALT")) {
                    this.container.dataset.isAltOn = "true";
                }
            } else {
                key.holdTimeout = setTimeout(() => {
                    key.holdInterval = setInterval(() => {
                        this.pressKey(key);
                    }, 70);
                }, 400);
                this.pressKey(key);
            }

            // Keep focus on the terminal
            if (window.keyboard.linkedToTerm) window.term[window.currentTerm].term.focus();
            if (this.container.dataset.passwordMode === "false") window.audioManager.stdin.play();
            e.preventDefault();
        };
        key.onmouseup = () => {
            if (/^ESCAPED\|-- (CTRL|SHIFT|ALT).*/.test(key.dataset.cmd)) {
                let cmd = key.dataset.cmd.substr(11);
                if (cmd.startsWith("CTRL")) {
                    this.container.dataset.isCtrlOn = "false";
                }
                if (cmd.startsWith("SHIFT")) {
                    this.container.dataset.isShiftOn = "false";
                }
                if (cmd.startsWith("ALT")) {
                    this.container.dataset.isAltOn = "false";
                }
            } else {
                clearTimeout(key.holdTimeout);
                clearInterval(key.holdInterval);
            }

            key.setAttribute("class", "keyboard_key blink");
            setTimeout(() => {
                key.setAttribute("class", "keyboard_key");
            }, 100);
        };
    }
    // See #440. Extracted out of keydownHandler to keep its cognitive
    // complexity down.
    _updateModifiersOnKeydown(e) {
        if (e.code === "ControlLeft" || e.code === "ControlRight") this.container.dataset.isCtrlOn = true;
        if (e.code === "ShiftLeft" || e.code === "ShiftRight") this.container.dataset.isShiftOn = true;
        if (e.code === "AltLeft" || e.code === "AltRight") this.container.dataset.isAltOn = true;
        if (e.code === "CapsLock" && this.container.dataset.isCapsLckOn !== "true")
            this.container.dataset.isCapsLckOn = true;
        if (e.code === "CapsLock" && this.container.dataset.isCapsLckOn === "true")
            this.container.dataset.isCapsLckOn = false;
    }
    // See #516: whether a keydown should play the stdin sound - true for any
    // non-repeat keypress, and for repeats of keys other than the pure
    // modifier keys (which fire a flood of "repeat" events while held).
    // Extracted out of keydownHandler to keep its cognitive complexity down.
    _isRepeatableKeydown(e) {
        return (
            e.repeat === false ||
            (e.repeat === true &&
                !e.code.startsWith("Shift") &&
                !e.code.startsWith("Alt") &&
                !e.code.startsWith("Control") &&
                !e.code.startsWith("Caps"))
        );
    }
    pressKey(key) {
        let cmd = key.dataset.cmd || "";

        if (this._tryTriggerShortcut(cmd)) return;

        cmd = this._applyKeyModifiers(cmd, key);
        cmd = this._applyAccentModifiers(cmd);

        // Escaped commands
        const escaped = this._handleEscapedCommand(cmd);
        if (escaped.handled) return true;
        cmd = escaped.cmd;

        if (cmd === "\n") {
            if (window.keyboard.linkedToTerm) {
                window.term[window.currentTerm].writelr("");
            } else {
                document.activeElement.dispatchEvent(new CustomEvent("change", { detail: "enter" }));
            }
            return true;
        }

        this._writeCmd(cmd);
    }
    // Writes the given resolved (already accent/modifier/shortcut-processed)
    // cmd into the linked terminal, or into the currently focused input field
    // if the keyboard isn't linked to a terminal. Extracted out of pressKey()
    // to keep its cognitive complexity down.
    _writeCmd(cmd) {
        if (window.keyboard.linkedToTerm) {
            window.term[window.currentTerm].write(cmd);
            return;
        }

        let isDelete = false;
        if (document.activeElement.value !== undefined) {
            switch (cmd) {
                case "":
                    document.activeElement.value = document.activeElement.value.slice(0, -1);
                    isDelete = true;
                    break;
                case "OD":
                    document.activeElement.selectionStart--;
                    document.activeElement.selectionEnd = document.activeElement.selectionStart;
                    break;
                case "OC":
                    document.activeElement.selectionEnd++;
                    document.activeElement.selectionStart = document.activeElement.selectionEnd;
                    break;
                default:
                    if (!this.ctrlseq.includes(cmd.slice(0, 1))) {
                        document.activeElement.value = document.activeElement.value + cmd;
                    }
            }
        }
        // Emulate oninput events
        document.activeElement.dispatchEvent(new CustomEvent("input", { detail: isDelete ? "delete" : "insert" }));
        document.activeElement.focus();
    }

    // Checks the current keyboard-shortcuts category (Ctrl/Alt/Shift combo)
    // for a matching, enabled shortcut and triggers it. Extracted out of
    // pressKey() to keep its cognitive complexity down.
    _tryTriggerShortcut(cmd) {
        let shortcutsCat = "";
        if (this.container.dataset.isCtrlOn === "true") shortcutsCat += "Ctrl";
        if (this.container.dataset.isAltOn === "true") shortcutsCat += "Alt";
        if (this.container.dataset.isShiftOn === "true") shortcutsCat += "Shift";

        if (shortcutsCat.length <= 1) return false;

        let shortcutsTriggered = false;
        this._shortcuts[shortcutsCat].forEach((cut) => {
            if (!cut.enabled) return;

            let trig = cut.trigger
                .toLowerCase()
                .replace("plus", "+")
                .replace("space", " ")
                .replace("tab", "\t")
                .replace(/backspace|delete/, "\b")
                .replace(/esc|escape/, this.ctrlseq[1])
                .replace(/return|enter/, "\r");

            if (cmd !== trig) return;

            if (cut.type === "app") {
                window.useAppShortcut(cut.action);
                shortcutsTriggered = true;
            } else if (cut.type === "shell") {
                let fn = cut.linebreak ? "writelr" : "write";
                window.term[window.currentTerm][fn](cut.action);
            } else {
                console.warn(`${cut.trigger} has unknown type`);
            }
        });

        return shortcutsTriggered;
    }
    // Applies the active modifier key (shift/capslock/ctrl/alt/altshift/fn)'s
    // data-*_cmd override, if any, to cmd. Extracted out of pressKey() to keep
    // its cognitive complexity down.
    _applyKeyModifiers(cmd, key) {
        if (
            (this.container.dataset.isShiftOn === "true" && key.dataset.shift_cmd) ||
            (this.container.dataset.isCapsLckOn === "true" && key.dataset.shift_cmd)
        )
            cmd = key.dataset.shift_cmd;
        if (this.container.dataset.isCapsLckOn === "true" && key.dataset.capslck_cmd) cmd = key.dataset.capslck_cmd;
        if (this.container.dataset.isCtrlOn === "true" && key.dataset.ctrl_cmd) cmd = key.dataset.ctrl_cmd;
        if (this.container.dataset.isAltOn === "true" && key.dataset.alt_cmd) cmd = key.dataset.alt_cmd;
        if (
            this.container.dataset.isAltOn === "true" &&
            this.container.dataset.isShiftOn === "true" &&
            key.dataset.altshift_cmd
        )
            cmd = key.dataset.altshift_cmd;
        if (this.container.dataset.isFnOn === "true" && key.dataset.fn_cmd) cmd = key.dataset.fn_cmd;
        return cmd;
    }
    // Applies any pending diacritic modifier (circumflex, acute, etc, each
    // primed by a previous ESCAPED command) to cmd, resetting the flag that
    // primed it. Extracted out of pressKey() to keep its cognitive complexity
    // down.
    _applyAccentModifiers(cmd) {
        if (this.container.dataset.isNextCircum === "true") {
            cmd = this.addCircum(cmd);
            this.container.dataset.isNextCircum = "false";
        }
        if (this.container.dataset.isNextTrema === "true") {
            cmd = this.addTrema(cmd);
            this.container.dataset.isNextTrema = "false";
        }
        if (this.container.dataset.isNextAcute === "true") {
            cmd = this.addAcute(cmd);
            this.container.dataset.isNextAcute = "false";
        }
        if (this.container.dataset.isNextGrave === "true") {
            cmd = this.addGrave(cmd);
            this.container.dataset.isNextGrave = "false";
        }
        if (this.container.dataset.isNextCaron === "true") {
            cmd = this.addCaron(cmd);
            this.container.dataset.isNextCaron = "false";
        }
        if (this.container.dataset.isNextBar === "true") {
            cmd = this.addBar(cmd);
            this.container.dataset.isNextBar = "false";
        }
        if (this.container.dataset.isNextBreve === "true") {
            cmd = this.addBreve(cmd);
            this.container.dataset.isNextBreve = "false";
        }
        if (this.container.dataset.isNextTilde === "true") {
            cmd = this.addTilde(cmd);
            this.container.dataset.isNextTilde = "false";
        }
        if (this.container.dataset.isNextMacron === "true") {
            cmd = this.addMacron(cmd);
            this.container.dataset.isNextMacron = "false";
        }
        if (this.container.dataset.isNextCedilla === "true") {
            cmd = this.addCedilla(cmd);
            this.container.dataset.isNextCedilla = "true";
        }
        if (this.container.dataset.isNextOverring === "true") {
            cmd = this.addOverring(cmd);
            this.container.dataset.isNextOverring = "false";
        }
        if (this.container.dataset.isNextGreek === "true") {
            cmd = this.toGreek(cmd);
            this.container.dataset.isNextGreek = "false";
        }
        if (this.container.dataset.isNextIotasub === "true") {
            cmd = this.addIotasub(cmd);
            this.container.dataset.isNextIotasub = "false";
        }
        return cmd;
    }
    // Handles an "ESCAPED|-- X" pseudo-command by setting the corresponding
    // dataset flag. Returns {handled: true} if cmd matched a known escaped
    // command (caller should stop processing), otherwise {handled: false,
    // cmd} with cmd set to the ESCAPED|-- -stripped value if the prefix
    // matched (even when no case did), or left untouched if it didn't -
    // matching the original inline code's fallthrough behavior exactly.
    // Extracted out of pressKey() to keep its cognitive complexity down.
    _handleEscapedCommand(cmd) {
        if (!cmd.startsWith("ESCAPED|-- ")) return { handled: false, cmd };

        const stripped = cmd.substr(11);
        switch (stripped) {
            case "CAPSLCK: ON":
                this.container.dataset.isCapsLckOn = "true";
                return { handled: true };
            case "CAPSLCK: OFF":
                this.container.dataset.isCapsLckOn = "false";
                return { handled: true };
            case "FN: ON":
                this.container.dataset.isFnOn = "true";
                return { handled: true };
            case "FN: OFF":
                this.container.dataset.isFnOn = "false";
                return { handled: true };
            case "CIRCUM":
                this.container.dataset.isNextCircum = "true";
                return { handled: true };
            case "TREMA":
                this.container.dataset.isNextTrema = "true";
                return { handled: true };
            case "ACUTE":
                this.container.dataset.isNextAcute = "true";
                return { handled: true };
            case "GRAVE":
                this.container.dataset.isNextGrave = "true";
                return { handled: true };
            case "CARON":
                this.container.dataset.isNextCaron = "true";
                return { handled: true };
            case "BAR":
                this.container.dataset.isNextBar = "true";
                return { handled: true };
            case "BREVE":
                this.container.dataset.isNextBreve = "true";
                return { handled: true };
            case "TILDE":
                this.container.dataset.isNextTilde = "true";
                return { handled: true };
            case "MACRON":
                this.container.dataset.isNextMacron = "true";
                return { handled: true };
            case "CEDILLA":
                this.container.dataset.isNextCedilla = "true";
                return { handled: true };
            case "OVERRING":
                this.container.dataset.isNextOverring = "true";
                return { handled: true };
            case "GREEK":
                this.container.dataset.isNextGreek = "true";
                return { handled: true };
            case "IOTASUB":
                this.container.dataset.isNextIotasub = "true";
                return { handled: true };
        }
        return { handled: false, cmd: stripped };
    }

    togglePasswordMode() {
        let d = this.container.dataset.passwordMode;
        d = d === "true" ? "false" : "true";
        this.container.dataset.passwordMode = d;
        window.passwordMode = d;
        return d;
    }
    addCircum(char) {
        const map = {
            a: "â",
            A: "Â",
            z: "ẑ",
            Z: "Ẑ",
            e: "ê",
            E: "Ê",
            y: "ŷ",
            Y: "Ŷ",
            u: "û",
            U: "Û",
            i: "î",
            I: "Î",
            o: "ô",
            O: "Ô",
            s: "ŝ",
            S: "Ŝ",
            g: "ĝ",
            G: "Ĝ",
            h: "ĥ",
            H: "Ĥ",
            j: "ĵ",
            J: "Ĵ",
            w: "ŵ",
            W: "Ŵ",
            c: "ĉ",
            C: "Ĉ",
            1: "¹",
            2: "²",
            3: "³",
            4: "⁴",
            5: "⁵",
            6: "⁶",
            7: "⁷",
            8: "⁸",
            9: "⁹",
            0: "⁰",
        };
        return map[char] ?? char;
    }
    addTrema(char) {
        switch (char) {
            case "a":
                return "ä";
            case "A":
                return "Ä";
            case "e":
                return "ë";
            case "E":
                return "Ë";
            case "t":
                return "ẗ";
            // My keyboard says no uppercase ẗ
            case "y":
                return "ÿ";
            case "Y":
                return "Ÿ";
            case "u":
                return "ü";
            case "U":
                return "Ü";
            case "i":
                return "ï";
            case "I":
                return "Ï";
            case "o":
                return "ö";
            case "O":
                return "Ö";
            case "h":
                return "ḧ";
            case "H":
                return "Ḧ";
            case "w":
                return "ẅ";
            case "W":
                return "Ẅ";
            case "x":
                return "ẍ";
            case "X":
                return "Ẍ";
            default:
                return char;
        }
    }
    addAcute(char) {
        const map = {
            a: "á",
            A: "Á",
            c: "ć",
            C: "Ć",
            e: "é",
            E: "E",
            g: "ǵ",
            G: "Ǵ",
            i: "í",
            I: "Í",
            j: "ȷ́",
            J: "J́",
            k: "ḱ",
            K: "Ḱ",
            l: "ĺ",
            L: "Ĺ",
            m: "ḿ",
            M: "Ḿ",
            n: "ń",
            N: "Ń",
            o: "ó",
            O: "Ó",
            p: "ṕ",
            P: "Ṕ",
            r: "ŕ",
            R: "Ŕ",
            s: "ś",
            S: "Ś",
            u: "ú",
            U: "Ú",
            v: "v́",
            V: "V́",
            w: "ẃ",
            W: "Ẃ",
            y: "ý",
            Y: "Ý",
            z: "ź",
            Z: "Ź",
            ê: "ế",
            Ê: "Ế",
            ç: "ḉ",
            Ç: "Ḉ",
        };
        return map[char] ?? char;
    }
    addGrave(char) {
        switch (char) {
            case "a":
                return "à";
            case "A":
                return "À";
            case "e":
                return "è";
            case "E":
                return "È";
            case "i":
                return "ì";
            case "I":
                return "Ì";
            case "m":
                return "m̀";
            case "M":
                return "M̀";
            case "n":
                return "ǹ";
            case "N":
                return "Ǹ";
            case "o":
                return "ò";
            case "O":
                return "Ò";
            case "u":
                return "ù";
            case "U":
                return "Ù";
            case "v":
                return "v̀";
            case "V":
                return "V̀";
            case "w":
                return "ẁ";
            case "W":
                return "Ẁ";
            case "y":
                return "ỳ";
            case "Y":
                return "Ỳ";
            case "ê":
                return "ề";
            case "Ê":
                return "Ề";
            default:
                return char;
        }
    }
    addCaron(char) {
        const map = {
            a: "ǎ",
            A: "Ǎ",
            c: "č",
            C: "Č",
            d: "ď",
            D: "Ď",
            e: "ě",
            E: "Ě",
            g: "ǧ",
            G: "Ǧ",
            h: "ȟ",
            H: "Ȟ",
            i: "ǐ",
            I: "Ǐ",
            j: "ǰ",
            k: "ǩ",
            K: "Ǩ",
            l: "ľ",
            L: "Ľ",
            n: "ň",
            N: "Ň",
            o: "ǒ",
            O: "Ǒ",
            r: "ř",
            R: "Ř",
            s: "š",
            S: "Š",
            t: "ť",
            T: "Ť",
            u: "ǔ",
            U: "Ǔ",
            z: "ž",
            Z: "Ž",
            1: "₁",
            2: "₂",
            3: "₃",
            4: "₄",
            5: "₅",
            6: "₆",
            7: "₇",
            8: "₈",
            9: "₉",
            0: "₀",
        };
        return map[char] ?? char;
    }
    addBar(char) {
        const map = {
            a: "ⱥ",
            A: "Ⱥ",
            b: "ƀ",
            B: "Ƀ",
            c: "ȼ",
            C: "Ȼ",
            d: "đ",
            D: "Đ",
            e: "ɇ",
            E: "Ɇ",
            g: "ǥ",
            G: "Ǥ",
            h: "ħ",
            H: "Ħ",
            i: "ɨ",
            I: "Ɨ",
            j: "ɉ",
            J: "Ɉ",
            l: "ł",
            L: "Ł",
            o: "ø",
            O: "Ø",
            p: "ᵽ",
            P: "Ᵽ",
            r: "ɍ",
            R: "Ɍ",
            t: "ŧ",
            T: "Ŧ",
            u: "ʉ",
            U: "Ʉ",
            y: "ɏ",
            Y: "Ɏ",
            z: "ƶ",
            Z: "Ƶ",
        };
        return map[char] ?? char;
    }
    addBreve(char) {
        switch (char) {
            case "a":
                return "ă";
            case "A":
                return "Ă";
            case "e":
                return "ĕ";
            case "E":
                return "Ĕ";
            case "g":
                return "ğ";
            case "G":
                return "Ğ";
            case "i":
                return "ĭ";
            case "I":
                return "Ĭ";
            case "o":
                return "ŏ";
            case "O":
                return "Ŏ";
            case "u":
                return "ŭ";
            case "U":
                return "Ŭ";
            case "à":
                return "ằ";
            case "À":
                return "Ằ";
            default:
                return char;
        }
    }
    addTilde(char) {
        switch (char) {
            case "a":
                return "ã";
            case "A":
                return "Ã";
            case "e":
                return "ẽ";
            case "E":
                return "Ẽ";
            case "i":
                return "ĩ";
            case "I":
                return "Ĩ";
            case "n":
                return "ñ";
            case "N":
                return "Ñ";
            case "o":
                return "õ";
            case "O":
                return "Õ";
            case "u":
                return "ũ";
            case "U":
                return "Ũ";
            case "v":
                return "ṽ";
            case "V":
                return "Ṽ";
            case "y":
                return "ỹ";
            case "Y":
                return "Ỹ";
            case "ê":
                return "ễ";
            case "Ê":
                return "Ễ";
            default:
                return char;
        }
    }
    addMacron(char) {
        switch (char) {
            case "a":
                return "ā";
            case "A":
                return "Ā";
            case "e":
                return "ē";
            case "E":
                return "Ē";
            case "g":
                return "ḡ";
            case "G":
                return "Ḡ";
            case "i":
                return "ī";
            case "I":
                return "Ī";
            case "o":
                return "ō";
            case "O":
                return "Ō";
            case "u":
                return "ū";
            case "U":
                return "Ū";
            case "y":
                return "ȳ";
            case "Y":
                return "Ȳ";
            case "é":
                return "ḗ";
            case "É":
                return "Ḗ";
            case "è":
                return "ḕ";
            case "È":
                return "Ḕ";
            default:
                return char;
        }
    }
    addCedilla(char) {
        switch (char) {
            case "c":
                return "ç";
            case "C":
                return "Ç";
            case "d":
                return "ḑ";
            case "D":
                return "Ḑ";
            case "e":
                return "ȩ";
            case "E":
                return "Ȩ";
            case "g":
                return "ģ";
            case "G":
                return "Ģ";
            case "h":
                return "ḩ";
            case "H":
                return "Ḩ";
            case "k":
                return "ķ";
            case "K":
                return "Ķ";
            case "l":
                return "ļ";
            case "L":
                return "Ļ";
            case "n":
                return "ņ";
            case "N":
                return "Ņ";
            case "r":
                return "ŗ";
            case "R":
                return "Ŗ";
            case "s":
                return "ş";
            case "S":
                return "Ş";
            case "t":
                return "ţ";
            case "T":
                return "Ţ";
            default:
                return char;
        }
    }
    addOverring(char) {
        switch (char) {
            case "a":
                return "å";
            case "A":
                return "Å";
            case "u":
                return "ů";
            case "U":
                return "Ů";
            case "w":
                return "ẘ"; // capital w with overring not supported on bépo layout apparently
            case "y":
                return "ẙ"; // same for capital y with overring
            default:
                return char;
        }
    }
    toGreek(char) {
        const map = {
            b: "β",
            p: "π",
            P: "Π",
            d: "δ",
            D: "Δ",
            l: "λ",
            L: "Λ",
            j: "θ",
            J: "Θ",
            z: "ζ",
            w: "ω",
            W: "Ω",
            A: "α",
            u: "υ",
            U: "Υ",
            i: "ι",
            e: "ε",
            t: "τ",
            s: "σ",
            S: "Σ",
            r: "ρ",
            R: "Ρ",
            n: "ν",
            m: "μ",
            y: "ψ",
            Y: "Ψ",
            x: "ξ",
            X: "Ξ",
            k: "κ",
            q: "χ",
            Q: "Χ",
            g: "γ",
            G: "Γ",
            h: "η",
            f: "φ",
            F: "Φ",
        };
        return map[char] ?? char;
    }
    addIotasub(char) {
        switch (char) {
            case "o":
                return "ǫ";
            case "O":
                return "Ǫ";
            case "a":
                return "ą";
            case "A":
                return "Ą";
            case "u":
                return "ų";
            case "U":
                return "Ų";
            case "i":
                return "į";
            case "I":
                return "Į";
            case "e":
                return "ę";
            case "E":
                return "Ę";
            default:
                return char;
        }
    }
}

window.Keyboard = Keyboard;
