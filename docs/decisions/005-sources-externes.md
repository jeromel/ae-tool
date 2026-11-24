# 005 — Sources externes : un moteur de rapprochement, des adaptateurs en lecture seule

## Contexte

Le référentiel d'architecture ne doit pas ressaisir ce que d'autres outils maîtrisent. La CMDB fait foi pour les
éléments de configuration ; l'outil de gestion de projet fait foi pour les projets. Le lot 2 doit relier
le référentiel à ces sources. Elles n'offrent pas toutes une API utilisable (certaines n'offrent que des
exports Excel), et aucune n'était joignable pendant le développement.

## Décision

1. **Un moteur de rapprochement unique, indépendant de la source.** Il reçoit des lignes (colonne → valeur) et un profil
   de source (quelle colonne porte l'identifiant, le nom, la nature ; quelles colonnes reprendre et vers quelles
   propriétés ; vers quels types du métamodèle). Il compare, propose, puis applique les décisions de l'utilisateur.
2. **Des adaptateurs minces** qui ne font que produire des lignes : fichier CSV ou Excel, API REST générique (adresse, pagination et noms de champs réglés par profil).
   Un nouvel outil se branche en écrivant un adaptateur, sans toucher au moteur ni à l'écran.
3. **Lecture seule vers la source.** L'outil ne modifie jamais la CMDB ni l'outil de projet.
4. **Deux temps** : calculer le rapprochement ne modifie rien ; l'application des décisions (lier, créer, ignorer) est
   explicite. Un objet créé depuis une source entre dans le circuit de validation à l'état « proposé ».
5. **Le lien est porté par l'objet** : identifiant de l'objet chez la source, par source. Les attributs repris sont des
   propriétés ordinaires, marquées par leur source.
6. **Les secrets** (mot de passe, jeton) sont chiffrés au repos par le mécanisme de protection des données du cadre .NET
   et ne sont jamais renvoyés par l'API.
7. Un profil peut déclarer que **la source fait foi** : le nom de l'objet lié est alors aligné sur elle.

## Conséquences

- Le rapprochement est utilisable dès maintenant avec un simple export, sans attendre l'ouverture d'un accès API.
- La transcodification entre les catégories de la source et les types du métamodèle est une donnée du profil : c'est la
  même logique que la matrice ArchiMate (décision 001).
- L'adaptateur API REST est générique : l'adresse, la pagination (taille de page, décalage, lien de page suivante) et
  les champs qui portent les éléments, le total et l'identifiant se règlent par profil. Il est testé sur des réponses
  simulées. Les noms de champs dépendent de l'API : l'aperçu montre les colonnes réellement reçues, et le mappage se règle dessus.

## Limites connues

- Pas de synchronisation planifiée dans cette première étape : le rapprochement est lancé à la demande.
- Pas d'adaptateur par API pour l'outil de gestion de projet ; l'export fichier le couvre en attendant.
- Les relations entre éléments de la CMDB (liens entre CI) ne sont pas encore reprises.
