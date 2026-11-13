# Construction complète sous Windows : interface, API, tests.
# Prérequis : SDK .NET 8 et Node.js 22.22.3 ou plus récent (voir README).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Push-Location "$root\frontend"
npm ci
if ($LASTEXITCODE) { throw "npm ci a échoué" }
npx ng build
if ($LASTEXITCODE) { throw "ng build a échoué" }
Pop-Location

$www = "$root\backend\src\AeTool.Api\wwwroot"
if (Test-Path $www) { Remove-Item $www -Recurse -Force }
Copy-Item "$root\frontend\dist\frontend\browser" $www -Recurse

Push-Location "$root\backend"
dotnet build src\AeTool.Api
if ($LASTEXITCODE) { throw "dotnet build a échoué" }
dotnet run --project tests\AeTool.Tests
if ($LASTEXITCODE) { throw "des tests échouent" }
Pop-Location

Write-Host "Construction terminée. Lancer : dotnet run --project backend\src\AeTool.Api"
