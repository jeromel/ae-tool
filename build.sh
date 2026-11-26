#!/usr/bin/env bash
# Construit l'interface, la place dans l'API, compile et lance les tests. Aucun accès réseau requis côté .NET.
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# Tests des modules de calcul du dessin (tracé, disposition, noms), hors navigateur.
(cd frontend && npm ci --no-audit --no-fund && npm run --silent test:unit && npx ng build)
rm -rf backend/src/AeTool.Api/wwwroot
mkdir -p backend/src/AeTool.Api/wwwroot
cp -r frontend/dist/frontend/browser/. backend/src/AeTool.Api/wwwroot/
# Témoin de version : lancer.sh reconstruit l'interface quand il ne correspond plus à la version du code.
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' backend/Directory.Build.props)"
printf '%s\n' "$version" > backend/src/AeTool.Api/wwwroot/version.txt
# Le cadre cible suit le SDK retenu (voir backend/Cadre.props) : SDK 8 donne net8.0, SDK 10 donne net10.0, etc.
sdk="$(dotnet --version)"
echo "SDK .NET utilisé : $sdk"
case "$sdk" in 8.*|10.*) ;; *) echo "Ce SDK n'a pas été validé avec l'outil : les tests qui suivent font foi. Pour revenir au SDK 8 : dans global.json, remplacer latestMajor par latestFeature." ;; esac
dotnet build backend/src/AeTool.Api -c Release
dotnet run --project backend/tests/AeTool.Tests -c Release
echo "Lancer : dotnet run --project backend/src/AeTool.Api -c Release --urls http://localhost:5080"
