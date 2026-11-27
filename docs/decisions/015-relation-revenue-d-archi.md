# 015 — Une relation revenue d'Archi devient une relation d'Archi ; nom et description au compte rendu du premier retour

**État : Acceptée** (27/11/2026).

Complète la [014](014-maitre-juge-objet-par-objet.md), qui ne traite que des objets. Elle ne la remplace pas.

## Contexte

L'export B « Pas encore dans Archi » (`shared/notes/note-faisabilite-export-b-emmett.md` §2) a besoin de savoir si une
relation est déjà dans Archi. Pour les objets, la 014 y répond : un objet revenu d'Archi passe à `Source = "archi"`.
Rien de tel n'existe pour les relations.

Constats sur le code (0.15.20) :

- **Une relation ne change jamais de source au réimport.** `ArchimateExchange` écrit
  `Source = existing?.Source ?? SourceTag`. La règle vient de la 0.15.9 : elle évitait d'archiver un flux du catalogue
  qui ne figurait pas dans le fichier Archi, à une époque où ces flux n'étaient jamais exportés.
- Il en découle trois défauts, les mêmes que ceux que la 014 a corrigés pour les objets :
  - une relation créée ici, exportée puis supprimée dans Archi, **n'est pas archivée** (question 1 de
    `note-livraison-0.15.17-emmett`) ;
  - l'export B ne peut pas l'écarter : elle **repartirait à chaque export**, et la fusion réécrirait son nom et ses
    propriétés dans Archi ;
  - les objets et les relations du catalogue suivent **deux règles différentes** : un objet du catalogue change de source
    au retour d'Archi (014), une relation du catalogue non.
- **La source n'est pas ce qui protège les données du catalogue.** Les propriétés tenues par le catalogue sont gardées au
  réimport par `CatalogueModel.IsOwned`, quelle que soit la source. `CatalogueImporter` retrouve ses relations par
  extrémités et par type, sans regarder la source. Il ne la lit que pour poser un type d'accès encore vide, cas qu'un
  retour d'Archi ne laisse pas.
- **Le nom et la description d'un objet qui revient ne figurent pas au compte rendu** (question 2 de la même note).
  Quelqu'un qui a renommé ici un objet déjà exporté voit le nouveau nom écrasé au réimport, sans aucune ligne. Le point 3
  de la 014 ne liste que les propriétés. Il vise pourtant « toute valeur saisie ici » au premier retour.

## Décision

1. **Une relation revenue d'Archi devient une relation d'Archi.** Au réimport, une relation déjà présente dont
   l'identifiant figure dans le fichier prend `Source = "archi"`, **quelle que soit sa source d'origine, catalogue
   compris**. À partir de là, elle suit la règle des relations d'Archi : Archi fait foi sur son nom, sa description et
   ses propriétés déclarées par Archi, et elle est archivée si elle manque au fichier suivant.
2. **Les propriétés tenues par le catalogue restent au catalogue**, comme pour les objets (014, point 1) : `IsOwned` les
   garde au réimport quand Archi ne les porte pas. Rien ne change sur ce point.
3. **« Déjà dans Archi » se lit de la même façon pour les objets et les relations : `Source = "archi"`.** L'export B
   n'emploie aucun autre critère.
4. **Au premier retour d'un objet, le compte rendu liste aussi le nom et la description** quand le fichier les change.
   Une ligne pour chacun, au même format que les propriétés : l'objet, le champ, l'ancienne valeur et la nouvelle, ou
   « effacée ». Aux retours suivants, le nom et la description sont verrouillés ici (014, point 2). Un changement à ce
   moment-là vient d'Archi lui-même et n'est pas listé.

## Conséquences

- `ArchimateExchange`, import des relations : la source passe à `archi` pour toute relation trouvée dans le fichier. Ce
  changement n'est pas compté comme une modification, comme pour les objets. Le commentaire de la 0.15.9 est supprimé.
- `ArchimateExchange.ReportOverwrites` (ou la fonction voisine) : le nom et la description entrent dans le compte rendu
  quand l'objet revient pour la première fois.
- `CatalogueImporter`, ligne qui pose le type d'accès : on vérifie qu'aucune autre lecture de `Source == "catalogue"` ne
  porte sur une relation. Aujourd'hui, il n'y en a pas.
- Le fichier stocké ne change pas de schéma. Il n'y a ni migration ni champ nouveau.
- Tests à écrire :
  - aller-retour d'une relation créée ici : elle change de source, puis elle est archivée si elle manque au fichier
    suivant ;
  - aller-retour d'un flux du catalogue : il change de source et garde son code et ses caractéristiques ;
  - réimport du catalogue après ce changement : le flux est retrouvé, il n'y a pas de doublon ;
  - export B après réimport : la liste des relations est vide ;
  - objet renommé ici puis revenu d'Archi : une ligne « nom » au compte rendu.
- **Risque résiduel accepté** : si un flux du catalogue est supprimé dans Archi, il est archivé ici. Le réimport suivant
  du catalogue le recrée alors sous un nouvel identifiant, et il reparaît dans la liste B. Le désaccord entre Archi et
  le catalogue se voit, au lieu d'être masqué. C'est à l'architecte de trancher, dans Archi ou dans le catalogue.
- Coût ≈ 20 à 35 k tokens : la règle 1 ≈ 15 à 25 k, le point 4 ≈ 5 à 10 k. C'est à faire avant le code de l'export B
  ou avec lui.

## Écarté

- **(a) d'Emmett, la règle de la 014 sans les flux du catalogue.** Les flux du catalogue repartiraient à chaque export
  B, et la liste ne se viderait jamais. Le motif, « le catalogue est maître de ses flux », ne tient pas : le catalogue
  n'est maître que de ses propriétés, qu'`IsOwned` protège déjà. Il n'archive pas les flux et ne retrouve pas une
  relation par sa source.
- **(b) d'Emmett, mémoriser « vu au dernier import Archi le … » sur chaque objet et chaque relation.** Cela ajoute un
  champ au fichier stocké, une migration et une ADR de stockage, pour une information que la source porte déjà. Ce
  serait le double du coût. Cette mémoire reste la suite prévue par la 014 si les écrasements répétés d'une même
  propriété l'exigent un jour. Il n'y a pas lieu de l'anticiper.
- **Retenir comme critère un concept ArchiMate renseigné sur la relation.** Cette valeur n'est pas fiable : l'import
  draw.io la pose sur des éléments qui n'ont jamais été dans Archi, et le réimport la remet à vide sur une relation
  inchangée.
