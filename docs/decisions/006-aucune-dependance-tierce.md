# 006 — Aucune dépendance tierce côté serveur

## Contexte

La règle de l'organisation est « SaaS avant achat avant développement ». Développer cet outil est donc une dérogation, qui
n'est défendable que si le résultat reste maintenable par une petite équipe pendant des années. Le premier risque de
maintenance d'un développement est sa chaîne de dépendances : failles à corriger, versions incompatibles, bibliothèques
abandonnées.

## Décision

- **Serveur** : uniquement le cadre partagé .NET (bibliothèque de base et ASP.NET Core), livré et corrigé par Microsoft avec
  le runtime. Aucun paquet NuGet.
- **Interface** : uniquement Angular et ses outils de construction. Aucune bibliothèque de composants, de graphiques ou de
  diagrammes ; les appels HTTP passent par `fetch`.
- **Tests** : un outillage maison de quelques dizaines de lignes, sans cadre de test tiers.
- Toute exception est une **dérogation écrite** : une décision d'architecture qui nomme la dépendance, son éditeur, sa
  licence, le besoin qu'elle couvre et ce qu'il en coûterait de s'en passer.

## Conséquences

- La mise à jour de sécurité du serveur se réduit à celle du runtime .NET.
- Certaines fonctions sont écrites à la main : lecture des classeurs Excel, rendu des diagrammes en SVG, lecture des CSV,
  client API REST des sources, flux de connexion OAuth. Elles sont courtes, testées, et documentées dans le code.
- Deux manques assumés aujourd'hui : pas de base SQL (décision 004), pas de bibliothèque OpenID Connect (décision 003).
- Le registre des dépendances et la nomenclature logicielle (SBOM) ne portent que sur la chaîne de construction de l'interface.

## Écarté

- **Autoriser les paquets Microsoft** par principe : la frontière serait floue et la dérogation deviendrait la règle.
  Mieux vaut les traiter une par une.
