# 001 — Un métamodèle propre à l'outil ; ArchiMate devient un format d'échange

## Contexte

Dans le prototype, les types d'objets de l'outil étaient les concepts ArchiMate eux-mêmes : la clé d'un type était son nom
ArchiMate (`ApplicationComponent`) et tout le contenu venait d'un export Archi. C'était suffisant pour reprendre l'existant,
mais cela liait trois choses qui n'ont pas le même cycle de vie :

- le **vocabulaire de l'entreprise** (« application », « zone », « quartier », « équipement industriel ») ;
- la **notation** ArchiMate, qui n'a par exemple qu'un concept (`Grouping`) pour plusieurs notions d'urbanisation ;
- l'**outil de saisie** du moment (Archi).

## Décision

1. Le métamodèle de l'outil est la référence. Ses types d'objets, ses types de relations et ses propriétés ont leurs
   propres clés, leurs libellés, leurs couches et leurs règles (couples autorisés, type de donnée, valeurs permises,
   caractère obligatoire). Tout cela est une donnée, modifiable dans l'outil sans livraison.
2. La correspondance avec ArchiMate est une **matrice de transcodification** rangée dans le métamodèle :
   - une règle par couple (type de l'outil, concept ArchiMate), avec un discriminant facultatif (une propriété et sa valeur)
     quand plusieurs types de l'outil partagent un concept ;
   - une règle par couple (relation de l'outil, relation ArchiMate), contrainte ou non par les types des extrémités ;
   - un nom Archi par propriété.
3. Chaque objet importé garde son concept ArchiMate d'origine. L'export le réutilise : l'aller-retour reste sans perte,
   même quand plusieurs concepts sont regroupés sous un même type.
4. Un type sans équivalent ArchiMate est exporté avec un concept de repli et une propriété marqueur ; le réimport le retrouve.
5. Modifier la matrice ne retype pas les objets déjà importés : c'est une action explicite, « réappliquer la matrice »,
   précédée d'une simulation.

## Conséquences

- L'entreprise peut nommer ses concepts, en ajouter et en fusionner sans attendre une évolution d'ArchiMate ni de l'outil.
- Un objet peut être créé dans l'outil sans passer par Archi.
- D'autres formats d'échange (un autre outil de modélisation, une autre version d'ArchiMate) se brancheraient de la même
  façon : une matrice de plus.
- Les données d'avant cette décision sont migrées automatiquement (version de schéma 1 → 2), avec copie du fichier d'origine.
- Le métamodèle par défaut reste aligné sur ArchiMate (une correspondance simple par concept), pour que la reprise de
  l'existant ne demande aucun réglage.

## Écarté

- **Garder les concepts ArchiMate comme types** et n'ajouter que des libellés : simple, mais interdit de distinguer deux
  notions partageant un concept, et laisse la notation décider du vocabulaire.
- **Abandonner ArchiMate** : le patrimoine existe dans Archi, et la notation reste le langage commun des architectes.
