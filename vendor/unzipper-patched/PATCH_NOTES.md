# Patch notes (LightYagami28 fork)

This is a fork of [unzipper@0.12.5](https://www.npmjs.com/package/unzipper/v/0.12.5),
published to work around [SNYK-JS-UNZIPPER-18365659](https://security.snyk.io/vuln/SNYK-JS-UNZIPPER-18365659)
(CVE-2026-17514 / CVE-2026-59972, a Zip Slip / arbitrary file write finding)
being flagged by security scanners against edex-ui, with no fixed upstream
version available at the time of writing.

## What was actually verified

Before patching anything, the described exploit was reproduced against the
unpatched `unzipper@0.12.5` code directly: a crafted zip with both a classic
`../../` traversal entry and the "sibling-prefix" entry described in the
advisory (e.g. `../dest-evil/escaped.txt`, chosen so the resolved path
shares a string prefix with the destination without being nested inside it).

**Neither entry escaped the destination directory.** The code already
validates extraction paths with `path.relative(opts.path, extractPath)` and
rejects anything that starts with `..` or is absolute - which correctly
catches the sibling-prefix case too, since a path outside the destination's
subtree always relativizes to something starting with `..`. This does not
match the advisory's description of the flaw as a naive
`extractPath.indexOf(opts.path) != 0` check; whatever version that
description was written against, it isn't what's in `lib/extract.js` /
`lib/Open/directory.js` in this release.

## What this fork changes

Out of caution (not because a bypass was found), both `lib/extract.js` and
`lib/Open/directory.js` gained one extra line, on top of the existing
`path.relative` check: a literal `extractPath.startsWith(opts.path + path.sep)`
requirement, matching the remediation Snyk's own advisory describes. This is
redundant with the existing check for every input tested, but costs nothing
and closes the gap if the advisory's description turns out to apply to some
code path not covered by the test above.

No other files were changed. No build step was re-run; this fork ships the
same pre-built CommonJS `lib/` output as the published 0.12.5 npm package,
patched directly, rather than rebuilding from the upstream ESM source tree
(which has diverged significantly and isn't what's actually installed here).

## Status

This fork exists to unblock a security scanner finding, not because
`edex-ui` observed real-world exploitation. If upstream ships a fixed
version, switch back to the npm registry package instead of this fork.
