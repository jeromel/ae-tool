#!/usr/bin/env bash
# Prépare le dossier à déposer sur un serveur : API compilée en Release, interface comprise.
# Le résultat (livraison/ae-tool et livraison/ae-tool-<version>.tar.gz) ne contient aucune donnée.
# Prérequis sur le serveur : le runtime ASP.NET Core de la version majeure du SDK utilisé ici. Voir docs/installation.md.
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"

"$root/build.sh"

# Failles connues des paquets livrés avec l'interface : une faille critique bloque la livraison (exigence ET-23).
(cd "$root/frontend" && npm audit --omit=dev --audit-level=critical)

sortie="$root/livraison/ae-tool"
rm -rf "$sortie"
dotnet publish "$root/backend/src/AeTool.Api" -c Release -o "$sortie"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/backend/Directory.Build.props")"
tar -czf "$root/livraison/ae-tool-$version.tar.gz" -C "$root/livraison" ae-tool
echo "Livraison prête : $root/livraison/ae-tool-$version.tar.gz"
