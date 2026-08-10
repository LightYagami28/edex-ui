const fs = require("node:fs");
const path = require("node:path");
const stdout = process.stdout;
const UglifyJS = require("terser");
const CleanCSS = require("clean-css");
JSON.minify = require("node-json-minify");

function writeMinified(path, data) {
    return new Promise((res, rej) => {
        fs.writeFile(path, data, (err) => {
            if (err) {
                stdout.write(" -  ❌\n\n\n", () => {
                    rej(err);
                });
            }
            stdout.write(" -  ✓\n", () => {
                res();
            });
        });
    });
}

async function recursiveMinify(dirPath) {
    let files;
    try {
        files = fs.readdirSync(dirPath);
    } catch {
        return;
    }
    if (files.length > 0) {
        for (const file of files) {
            let filePath = dirPath + "/" + file;
            if (fs.statSync(filePath).isFile()) {
                // Do not process grid.json because it's heavy and pre-minified, and themes and keyboard files to leave them in a human-readable state
                if (filePath.endsWith(".json") && !filePath.endsWith("icons.json")) continue;
                // See #446
                if (filePath.endsWith("file-icons-match.js")) continue;
                stdout.write(filePath.slice(filePath.indexOf("prebuild-src/") + 13) + "...");

                switch (filePath.split(".").pop()) {
                    case "js": {
                        let minified = await UglifyJS.minify(fs.readFileSync(filePath, { encoding: "utf-8" }), {
                            compress: {
                                dead_code: false,
                                unused: false,
                                warnings: true,
                            },
                            output: {
                                beautify: false,
                                ecma: 6,
                            },
                        });
                        if (!minified.error) {
                            await writeMinified(filePath, minified.code);
                        } else {
                            stdout.write(" -  ❌\n\n\n");
                            throw minified.error;
                        }
                        break;
                    }
                    case "css": {
                        let output = new CleanCSS({ level: 2 }).minify(
                            fs.readFileSync(filePath, { encoding: "utf-8" })
                        );
                        if (output.errors.length >= 1) {
                            stdout.write(" -  ❌\n\n\n");
                            throw output.errors;
                        } else {
                            await writeMinified(filePath, output.styles);
                        }
                        break;
                    }
                    case "json": {
                        let out;
                        try {
                            out = JSON.minify(fs.readFileSync(filePath, { encoding: "utf-8" }));
                        } catch (err) {
                            stdout.write(" -  ❌\n\n\n");
                            throw err;
                        }
                        await writeMinified(filePath, out);
                        break;
                    }
                    default:
                        stdout.write("\n");
                }
            } else {
                await recursiveMinify(filePath);
            }
        }
    }
}

recursiveMinify(path.join(__dirname, "prebuild-src"));
