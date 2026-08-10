class Netstat {
    constructor(parentId) {
        if (!parentId) throw new Error("Missing parameters");

        const eDEX = window.eDEX;

        // Create DOM
        this.parent = document.getElementById(parentId);
        this.parent.innerHTML += `<div id="mod_netstat">
            <div id="mod_netstat_inner">
                <h1>NETWORK STATUS<i id="mod_netstat_iname"></i></h1>
                <div id="mod_netstat_innercontainer">
                    <div>
                        <h1>STATE</h1>
                        <h2>UNKNOWN</h2>
                    </div>
                    <div>
                        <h1>IPv4</h1>
                        <h2>--.--.--.--</h2>
                    </div>
                    <div>
                        <h1>PING</h1>
                        <h2>--ms</h2>
                    </div>
                </div>
            </div>
        </div>`;

        this.offline = false;
        // GeoIP lookups (and thus external IP tracking) live in the main
        // process now; nothing here needs to wait on the database anymore.
        this._extIpLookupPending = false;
        this.iface = null;
        this.failedAttempts = {};
        this.runsBeforeGeoIPUpdate = 0;

        this.geoLookup = {
            get: (ip) => eDEX.geoip.lookup(ip),
        };

        // Init updaters
        this.updateInfo();
        this.infoUpdater = setInterval(() => {
            this.updateInfo();
        }, 2000);
    }
    updateInfo() {
        window.si.networkInterfaces().then(async (data) => {
            const found = this._findInterface(data);
            if (found.abort) return;
            const net = found.net;

            if (net.ip4 !== this.internalIPv4) this.runsBeforeGeoIPUpdate = 0;

            this.iface = net.iface;
            this.internalIPv4 = net.ip4;
            document.getElementById("mod_netstat_iname").innerText = "Interface: " + net.iface;

            let offline = false;
            let p;
            if (net.ip4 === "127.0.0.1") {
                offline = true;
            } else {
                this._maybeUpdateExternalIp(net);
                p = await this.ping(window.settings.pingAddr || "1.1.1.1", 80, net.ip4).catch(() => {
                    offline = true;
                });
            }

            this.offline = offline;
            if (offline) {
                this._setOfflineUI();
            } else {
                document.querySelector("#mod_netstat_innercontainer > div:first-child > h2").innerHTML = "ONLINE";
                document.querySelector("#mod_netstat_innercontainer > div:nth-child(3) > h2").innerHTML =
                    Math.round(p) + "ms";
            }
        });
    }
    // Finds the networkInterfaces() entry to use: either the user's configured
    // window.settings.iface, or (by default) the first external, IPv4
    // connected interface that has a MAC address set. Extracted out of
    // updateInfo() to keep its cognitive complexity down.
    _findInterface(data) {
        let net = data[0];
        let netID = 0;

        if (typeof window.settings.iface === "string") {
            while (net.iface !== window.settings.iface) {
                netID++;
                if (data[netID]) {
                    net = data[netID];
                } else {
                    // No detected interface has the custom iface name, fallback to automatic detection on next loop
                    window.settings.iface = false;
                    return { abort: true };
                }
            }
        } else {
            while (net.operstate !== "up" || net.internal === true || net.ip4 === "" || net.mac === "") {
                netID++;
                if (data[netID]) {
                    net = data[netID];
                } else {
                    // No external connection!
                    this.iface = null;
                    document.getElementById("mod_netstat_iname").innerText = "Interface: (offline)";
                    this.offline = true;
                    this._setOfflineUI();
                    break;
                }
            }
        }

        return { abort: false, net };
    }
    _setOfflineUI() {
        document.querySelector("#mod_netstat_innercontainer > div:first-child > h2").innerHTML = "OFFLINE";
        document.querySelector("#mod_netstat_innercontainer > div:nth-child(2) > h2").innerHTML = "--.--.--.--";
        document.querySelector("#mod_netstat_innercontainer > div:nth-child(3) > h2").innerHTML = "--ms";
    }
    // Kicks off (at most one at a time) the external-IP/geoIP lookup used to
    // populate the IPv4 display, throttled by runsBeforeGeoIPUpdate. Extracted
    // out of updateInfo() to keep its cognitive complexity down.
    _maybeUpdateExternalIp(net) {
        const eDEX = window.eDEX;
        if (this.runsBeforeGeoIPUpdate === 0 && !this._extIpLookupPending) {
            this._extIpLookupPending = true;
            eDEX.net
                .getExternalIp(net.ip4)
                .then(async (ip) => {
                    let geo = await eDEX.geoip.lookup(ip);
                    this.ipinfo = { ip, geo: geo?.location };

                    document.querySelector("#mod_netstat_innercontainer > div:nth-child(2) > h2").innerHTML =
                        window._escapeHtml(ip);

                    this.runsBeforeGeoIPUpdate = 10;
                })
                .catch((e) => {
                    this.failedAttempts[e] = (this.failedAttempts[e] || 0) + 1;
                    if (this.failedAttempts[e] > 2) return;
                    console.warn(e);
                    eDEX.ipc.send("log", "note", "NetStat: Error fetching data from myexternalip.com");
                    eDEX.ipc.send("log", "debug", `Error: ${e}`);
                })
                .finally(() => {
                    this._extIpLookupPending = false;
                });
        } else if (this.runsBeforeGeoIPUpdate !== 0) {
            this.runsBeforeGeoIPUpdate = this.runsBeforeGeoIPUpdate - 1;
        }
    }
    ping(target, port, local) {
        return window.eDEX.net.ping(target, port, local);
    }
}

window.Netstat = Netstat;
