$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Lo zip si estrae in tools\edex-ui-<versione>-win-x64; Chocolatey rimuove lo shim da solo.
Get-ChildItem $toolsDir -Directory -Filter 'edex-ui-*-win-x64' | Remove-Item -Recurse -Force
