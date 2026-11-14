#!/usr/bin/env bash
# Lance l'outil sur ce poste. Les données sont rangées dans le dossier « donnees », à côté du code,
# et non dans le dossier de compilation : un nettoyage ou une recompilation ne les efface pas.
# Usage : ./lancer.sh [port]        CONSTRUIRE=1 ./lancer.sh pour forcer une construction complète.
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
port="${1:-5080}"
donnees="$root/donnees"
fichier="$donnees/referentiel.json"
mkdir -p "$donnees"

# Reprise des données d'une version antérieure, qui les rangeait dans le dossier de compilation.
if [ ! -f "$fichier" ]; then
  ancien="$(find "$root/backend/src/AeTool.Api/bin" -name referentiel.json -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2-)"
  if [ -n "$ancien" ]; then cp "$ancien" "$fichier"; echo "Données reprises de $ancien"; fi
fi

if [ "${CONSTRUIRE:-0}" = "1" ] || [ ! -f "$root/backend/src/AeTool.Api/wwwroot/index.html" ]; then "$root/build.sh"; fi

echo "Données : $donnees"
echo "Adresse : http://localhost:$port  (Ctrl+C pour arrêter)"
exec dotnet run -c Release --project "$root/backend/src/AeTool.Api" -- --urls "http://localhost:$port" --DataPath "$fichier"
