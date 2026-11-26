# Construction complète sous Windows : interface, API, tests.
# Prérequis : un SDK .NET (8 ou plus récent ; 10 recommandé) et Node.js 22.22.3 ou plus récent (voir README).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Push-Location "$root\frontend"
npm ci
if ($LASTEXITCODE) { throw "npm ci a échoué" }
# Tests des modules de calcul du dessin (tracé, disposition, noms), hors navigateur.
npm run --silent test:unit
if ($LASTEXITCODE) { throw "des tests de l'interface échouent" }
npx ng build
if ($LASTEXITCODE) { throw "ng build a échoué" }
Pop-Location

$www = "$root\backend\src\AeTool.Api\wwwroot"
if (Test-Path $www) { Remove-Item $www -Recurse -Force }
Copy-Item "$root\frontend\dist\frontend\browser" $www -Recurse
# Témoin de version : lancer.ps1 reconstruit l'interface quand il ne correspond plus à la version du code.
$version = [regex]::Match((Get-Content "$root\backend\Directory.Build.props" -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
Set-Content -Path "$www\version.txt" -Value $version -Encoding ascii

Push-Location "$root\backend"
# Le cadre cible suit le SDK retenu (voir backend\Cadre.props) : SDK 8 donne net8.0, SDK 10 donne net10.0, etc.
$sdk = dotnet --version
Write-Host "SDK .NET utilisé : $sdk"
if ($sdk -notmatch '^(8|10)\.') {
    Write-Host "Ce SDK n'a pas été validé avec l'outil : les tests qui suivent font foi." -ForegroundColor Yellow
    Write-Host "En cas d'échec, pour revenir au SDK 8 : dans global.json, remplacer latestMajor par latestFeature." -ForegroundColor Yellow
}
dotnet build src\AeTool.Api
if ($LASTEXITCODE) { throw "dotnet build a échoué" }
dotnet run --project tests\AeTool.Tests
if ($LASTEXITCODE) { throw "des tests échouent" }
Pop-Location

Write-Host "Construction terminée (version $version). Lancer : .\lancer.ps1"
