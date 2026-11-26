# 011 — Le stockage par fichier est conservé pour la généralisation ; la base relationnelle reste différée

**État : Acceptée** (26/11/2026). Remplace la décision [004](004-stockage-fichier.md).

## Contexte

La décision 004 a retenu un dossier de fichiers pour le pilote et demandait de la revoir avant la généralisation, au premier
de quatre seuils atteint : plus d'une dizaine de contributeurs réguliers, un référentiel au-delà de 50 Mo, un besoin de
haute disponibilité ou de plusieurs instances, une exigence d'audit imposant une base gérée par l'exploitation.

Faits établis le 26/11/2026 :

- la généralisation visée reste **sous dix contributeurs réguliers** (l'équipe d'architecture et quelques référents) ;
- **l'exploitation accepte un dossier** sauvegardé par l'infrastructure ; elle n'impose ni base gérée, ni haute disponibilité ;
- le référentiel de taille moyenne occupe environ 6 Mo, loin des 50 Mo ;
- le nombre de lecteurs ne pèse pas sur le stockage : le référentiel est lu en mémoire, seul le rythme des écritures
  compte.

Aucun seuil n'est atteint.

## Décision

1. **Le stockage par fichier de la décision 004 est conservé pour la généralisation**, avec ses mesures de durcissement
   (écriture atomique, journal en ajout seul, sauvegardes automatiques, version de schéma et migration à la lecture).
2. **La dérogation à ET-04 (base relationnelle) est maintenue.** Elle n'est plus limitée au pilote : elle tient tant
   qu'aucun seuil n'est atteint.
3. **Les seuils de révision restent ceux de la décision 004.** Leur suivi revient à l'administrateur de l'outil, à
   chaque nouvelle population de contributeurs et à chaque palier de taille du fichier.
4. **Le jour où un seuil est atteint, la première marche est un stockage du même document dans une base gérée**, derrière
   `IRepositoryStore`, et non un schéma relationnel. L'interface charge et enregistre le référentiel en entier : une table
   qui porte le document et une table pour le journal s'y branchent sans toucher au Core.

## Conséquences

- Rien à coder. Aucune dépendance nouvelle.
- Les conséquences de la décision 004 demeurent : une seule instance écrit dans le dossier, chaque écriture réécrit le
  fichier, le contrôle d'accès aux données repose sur les droits du dossier.
- Le jour où le point 4 s'applique, compter :
  - document dans une base gérée : environ 2 à 3 jours, tests compris, plus une dérogation à la décision 006 pour le
    pilote d'accès à la base ;
  - schéma relationnel : le Core travaille sur un référentiel entier en mémoire ; il faudrait écrire chaque modification
    ligne à ligne, ce qui touche toutes les écritures. Plusieurs semaines, et une surface de défauts sur le composant qui
    porte les données.
- La couverture du cahier des charges (ET-04) passe de « dérogation pour le pilote » à « dérogation maintenue, seuils
  suivis ».

## Écarté

- **Passer à une base relationnelle pour la généralisation** : aucun seuil ne l'exige, et le coût est hors de proportion
  avec le besoin. Ce serait aussi livrer un code de persistance que personne n'a pu essayer sur la base cible (décision 004,
  « Écarté »).
- **Le document dans une base gérée dès maintenant** : il n'apporte rien tant que l'exploitation accepte un dossier, et
  il ajoute une dépendance et un serveur à faire fonctionner.
- **Relever les seuils** : rien ne le justifie. Ils sont prudents, ils ne sont pas atteints, ils restent.
