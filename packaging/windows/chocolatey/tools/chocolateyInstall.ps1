$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Chocolatey mette uno shim per ogni .exe sotto tools: `edex-ui` è subito nel PATH.
# Su Windows arm64 gira lo zip x64 in emulazione; lo zip arm64 nativo è per winget, Scoop e lo zip portatile.
Install-ChocolateyZipPackage -PackageName $env:ChocolateyPackageName `
  -Url64bit '@URL_BASE@/edex-ui-@VERSION@-win-x64.zip' `
  -Checksum64 '@SHA256_WIN_X64@' -ChecksumType64 'sha256' `
  -UnzipLocation $toolsDir
