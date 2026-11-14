# Prépare le dossier à déposer sur un serveur : API compilée en Release, interface comprise.
# Le résultat (livraison\ae-tool et livraison\ae-tool-<version>.zip) ne contient aucune donnée.
# Prérequis sur le serveur : le runtime ASP.NET Core de la version majeure du SDK utilisé ici (le « Hosting Bundle » pour IIS). Voir docs\installation.md.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

& "$root\build.ps1"

$sortie = "$root\livraison\ae-tool"
if (Test-Path $sortie) { Remove-Item $sortie -Recurse -Force }
dotnet publish "$root\backend\src\AeTool.Api" -c Release -o $sortie
if ($LASTEXITCODE) { throw "dotnet publish a échoué" }

$version = [regex]::Match((Get-Content "$root\backend\Directory.Build.props" -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
$archive = "$root\livraison\ae-tool-$version.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }
Compress-Archive -Path "$sortie\*" -DestinationPath $archive
Write-Host "Livraison prête : $archive"
