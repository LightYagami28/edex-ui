class UpdateChecker {
    constructor() {
        const eDEX = window.eDEX;

        this._fail = (e) => {
            eDEX.ipc.send("log", "note", "UpdateChecker: Could not fetch latest release from GitHub's API.");
            eDEX.ipc.send("log", "debug", `Error: ${e}`);
        };

        eDEX.app
            .getVersion()
            .then((current) =>
                eDEX.net.checkForUpdate().then((release) => {
                    if (release.tag_name.slice(1) === current) {
                        eDEX.ipc.send("log", "info", "UpdateChecker: Running latest version.");
                    } else if (
                        Number(release.tag_name.slice(1).replace(/\./g, "")) <
                        Number(current.replace("-pre", "").replace(/\./g, ""))
                    ) {
                        eDEX.ipc.send("log", "info", "UpdateChecker: Running an unreleased, development version.");
                    } else {
                        new Modal({
                            type: "info",
                            title: "New version available",
                            message: `eDEX-UI <strong>${release.tag_name}</strong> is now available.<br/>Head over to <a href="#" onclick="window.eDEX.shell.openExternal('${release.html_url}')">github.com</a> to download the latest version.`,
                        });
                        eDEX.ipc.send("log", "info", `UpdateChecker: New version ${release.tag_name} available.`);
                    }
                })
            )
            .catch((e) => this._fail(e));
    }
}

window.UpdateChecker = UpdateChecker;
