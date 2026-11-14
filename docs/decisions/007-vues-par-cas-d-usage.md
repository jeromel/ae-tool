# 007 — Des vues générées par cas d'usage plutôt que des dessins importés

## Contexte

Les 132 vues importées d'Archi sont des dessins faits à la main. Elles sont chargées, organisées selon les dossiers de leur
auteur, et une partie de la connaissance n'existe que dans leur géométrie : le rattachement d'une application à une
capacité est le plus souvent une boîte posée dans une autre, pas une relation. L'outil ne pouvait donc que rejouer ces
dessins.

## Décision

1. **L'entrée se fait par une question**, pas par un dossier. Le menu suit l'intention (comprendre, décider, tenir à
   jour) ; chaque espace propose des questions prêtes à l'emploi qui règlent filtre, tri et coloration.
2. **Les vues des espaces sont générées depuis le graphe** (objets et relations), bornées en taille, avec des tiroirs que
   le lecteur ouvre à la demande. Elles ne dépendent d'aucun dessin.
3. **Un fait se porte par une relation.** Tant qu'un rattachement n'existe que par imbrication dans une vue, l'outil le
   signale comme « déduit d'une vue » ; l'objectif est de le transformer en relation.
4. **Une question à laquelle le référentiel ne sait pas répondre reste visible** et dit ce qu'il faut renseigner.
5. Les vues Archi restent consultables, avec un rendu retravaillé, comme source et comme archive.

## Conséquences

- La qualité des vues dépend de la qualité des relations et des propriétés : la page de qualité du référentiel devient un
  outil de pilotage, pas un contrôle de forme.
- La charte de modélisation change : le livrable de l'architecte est le graphe, les dessins en sont des projections.
- Le catalogue de questions est un actif d'architecture au sens des points de vue de TOGAF (une partie prenante, une
  préoccupation, une vue).

## Limites connues

- Hors la carte des capacités, les espaces sont encore des listes : la vue de contexte d'une application et la chaîne
  technique restent à générer.
- La revue assistée qui transforme les imbrications en relations reste à faire.
