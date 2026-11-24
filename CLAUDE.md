# Consignes de développement

## Neutralité du dépôt

L'outil est agnostique du contexte de son utilisateur. Le dépôt — code, tests, documentation, exemples, messages de
commit — ne cite aucune organisation réelle, ni ses sites, ses projets, ses outils internes ou ses prestataires.

- Pour tout exemple ou contexte, utiliser l'entreprise fictive **Hydréane** (`exemples/hydreane`).
- Un connecteur vers un produit du marché se décrit de façon générique (« CMDB par API REST », « outil de gestion de
  projet »), sans nom d'éditeur.
- Aucun jeu de données réel ou anonymisé, aucune table de correspondance n'entre dans le dépôt.
- Le test `NeutralityTests` parcourt les fichiers suivis et échoue si un terme interdit apparaît. Il ne connaît que
  leurs empreintes : la liste en clair est tenue hors du dépôt.

## Habitudes

- Interface et documentation en français ; texte au service de l'utilisateur, sans jargon.
- Aucune dépendance tierce côté serveur (décision 006).
- Chaque évolution : tests, documentation (`docs/`), recette dans le navigateur sur le jeu Hydréane.
