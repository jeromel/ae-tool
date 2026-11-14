# Lance l'outil sur ce poste. Les données sont rangées dans le dossier « donnees », à côté du code,
# et non dans le dossier de compilation : un nettoyage ou une recompilation ne les efface pas.
# Usage : .\lancer.ps1              (construit si nécessaire, puis lance sur http://localhost:5080)
#         .\lancer.ps1 -Construire  (force une construction complète avant de lancer)
#         .\lancer.ps1 -Port 5090
param([int]$Port = 5080, [switch]$Construire)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$donnees = Join-Path $root 'donnees'
$fichier = Join-Path $donnees 'referentiel.json'

if (-not (Test-Path $donnees)) { New-Item -ItemType Directory $donnees | Out-Null }

# Reprise des données d'une version antérieure, qui les rangeait dans le dossier de compilation.
if (-not (Test-Path $fichier)) {
    $anciens = Get-ChildItem "$root\backend\src\AeTool.Api\bin" -Recurse -Filter referentiel.json -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    if ($anciens) {
        Copy-Item $anciens[0].FullName $fichier
        Write-Host "Données reprises de $($anciens[0].FullName)"
    }
}

# L'interface compilée porte un témoin de version : absente ou d'une autre version que le code, elle est reconstruite.
$version = [regex]::Match((Get-Content "$root\backend\Directory.Build.props" -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
$temoin = "$root\backend\src\AeTool.Api\wwwroot\version.txt"
$aJour = (Test-Path $temoin) -and ("$(Get-Content $temoin -Raw)".Trim() -eq $version)
if ($Construire -or -not $aJour) { & "$root\build.ps1" }

Write-Host "Données : $donnees"
Write-Host "Adresse : http://localhost:$Port  (Ctrl+C pour arrêter)"
dotnet run --project "$root\backend\src\AeTool.Api" -- --urls "http://localhost:$Port" --DataPath $fichier
