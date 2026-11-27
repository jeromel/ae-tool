# Décisions d'architecture

Chaque décision structurante de l'outil est consignée ici : le contexte, ce qui a été décidé, ce que cela entraîne,
et ce qui a été écarté. Une décision ne se modifie pas : on en écrit une nouvelle qui la remplace.

| N° | Décision | État |
|---|---|---|
| [001](001-metamodele-propre.md) | Un métamodèle propre à l'outil ; ArchiMate devient un format d'échange | Acceptée |
| [002](002-roles-fonctionnels.md) | Les cas d'usage se branchent sur des rôles fonctionnels, pas sur des types | Acceptée |
| [003](003-authentification-et-roles.md) | Comptes locaux ou Entra ID ; les habilitations sont gérées dans l'outil | Acceptée |
| [004](004-stockage-fichier.md) | Stockage par fichier pour le pilote ; base SQL différée | Remplacée par la 011 |
| [005](005-sources-externes.md) | Sources externes : un moteur de rapprochement, des adaptateurs en lecture seule | Acceptée |
| [006](006-aucune-dependance-tierce.md) | Aucune dépendance tierce côté serveur | Acceptée |
| [007](007-vues-par-cas-d-usage.md) | Des vues générées par cas d'usage plutôt que des dessins importés | Acceptée |
| [008](008-cadre-dotnet-suit-le-sdk.md) | Le cadre .NET visé suit le SDK de construction ; .NET 10 pour un serveur | Acceptée |
| [009](009-catalogue-donnees-flux.md) | Catalogue des données et des flux : deux niveaux de flux, sous-types par propriété, postes comptés par profil | Acceptée |
| [010](010-rapprochement-dessins-par-proprietes.md) | Rapprochement des dessins : les autres noms et l'identifiant de l'outil sont des propriétés | Acceptée |
| [011](011-stockage-fichier-generalisation.md) | Le stockage par fichier est conservé pour la généralisation ; remplace la 004 | Acceptée |
| [012](012-acces-reserve-a-des-personnes-nommees.md) | Données sensibles : des personnes nommées, un périmètre de lecture selon le besoin d'en connaître | Acceptée |
| [013](013-description-openapi-generee-sans-paquet.md) | La description OpenAPI est produite par l'outil à partir de ses routes, sans paquet ; cible : API publique de lecture | Acceptée |
| [014](014-maitre-juge-objet-par-objet.md) | Le maître se juge objet par objet ; un objet revenu d'Archi devient un objet d'Archi | Acceptée |
