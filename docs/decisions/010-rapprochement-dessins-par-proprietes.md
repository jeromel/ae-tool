# 010 — Rapprochement des dessins : les autres noms et l'identifiant de l'outil sont des propriétés

**État : Acceptée** (25/11/2026). Réserve sur le point 2 levée le 25/11/2026. L'essai sur Archi réel a montré qu'une propriété est conservée à l'import « Another Model into Selected Model ». Archi ne permet pas de copier-coller un objet d'un modèle à l'autre : le trajet passe par l'import Open Exchange, l'enregistrement en `.archimate`, puis cette fusion.

## Contexte

Le besoin EV-05 demande de relier les formes des dessins draw.io des collègues aux objets du référentiel venus d'Archi.
Deux faits manquent au modèle :

- **les autres noms d'un objet** (« Ondine », « Facturation » pour ONDINE), pour qu'un dessin suivant soit rapproché seul ;
- **un identifiant qui survit à un passage par Archi** : un objet créé depuis un dessin, exporté vers Archi, puis copié dans le
  modèle maître de l'architecte, change d'identifiant Archi. L'import suivant ne le reconnaît pas et crée un doublon.

Par ailleurs, la mémoire d'un dessin est aujourd'hui indexée par le nom du fichier, qui change quand le collègue le renomme,
et la similarité de noms existe deux fois : une version privée au catalogue et une égalité stricte pour les dessins.

## Décision

1. **Les autres noms d'un objet sont la propriété `ae.alias`.** Les noms y sont séparés par `;` et le rapprochement
   les compare comme le nom. Ce n'est pas un champ du modèle : la propriété se stocke, s'exporte vers Archi et revient à
   l'import comme toute autre, et l'architecte peut la tenir dans Archi.
2. **L'export vers Archi écrit la propriété `ae.id`** (l'identifiant de l'outil) sur chaque objet. L'import Archi rapproche
   d'abord par identifiant Archi, puis par `ae.id`. Cette clé est déjà reconnue à l'import draw.io (`DrawioImporter`).
3. **La mémoire d'un dessin est indexée par l'identifiant de page draw.io** (`<diagram id>`), qui reste stable quand le
   fichier est renommé, et non par le nom du fichier.
4. **Une seule fonction de similarité de noms**, dans le Core. Elle sert au catalogue et aux dessins, et elle tient compte de
   `ae.alias`.

## Conséquences

- Pas de changement de stockage ni de format de fichier : la décision 004 n'est pas touchée.
- Une mise à niveau (`Upgrade.cs`) réécrit les clés de mémoire existantes, du nom de fichier vers l'identifiant de page.
- Les propriétés `ae.*` deviennent des propriétés réservées par l'outil. Le métamodèle les déclare, pour qu'elles ne soient
  pas confondues avec les propriétés métier importées.
- Le point 2 suppose qu'Archi conserve les propriétés d'un objet lors d'un copier-coller entre modèles. **Il faut le vérifier
  sur Archi réel avant de coder ce point.** Si ce n'est pas le cas, le point 2 est retiré et une nouvelle décision le remplace.

## Écarté

- **Un champ `Aliases` sur l'objet** : il imposerait une mise à niveau du stockage et un traitement à part dans l'export Archi,
  pour un résultat identique.
- **Un script jArchi de fusion des modèles** : un second langage et un second outil à maintenir, à installer chez chaque
  architecte.
- **Une similarité plus fine** (distance d'édition, phonétique) : « Portail » → « Mon Espace Eau » n'est trouvable par aucun
  calcul. Les autres noms règlent ce cas, pas un algorithme.
