$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

Install-ChocolateyZipPackage -PackageName $env:ChocolateyPackageName `
  -Url64bit '@URL_BASE@/edex-ui-@VERSION@-win-x64.zip' `
  -Checksum64 '@SHA256_WIN_X64@' -ChecksumType64 'sha256' `
  -UnzipLocation $toolsDir
