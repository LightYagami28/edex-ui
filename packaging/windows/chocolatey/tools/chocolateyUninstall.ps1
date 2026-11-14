$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

Get-ChildItem $toolsDir -Directory -Filter 'edex-ui-*-win-x64' | Remove-Item -Recurse -Force
