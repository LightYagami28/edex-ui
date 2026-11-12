# SPDX-License-Identifier: GPL-3.0-only
# Costruisce gli standalone Windows (win-x64, win-arm64), calcola le somme SHA-256 e genera i manifest dei gestori di pacchetti in packaging/dist.
# Uso: pwsh packaging/build.ps1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$repo = 'LightYagami28/edex-ui'
$version = ([xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))).SelectSingleNode('//Version').InnerText
$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $dist 'stage'
# Pulisce solo i propri artefatti: build.sh scrive nella stessa cartella.
foreach ($old in $stage, (Join-Path $dist 'windows')) { if (Test-Path $old) { Remove-Item -Recurse -Force $old } }
Get-ChildItem $dist -Filter 'edex-ui-*-win-*' -ErrorAction SilentlyContinue | Remove-Item -Force
New-Item -ItemType Directory -Force $stage | Out-Null

$tokens = @{ VERSION = $version; URL_BASE = "https://github.com/$repo/releases/download/v$version" }
foreach ($rid in 'win-x64', 'win-arm64') {
    $name = "edex-ui-$version-$rid"
    $dir = Join-Path $stage $name
    dotnet publish (Join-Path $root 'src/Cli/Edex.Cli.csproj') -c Release -r $rid --self-contained true "-p:Version=$version" -o $dir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallito per $rid" }
    Copy-Item (Join-Path $root 'LICENSE') $dir
    $zip = Join-Path $dist "$name.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($dir, $zip, 'Optimal', $true)
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content -Encoding ascii "$zip.sha256"
    $tokens["SHA256_$($rid.ToUpper().Replace('-', '_'))"] = $hash
}
Remove-Item -Recurse -Force $stage

$utf8 = New-Object Text.UTF8Encoding $false
foreach ($fmt in Get-ChildItem (Join-Path $PSScriptRoot 'windows') -Directory) {
    foreach ($f in Get-ChildItem $fmt.FullName -Recurse -File | Where-Object Name -NE 'README.md') {
        $rel = $f.FullName.Substring($PSScriptRoot.Length + 1)
        $out = Join-Path $dist $rel
        New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
        $text = [IO.File]::ReadAllText($f.FullName)
        foreach ($k in $tokens.Keys) { $text = $text.Replace("@${k}@", $tokens[$k]) }
        [IO.File]::WriteAllText($out, $text, $utf8)
    }
}

# Fallisce se un segnaposto non è stato sostituito.
$left = Get-ChildItem (Join-Path $dist 'windows') -Recurse -File | Select-String -Pattern '@[A-Z][A-Z0-9_]*@'
if ($left) { $left | ForEach-Object { Write-Error "Segnaposto non sostituito: $($_.Path):$($_.LineNumber) $($_.Matches.Value)" }; throw 'Segnaposto rimasti' }
Write-Host "Pronto in $dist (versione $version)"
