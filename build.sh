#!/usr/bin/env bash
# Construit l'interface, la place dans l'API, compile et lance les tests. Aucun accès réseau requis côté .NET.
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
(cd frontend && npm ci --no-audit --no-fund && npx ng build)
rm -rf backend/src/AeTool.Api/wwwroot
mkdir -p backend/src/AeTool.Api/wwwroot
cp -r frontend/dist/frontend/browser/. backend/src/AeTool.Api/wwwroot/
dotnet build backend/src/AeTool.Api -c Release
dotnet run --project backend/tests/AeTool.Tests -c Release
echo "Lancer : dotnet run --project backend/src/AeTool.Api -c Release --urls http://localhost:5080"
