# 009 — Catalogue des données et des flux : deux niveaux de flux, des sous-types par propriété, des postes comptés par profil

## Contexte

La question « où se trouvent mes factures : sur quels serveurs, quels postes, par quelles applications et quels flux
techniques passent-elles ? » demande trois choses que le référentiel n'avait pas :

- un **catalogue des données** tenu par les métiers : système maître, relais, consommateurs, data owner, data steward,
  sensibilité, données personnelles, attributs ;
- des **flux à deux niveaux** : le flux applicatif (logique : quelle donnée va de quelle application à quelle autre) et
  les flux techniques qui le réalisent (extraction, transformation, transport, chargement), reliés par une table de traçabilité ;
- les **lieux de stockage** que le modèle Archi décrit mal : partages de fichiers, postes de travail.

## Décision

1. **Le flux applicatif reste une relation de flux** entre deux applications, compatible avec Archi et avec l'espace Flux.
   Il porte son code (`asset.code`) et la donnée transportée, reliée à la relation comme ArchiMate l'autorise.
2. **Le flux technique est un objet**, de type « Flux technique » (rôle `flux-technique`), exporté vers Archi en processus
   technologique. Il est relié à l'application qu'il concerne ; une association vers le flux applicatif porte l'ordre
   d'exécution (`trace.order`) et le statut du lien.
3. **Un sous-type se dit par une propriété Archi, `asset.subtype`**, que les règles de transcodification lisent :
   un Node avec « Partage de fichiers » devient un partage, un Device avec « Profil de poste » devient un profil de poste.
   Les spécialisations d'Archi ne passent pas le format d'échange : la propriété les remplace. L'export la réécrit, l'aller-retour
   ne perd rien. Les nœuds nommés « Partage… » sans sous-type sont signalés et se reclassent sur demande.
4. **Les postes de travail ne deviennent pas des objets.** Un profil de poste (PC bureautique, PC métier…) reçoit un code
   `PP-nnn` attribué par l'outil ; la CMDB le porte dans un champ libre (champ pivot). Le comptage range chaque poste dans
   son profil et ne garde que des effectifs par site, entité et modèle, plus la liste des postes à rattacher. Aucun nom
   d'utilisateur n'entre dans le référentiel ; les logiciels installés non plus (ils relèvent de la gestion des actifs logiciels).
5. **Le catalogue s'importe en deux temps**, comme un dessin draw.io : l'analyse rapproche chaque ligne par son code, puis
   par son nom, sans rien écrire ; l'import applique les choix. Une relation existante ne reçoit qu'un flux du catalogue.
   Les propriétés tenues par le catalogue survivent à un réimport Archi qui ne les porte pas.
6. **Le parcours se calcule** à la demande depuis le graphe (données, applications, flux, étapes, hébergement, profils) ;
   ce qu'il ne sait pas dire est listé, jamais comblé. Il suit l'état du SI regardé (existant ou étape d'un scénario).

## Conséquences

- Les attributs d'un objet de données n'ont pas d'équivalent ArchiMate : ils restent dans l'outil et ne partent pas vers Archi.
- Le statut de l'information (`info.status` : constaté, à confirmer, à définir, proposé) est affiché partout en pointillé
  et sert au taux de fiabilité de la page Qualité.
- Un destinataire hors de l'onglet Applications (« EXTERNE ») devient un acteur, lié ou créé à l'import.

## Écarté

- **Le flux applicatif comme objet** : il aurait cassé l'espace Flux, les vues Archi existantes et la matrice émetteurs × récepteurs.
- **Les postes un par un dans le référentiel** : des milliers d'objets qui changent chaque semaine, sans valeur pour la cartographie.
- **Reconnaître un partage à son nom** : fragile ; la propriété est explicite et survit à un renommage.
