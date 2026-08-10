class AudioManager {
    constructor() {
        // howler.js is loaded as a classic <script> in ui.html (UMD build,
        // sets window.Howl/window.Howler itself); audio files are fetched as
        // static assets via relative URL since they can no longer be
        // resolved with path.join(__dirname, ...).
        const { Howl, Howler } = window;

        if (window.settings.audio === true) {
            if (window.settings.disableFeedbackAudio === false) {
                this.stdout = new Howl({
                    src: ["assets/audio/stdout.wav"],
                    volume: 0.4,
                });
                this.stdin = new Howl({
                    src: ["assets/audio/stdin.wav"],
                    volume: 0.4,
                });
                this.folder = new Howl({
                    src: ["assets/audio/folder.wav"],
                });
                this.granted = new Howl({
                    src: ["assets/audio/granted.wav"],
                });
            }
            this.keyboard = new Howl({
                src: ["assets/audio/keyboard.wav"],
            });
            this.theme = new Howl({
                src: ["assets/audio/theme.wav"],
            });
            this.expand = new Howl({
                src: ["assets/audio/expand.wav"],
            });
            this.panels = new Howl({
                src: ["assets/audio/panels.wav"],
            });
            this.scan = new Howl({
                src: ["assets/audio/scan.wav"],
            });
            this.denied = new Howl({
                src: ["assets/audio/denied.wav"],
            });
            this.info = new Howl({
                src: ["assets/audio/info.wav"],
            });
            this.alarm = new Howl({
                src: ["assets/audio/alarm.wav"],
            });
            this.error = new Howl({
                src: ["assets/audio/error.wav"],
            });

            Howler.volume(window.settings.audioVolume);
        } else {
            Howler.volume(0.0);
        }

        // Return a proxy to avoid errors if sounds aren't loaded
        return new Proxy(this, {
            get: (target, sound) => {
                if (sound in target) {
                    return target[sound];
                } else {
                    return {
                        play: () => {
                            return true;
                        },
                    };
                }
            },
        });
    }
}

window.AudioManager = AudioManager;
