# 014 — Le maître se juge objet par objet ; un objet revenu d'Archi devient un objet d'Archi

**État : Acceptée** (27/11/2026).

## Contexte

La règle « une donnée a un seul maître » est appliquée par `Mastery` (`AeTool.Core/Mastery.cs`) à la saisie, et par
l'import Open Exchange (`ArchimateExchange.cs`) au réimport. Le PO a arbitré deux points le 27/11/2026
(`shared/notes/note-ev05-proprietes-dessin3-haussman.md` §1 et §2) :

- **quand Archi porte une propriété sous le même nom qu'une propriété saisie dans l'outil, c'est Archi qui gagne, et
  on prévient** : le compte rendu d'import liste chaque écrasement ;
- **le maître se juge objet par objet** : un objet créé dans l'outil n'a pas de valeur dans Archi, donc l'outil est le
  maître de toutes ses propriétés, y compris celles de la méthode déclarées par l'import.

Constats sur le code :

- **le maître se juge aujourd'hui propriété par propriété.** Une propriété déclarée par l'import Archi
  (`PropertyDefinition.Source = "archi"`) est refusée à la saisie sur **tout** objet (`Mastery.MasterOfProperty`), y compris
  sur un objet créé dans l'outil. Un contributeur ne peut donc pas renseigner `lifecycle.state` sur l'application qu'il
  vient de créer ;
- **la source d'un objet ne change jamais.** Un objet créé dans l'outil (`Source = "saisie"`), exporté vers Archi puis
  réimporté, garde `saisie` : au réimport, `ArchimateExchange` met à jour son nom, sa description et ses propriétés, mais
  pas sa source. Il en découle trois défauts :
  - son nom et sa description restent modifiables dans l'outil, et le réimport suivant les écrase sans prévenir ;
  - supprimé dans Archi, il n'est pas archivé, car `ArchiveMissing` ne regarde que les objets dont la source est `archi` ;
  - la règle « Archi gagne » ne s'applique jamais pleinement à lui ;
- **le réimport écrase et efface en silence.** Il remplace la valeur d'une propriété saisie ici quand Archi porte la
  même clé. Il retire aussi une propriété déclarée par Archi lorsque le fichier ne la porte pas. Le compte rendu ne
  dit que « objet mis à jour ».

## Décision

1. **Le maître d'un objet est déterminé par sa source.**
   - Pour un objet venu d'Archi (`Source = "archi"`), Archi fait foi sur le nom, la description et les propriétés
     déclarées par Archi. Les propriétés saisies ici ou reprises du classeur d'urbanisation restent modifiables ici.
   - Pour un objet qui ne vient pas d'Archi (saisie, classeur d'urbanisation, dessin draw.io, catalogue), l'outil fait foi
     sur toutes ses propriétés, y compris celles qu'a déclarées Archi.
   - Ce qu'une autre source externe alimente reste à cette source : les propriétés tenues par le catalogue
     (`CatalogueModel.IsOwned`) et les sources de la décision 005.
2. **Un objet revenu d'Archi devient un objet d'Archi.** Au réimport, un objet déjà présent dont l'identifiant figure dans
   le fichier prend `Source = "archi"`. À partir de là, la règle 1 lui applique celle des objets d'Archi : son nom et sa
   description se verrouillent ici, et s'il disparaît du fichier suivant, il est archivé. Ses propriétés saisies ici ou
   reprises du classeur sont conservées, comme pour tout objet d'Archi.
3. **Archi gagne, et le compte rendu d'import le montre.** Le compte rendu liste une ligne par valeur que l'import remplace
   ou efface, en donnant l'objet, la propriété, l'ancienne valeur et la nouvelle (ou « effacée »). Deux cas sont visés :
   - une propriété qui ne vient pas d'Archi sur un objet d'Archi, c'est-à-dire une valeur que quelqu'un a pu saisir ici ;
   - toute propriété d'un objet qui bascule à cet import selon le point 2, puisque toutes ses valeurs ont été saisies ici.

   Les changements d'une propriété d'Archi sur un objet d'Archi ne sont pas listés : c'est Archi qui change sa propre
   donnée, et cette donnée n'a pas pu être saisie ici.

## Conséquences

- `Mastery.MasterOfProperty` : une propriété déclarée par Archi n'est maîtrisée par Archi que sur un objet d'Archi.
  Un test couvre la saisie de `lifecycle.state` sur un objet créé ici, qui doit être acceptée.
- `ArchimateExchange`, à l'import des éléments : la source bascule à `archi` pour tout objet trouvé dans le fichier, et
  le compte rendu reçoit une ligne par valeur écrasée ou effacée, selon le point 3. Tests à écrire :
  - aller-retour d'un objet créé ici : il bascule, son nom devient verrouillé, et il est archivé s'il manque au fichier
    suivant ;
  - écrasement d'une propriété saisie ici : une ligne au compte rendu ;
  - effacement au premier retour : une ligne au compte rendu.
- L'écran grise déjà les champs selon `Mastery`. Il suit la règle sans code propre.
- Le fichier stocké ne change pas de schéma : `Source` existe déjà sur l'objet.
- **Risque résiduel, accepté par l'arbitrage du PO** : une propriété saisie ici qu'Archi se met à porter reste modifiable
  ici sur les objets d'Archi. Chaque réimport écrase la saisie, et c'est seulement le compte rendu qui le montre. Si les
  comptes rendus montrent des écrasements répétés sur une même propriété, la suite sera de rendre cette propriété
  maîtrisée par Archi sur les objets où Archi la porte. Il faudra alors mémoriser, objet par objet, les clés que le
  dernier import a portées.
- Coût ≈ 90 à 140 k tokens (≈ ½ j) : la règle 1 et son test, la bascule et ses tests, puis les lignes du compte rendu.

## Écarté

- **L'outil reste maître d'une propriété saisie ici, même si Archi la porte** (première demande de Haussman, retirée
  le 27/11). Cela ferait deux maîtres pour une même valeur selon le sens de l'échange, et l'export finirait par écraser
  le modèle maître d'Archi.
- **Changer la définition de la propriété (`Source = archi`) dès qu'Archi la porte sur un objet.** La propriété se
  verrouillerait alors sur tous les objets d'Archi, et le réimport suivant effacerait les valeurs saisies ici sur
  les objets où Archi ne la porte pas, sans prévenir.
- **Garder la source d'origine après le retour d'Archi.** Un objet échappe alors à la fois au verrouillage et à
  l'archivage. Il n'aurait réellement aucun maître.
