# 004 — Stockage par fichier pour le pilote ; base SQL différée

## Contexte

Le cahier des charges prévoit une base relationnelle. Le prototype range tout le référentiel dans un fichier JSON, relu au
démarrage et réécrit à chaque modification, derrière une interface de stockage (`IRepositoryStore`).
Deux faits pèsent sur la décision :

- un référentiel réel de taille moyenne tient en quelques mégaoctets (environ 1 800 objets, 1 650 relations, 130 vues) et sera utilisé par une
  poignée de contributeurs pendant le pilote ;
- aucun pilote d'accès SQL n'est disponible sans dépendance externe (voir décision 006), et aucun n'a pu être installé
  ni donc testé pendant le développement.

## Décision

Pour le pilote, le stockage reste un **dossier de fichiers**, durci :

- écriture atomique du référentiel (fichier temporaire puis remplacement) ;
- **journal des modifications dans un fichier séparé**, en ajout seul ;
- **sauvegardes automatiques** : une par jour d'activité, et une avant chaque opération risquée (import, migration,
  restauration), avec une durée de conservation ;
- comptes, sources externes et clés de chiffrement dans des fichiers distincts du même dossier : un seul dossier à
  sauvegarder et à protéger ;
- numéro de version de schéma dans le fichier et migration automatique à la lecture.

L'interface de stockage est conservée : une implémentation SQL s'y branchera sans toucher au cœur.

## Conséquences

- Installation sans serveur de base de données ; la réversibilité est immédiate (le fichier est lisible, exportable tel quel).
- **Une seule instance** de l'application peut écrire dans le dossier. Pas de montée en charge horizontale.
- Chaque écriture réécrit le fichier du référentiel : acceptable pour quelques utilisateurs, pas pour des dizaines
  d'écritures simultanées.
- Le contrôle d'accès aux données repose sur les droits du dossier : il doit être restreint au compte de service.

## Quand revoir cette décision

Avant l'un de ces événements, le premier atteint :

- plus d'une dizaine de contributeurs réguliers ;
- un fichier de référentiel au-delà de 50 Mo ;
- un besoin de haute disponibilité ou de plusieurs instances ;
- une exigence d'audit imposant une base gérée par l'exploitation.

Le passage à SQL demandera d'accepter une dépendance (le pilote d'accès aux données), à instruire comme dérogation à la
décision 006.

## Écarté

- **Livrer une implémentation SQL non testée** : écrire du code de persistance sans pouvoir l'exécuter n'est pas
  acceptable pour le composant qui porte les données.
