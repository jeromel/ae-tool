# Décisions d'architecture

Chaque décision structurante de l'outil est consignée ici : le contexte, ce qui a été décidé, ce que cela entraîne,
et ce qui a été écarté. Une décision ne se modifie pas : on en écrit une nouvelle qui la remplace.

| N° | Décision | État |
|---|---|---|
| [001](001-metamodele-propre.md) | Un métamodèle propre à l'outil ; ArchiMate devient un format d'échange | Acceptée |
| [002](002-roles-fonctionnels.md) | Les cas d'usage se branchent sur des rôles fonctionnels, pas sur des types | Acceptée |
| [003](003-authentification-et-roles.md) | Comptes locaux ou Entra ID ; les habilitations sont gérées dans l'outil | Acceptée |
| [004](004-stockage-fichier.md) | Stockage par fichier pour le pilote ; base SQL différée | Acceptée, à revoir avant généralisation |
| [005](005-sources-externes.md) | Sources externes : un moteur de rapprochement, des adaptateurs en lecture seule | Acceptée |
| [006](006-aucune-dependance-tierce.md) | Aucune dépendance tierce côté serveur | Acceptée |
| [007](007-vues-par-cas-d-usage.md) | Des vues générées par cas d'usage plutôt que des dessins importés | Acceptée |
| [008](008-cadre-dotnet-suit-le-sdk.md) | Le cadre .NET visé suit le SDK de construction ; .NET 10 pour un serveur | Acceptée |
