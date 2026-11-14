# 002 — Les cas d'usage se branchent sur des rôles fonctionnels, pas sur des types

## Contexte

Les écrans de l'outil répondent à des questions (« quelles applications soutiennent cette capacité ? »). Pour le faire, le
code doit savoir ce qu'est une application, une capacité, un projet, un flux. Tant que les types étaient ceux d'ArchiMate,
le code testait des noms de types. Avec un métamodèle paramétrable (décision 001), ces noms ne sont plus stables : un
administrateur peut renommer un type, en créer un second pour les applications industrielles, ou en fusionner deux.

## Décision

Un type porte zéro, un ou plusieurs **rôles fonctionnels**. Le code ne connaît que les rôles.

| Rôle | Porté par | Ce qu'il active |
|---|---|---|
| application | types d'objets | Espace Applications, portefeuille, reprise du classeur d'urbanisation |
| capacité | types d'objets | Carte des capacités, rattachement des applications |
| projet | types d'objets | Projets, impacts, adhérences |
| donnée | types d'objets | Espace Données |
| support de donnée | types d'objets | Artefacts qui matérialisent une donnée |
| infrastructure | types d'objets | Espace Infrastructure, socle technique d'une application |
| fond de carte | types d'objets | Présent dans une vue projet, l'objet ne vaut pas impact |
| flux | types de relations | Espace Flux, pastilles sur les vues, matrice émetteurs × récepteurs |
| décomposition | types de relations | Arbre des capacités |

La liste des rôles est fixée par le code (chaque rôle correspond à une fonction de l'outil) ; leur affectation aux types
est une donnée du métamodèle.

## Conséquences

- Un nouveau contexte (une autre entreprise, un autre vocabulaire) se règle en affectant les rôles, sans toucher au code.
- Deux types peuvent partager un rôle : « application de gestion » et « application industrielle » apparaissent ensemble
  dans l'espace Applications.
- Ajouter un cas d'usage qui a besoin d'une notion nouvelle (par exemple « site ») demande d'ajouter un rôle : c'est le
  seul couplage restant entre le code et le métamodèle, et il est explicite.

## Écarté

- **Des clés de types dans les réglages** (liste des types « application », clé du type « projet ») : c'était l'état
  précédent ; chaque nouvelle fonction ajoutait son réglage et rien ne le signalait à l'administrateur.
- **S'appuyer sur les couches** : une couche est un classement de présentation ; un artefact et un serveur sont dans la même
  couche sans jouer le même rôle.
