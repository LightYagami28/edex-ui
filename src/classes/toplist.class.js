function formatRuntime(ms) {
    const msInDay = 24 * 60 * 60 * 1000;
    let days = Math.floor(ms / msInDay);
    let remainingMS = ms % msInDay;

    const msInHour = 60 * 60 * 1000;
    let hours = Math.floor(remainingMS / msInHour);
    remainingMS = ms % msInHour;

    let msInMin = 60 * 1000;
    let minutes = Math.floor(remainingMS / msInMin);
    remainingMS = ms % msInMin;

    let seconds = Math.floor(remainingMS / 1000);

    return `${days < 10 ? "0" : ""}${days}:${hours < 10 ? "0" : ""}${hours}:${minutes < 10 ? "0" : ""}${minutes}:${seconds < 10 ? "0" : ""}${seconds}`;
}

// Collapses same-named entries (a process's individual threads, when
// systeminformation reports them separately) into one, summing their cpu/mem.
// Shared by updateList() and processList()'s updateProcessList().
function dedupeProcesses(list) {
    return list
        .sort((a, b) => a.pid - b.pid)
        .filter((e, index, a) => {
            let i = a.findIndex((x) => x.name === e.name);
            if (i !== -1 && i !== index) {
                a[i].cpu = a[i].cpu + e.cpu;
                a[i].mem = a[i].mem + e.mem;
                return false;
            }
            return true;
        });
}

// String comparator matching this file's "ascending" convention (which, for
// historical reasons, sorts a > b first when ascending is true - see below).
function compareByString(a, b, ascending) {
    if (a === b) return 0;
    if (ascending) return a > b ? -1 : 1;
    return a < b ? -1 : 1;
}

// The sort comparator backing processList()'s sortable column headers.
function compareProcesses(a, b, sortKey, ascending) {
    switch (sortKey) {
        case "PID":
            return ascending ? a.pid - b.pid : b.pid - a.pid;
        case "Name":
            return compareByString(a.name, b.name, ascending);
        case "User":
            return compareByString(a.user, b.user, ascending);
        case "CPU":
            return ascending ? a.cpu - b.cpu : b.cpu - a.cpu;
        case "Memory":
            return ascending ? a.mem - b.mem : b.mem - a.mem;
        case "State":
            // Note: State's sort direction never respects `ascending` -
            // matches the original behavior (likely an existing oversight,
            // left as-is since this is a pure complexity refactor).
            return compareByString(a.state, b.state, false);
        case "Started":
            return ascending
                ? Date.parse(a.started) - Date.parse(b.started)
                : Date.parse(b.started) - Date.parse(a.started);
        case "Runtime":
            return ascending ? a.runtime - b.runtime : b.runtime - a.runtime;
        default:
            // default to the same sorting as the toplist
            return (b.cpu - a.cpu) * 100 + b.mem - a.mem;
    }
}

// Resets every sortable column header's sort-direction arrow. Used before
// (re-)applying the arrow to the column that was just clicked.
function clearHeaderArrows(headers) {
    for (let header of headers) {
        header.textContent = header.textContent.replace("▲", "").replace("▼", "");
    }
}

class Toplist {
    constructor(parentId) {
        if (!parentId) throw new Error("Missing parameters");

        // Create DOM
        this.parent = document.getElementById(parentId);
        this._element = document.createElement("div");
        this._element.setAttribute("id", "mod_toplist");
        this._element.innerHTML = `<h1>TOP PROCESSES<i>PID | NAME | CPU | MEM</i></h1><br>
        <table id="mod_toplist_table"></table>`;
        this._element.onclick = this.processList;

        this.parent.append(this._element);

        this.currentlyUpdating = false;

        this.updateList();
        this.listUpdater = setInterval(() => {
            this.updateList();
        }, 2000);
    }
    updateList() {
        if (this.currentlyUpdating) return;

        this.currentlyUpdating = true;
        window.si.processes().then((data) => {
            if (window.settings.excludeThreadsFromToplist === true) {
                data.list = dedupeProcesses(data.list);
            }

            let list = data.list
                .sort((a, b) => {
                    return (b.cpu - a.cpu) * 100 + b.mem - a.mem;
                })
                .splice(0, 5);

            document.querySelectorAll("#mod_toplist_table > tr").forEach((el) => {
                el.remove();
            });
            list.forEach((proc) => {
                let el = document.createElement("tr");
                el.innerHTML = `<td>${proc.pid}</td>
                                <td><strong>${proc.name}</strong></td>
                                <td>${Math.round(proc.cpu * 10) / 10}%</td>
                                <td>${Math.round(proc.mem * 10) / 10}%</td>`;
                document.getElementById("mod_toplist_table").append(el);
            });
            this.currentlyUpdating = false;
        });
    }

    processList() {
        let sortKey;
        let ascending = false;
        let removed = false;
        let currentlyUpdating = false;

        function setSortKey(fieldName) {
            if (sortKey === fieldName) {
                if (ascending) {
                    sortKey = undefined;
                    ascending = false;
                } else {
                    ascending = true;
                }
            } else {
                sortKey = fieldName;
                ascending = false;
            }
        }

        function updateProcessList() {
            if (currentlyUpdating) return;
            currentlyUpdating = true;
            window.si.processes().then((data) => {
                if (window.settings.excludeThreadsFromToplist === true) {
                    data.list = dedupeProcesses(data.list);
                }

                data.list.forEach((proc) => {
                    proc.runtime = new Date(Date.now() - Date.parse(proc.started));
                });

                currentlyUpdating = false;
                let list = data.list.sort((a, b) => compareProcesses(a, b, sortKey, ascending));

                if (removed) clearInterval(updateInterval);
                else {
                    document.querySelectorAll("#processList > tr").forEach((el) => {
                        el.remove();
                    });

                    list.forEach((proc) => {
                        let el = document.createElement("tr");
                        el.innerHTML = `<td class="pid">${proc.pid}</td>
                            <td class="name">${proc.name}</td>
                            <td class="user">${proc.user}</td>
                            <td class="cpu">${Math.round(proc.cpu * 10) / 10}%</td>
                            <td class="mem">${Math.round(proc.mem * 10) / 10}%</td>
                            <td class="state">${proc.state}</td>
                            <td class="started">${proc.started}</td>
                            <td class="runtime">${formatRuntime(proc.runtime)}</td>`;
                        document.getElementById("processList").append(el);
                    });
                }
            });
        }

        window.keyboard.detach();
        // Modal self-registers in window.modals[this.id], no local reference needed.
        // prettier-ignore
        new Modal( // NOSONAR
            {
                type: "custom",
                title: "Active Processes",
                html: `
<table id="processContainer">
    <thead>
        <tr>
            <td class="pid header">PID</td>
            <td class="name header">Name</td>
            <td class="user header">User</td>
            <td class="cpu header">CPU</td>
            <td class="mem header">Memory</td>
            <td class="state header">State</td>
            <td class="started header">Started</td>
            <td class="runtime header">Runtime</td>
        </tr>
    </thead>
    <tbody id="processList">
    </tbody>
  </table>`,
            },
            () => {
                removed = true;
                //clearInterval(updateInterval);
            }
        );

        let headers = document.getElementsByClassName("header");
        for (let header of headers) {
            let title = header.textContent;
            header.addEventListener("click", () => {
                clearHeaderArrows(headers);
                setSortKey(title);
                if (sortKey) {
                    header.textContent = `${title}${ascending ? "\u25B2" : "\u25BC"}`;
                }
            });
        }

        updateProcessList();
        window.keyboard.attach();
        window.term[window.currentTerm].term.focus();
        let updateInterval = setInterval(updateProcessList, 1000);
    }
}

window.Toplist = Toplist;
