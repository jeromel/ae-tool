# Prise en main

Le chemin le plus court pour se servir de l'outil : le lancer, y mettre le référentiel, régler son métamodèle,
puis faire vivre les données. Compter une heure pour tout parcourir.

## 1. Lancer

```powershell
.\lancer.ps1
```

Le script construit l'outil au premier lancement, puis l'ouvre sur http://localhost:5080. Les données vont dans le dossier
`donnees`, à côté du code. Un référentiel créé avec une version antérieure est repris et converti automatiquement ;
l'original est gardé à côté.

Au premier lancement, l'outil demande de créer le compte administrateur. Le code d'installation est affiché dans la
console et dans `donnees\code-installation.txt`.

## 2. Mettre le référentiel

**Administration › Imports** :

1. le modèle Archi, exporté par Fichier › Exporter › Modèle au format d'échange Open Group ;
2. puis le classeur d'urbanisation, rapproché par nom avec les applications.

Les imports sont rejouables : réimporter le même fichier ne crée aucun doublon, met à jour ce qui a changé et archive ce
qui a disparu. Ce qui a été saisi dans l'outil est conservé, sauf quand Archi porte la même donnée : c'est alors Archi
qui l'emporte, et le compte rendu d'import donne, pour chaque valeur remplacée, l'ancienne et la nouvelle. Un objet ou
une relation créé dans l'outil et retrouvé dans le fichier devient un élément d'Archi : son nom et sa description se
modifient ensuite dans Archi, et il est archivé s'il disparaît du modèle. Une sauvegarde est prise avant chaque import.

### Les dessins draw.io

**Contribuer › Importer un dessin** (dès le rôle contributeur) : choisissez un fichier .drawio, .xml ou .drawio.svg
(« SVG modifiable »). L'outil propose, forme par forme, avant d'écrire quoi que ce soit :

| Ce que porte la forme | Proposition |
|---|---|
| donnée `ae.id` (Modifier les données dans draw.io) | lier à cet objet |
| forme de la bibliothèque ArchiMate de draw.io | lier à l'objet du même nom et du même type, sinon le créer avec le type que donne la matrice de transcodification |
| donnée `archimate.type` (un concept ArchiMate ou un type de l'outil) | même chose, avec ce type |
| forme ordinaire (rectangle, note…) | lier à l'objet du même nom s'il est seul à le porter, sinon garder comme dessin |

Les liens suivent la même logique : un lien entre deux objets se rattache à leur relation existante, ou crée la relation
quand son tracé est celui d'une relation ArchiMate (flux en tirets, réalisation, composition…). Une flèche ordinaire
reste un trait de dessin, sauf si une seule relation relie déjà les deux objets. Deux objets du même nom ne sont jamais
départagés au hasard : la ligne attend votre choix.

**Schémas BPMN.** L'outil reconnaît un processus BPMN, dessiné avec la bibliothèque BPMN de draw.io ou avec des formes
ordinaires : une pool est un couloir qui contient des couloirs, une forme posée dans un couloir est une tâche, un losange
une passerelle, un petit cercle un événement. Il ne relie au référentiel que ce qui y existe déjà, pour ne pas le
charger du détail d'un BPMN :

| Forme BPMN | ArchiMate | Proposition par défaut |
|---|---|---|
| pool | processus ou acteur | lier à celui du même nom, sinon dessin |
| couloir | rôle | lier au rôle ou à l'acteur du même nom, sinon créer le rôle |
| tâche | processus | lier au processus du même nom, sinon dessin |
| passerelle (losange) | jonction | toujours dessin : elle oriente le déroulement, ce n'est pas un objet |
| événement | événement métier | lier à celui du même nom ; sans nom, dessin |
| flèche entre deux tâches liées | déclenchement | créer la relation |
| tâche liée posée dans un couloir lié | affectation du rôle au processus | créer la relation |

Les noms laissés par draw.io (« Pool », « Lane 3 ») ne sont rapprochés de rien. Le schéma garde ses formes (cercles,
losanges) et ses tracés à angles droits dans la vue.

Chaque page devient une vue, rangée dans **Vues › Schémas draw.io**. Les objets créés partent en validation. Réimporter
le même fichier met à jour ses vues sans doublon, même si un objet a été renommé entre-temps ; une page retirée du
fichier archive sa vue. L'export Archi complet n'emporte pas ces vues (Archi reste maître des siennes) ; le bouton
« Exporter vers Archi » d'une vue draw.io l'y fait passer.

### Rapporter dans le modèle Archi ce qui a été créé ici

Archi ne permet pas de copier-coller un objet d'un modèle à l'autre. Pour faire entrer dans le modèle maître les objets
créés dans l'outil (depuis un dessin, une saisie ou un classeur), on passe par un modèle intermédiaire, qui ne contient
que ce qui n'est pas encore dans Archi.

**À valider › Pas encore dans Archi** (valideur et administrateur) liste les objets validés créés ici et absents du
dernier import du modèle Archi, avec les relations qui les accompagnent. Ce qui en est exclu :
- les objets proposés ou brouillons, et les relations qui les touchent : on valide d'abord ;
- les vues : Archi reste maître de ses vues. Les objets et les relations qu'elles contiennent partent ;
- les dossiers : Archi range les nouveaux objets dans les dossiers par défaut de leur type.

Un objet d'Archi relié à un objet nouveau figure dans le fichier par son identifiant, son type et son nom seulement : la
fusion ne touche pas ses valeurs.

1. **Dans l'outil**, **Télécharger pour Archi**.
2. **Dans Archi**, *Fichier › Importer › Open Exchange XML Model* : le fichier s'ouvre comme un nouveau modèle.
   **Enregistrer** ce modèle en `.archimate`.
3. **Ouvrir le modèle maître**, le sélectionner, puis *Fichier › Importer › Another Model into Selected Model* et choisir
   le `.archimate` de l'étape 2. **Cocher « Update existing objects and folders »**, **décocher « Update model
   information and top-level folders »**.
4. **Ranger les nouveaux objets**, qu'Archi a posés dans les dossiers par défaut de leur type.

Ensuite, réimporter le modèle maître dans l'outil (§ 2) : ce qui vient d'y entrer passe sous la maîtrise d'Archi, et la
liste se vide. Si on oublie, rien n'est perdu : la liste reste pleine, et un nouveau téléchargement ne contient toujours
que ce qui manque à Archi.

Archi reconnaît chaque objet à son identifiant, qui ne change pas sur ce trajet : la fusion ajoute les nouveaux objets
sans dupliquer les autres, et un réimport du modèle maître dans l'outil ne crée pas de doublon. Après le trajet, les
flux venus du catalogue des données et des flux sont des relations d'Archi, comme le reste : supprimés dans Archi, ils
sont archivés ici au réimport suivant, et le réimport du catalogue les recrée. Leur code et leurs caractéristiques
restent tenus par le catalogue : modifiez-les dans le catalogue, pas dans Archi.

L'export complet (**Administration › Imports › Exports › Référentiel au format d'échange ArchiMate**) reste disponible
pour un premier passage, quand aucun modèle Archi n'a encore été importé. Il recrée toute l'arborescence des dossiers et
réécrit les valeurs du modèle maître : ne pas l'utiliser pour ce trajet.

Pour essayer l'outil sans données réelles, le dossier [`exemples/hydreane`](../exemples/hydreane/README.md) contient un
jeu complet d'une entreprise fictive : modèle Archi, classeur d'urbanisation, dessin draw.io et présentation de la méthode.

## 3. Régler le métamodèle

**Administration › Métamodèle.** C'est le cœur de l'outil : les types d'objets, de relations et de propriétés sont les
vôtres, pas ceux d'ArchiMate. ArchiMate n'est plus qu'un format d'échange, relié au métamodèle par une matrice.

| Onglet | Ce qu'on y règle |
|---|---|
| Types d'objets | libellé, couche, rôles, visuel ; créer, archiver, fusionner |
| Relations | libellé, lecture inverse, rôles, trait ; couples source → cible autorisés |
| Propriétés | type de donnée (texte, nombre, date, oui/non, liste), valeurs permises, types concernés, obligatoire |
| Couches | libellés et ordre |
| Transcodification ArchiMate | la matrice : quel concept ArchiMate correspond à quel type |
| Notation | grille et seuils du portefeuille |
| Réglages | état de validation des objets importés d'Archi |

### Les rôles : ce qui relie un type aux écrans

Un écran ne lit jamais un type par son nom : il lit les types qui portent un **rôle**. Vous pouvez donc renommer un type,
en créer, en fusionner, sans casser un écran.

| Rôle | Ce qu'il active |
|---|---|
| Application | espace Applications, portefeuille, matrices, reprise du classeur d'urbanisation |
| Capacité | carte des capacités, rattachement des applications |
| Projet | liste des projets, impacts et adhérences |
| Donnée | espace Données |
| Support de donnée | artefact (fichier, base, message) qui matérialise une donnée |
| Infrastructure | espace Infrastructure, socle technique d'une application |
| Fond de carte | présent dans une vue projet, il ne vaut pas impact |
| Flux (relation) | espace Flux, pastille sur les vues, matrice émetteurs × récepteurs |
| Décomposition (relation) | arbre des capacités |

### La matrice de transcodification, par l'exemple

Dans Archi, zones, quartiers et îlots du plan d'occupation des sols sont tous des `Grouping`, que le type par défaut
« Zone / quartier » reçoit en bloc. Vous voulez des types distincts. Il suffit qu'une propriété les distingue dans Archi,
par exemple `pos.niveau` valant `zone`, `quartier` ou `îlot`.

1. **Types d'objets** : « Nouveau type », libellé « Quartier », couche fonctionnelle, « Créer ». La clé (`quartier`) est
   créée par l'outil. Dans son détail, donnez-lui les **rôles** que portait le type d'origine si les mêmes écrans doivent
   continuer à le lire (ici « Fond de carte », que porte « Zone / quartier »).
2. **Transcodification ArchiMate › Objets** : sur la ligne « Quartier », « Ajouter une règle » : concept `Grouping`,
   discriminant `pos.niveau`, valeur `quartier`, règle principale.
3. **« Enregistrer la matrice ».** Rien n'est envoyé avant : les modifications restent en brouillon dans la page, et les
   incohérences sont signalées près des règles concernées.
4. **« Simuler la réapplication »** : l'outil dit combien d'objets changeraient de type, d'où vers où, sans rien modifier.
   Il avertit si le nouveau type ne porte pas un rôle du type de départ : ces objets sortiraient de l'écran correspondant.
5. **« Réappliquer la matrice »** : les objets déjà importés prennent leur nouveau type. Une sauvegarde est prise juste avant.

Ce que fait la matrice ensuite :

- **À l'import**, pour chaque élément Archi, dans l'ordre : le type inscrit dans la propriété marqueur `ae:type` s'il y en
  a une ; sinon une règle dont le discriminant correspond ; sinon la règle simple du concept ; sinon le type
  « Autre (non classé) ».
- **À l'export**, un objet venu d'Archi ressort avec son concept d'origine : l'aller-retour ne perd rien. Un objet créé
  dans l'outil sort avec le concept de la règle principale de son type.
- **Un type sans équivalent ArchiMate** (« Contrat », « Site industriel »…) n'a pas besoin de règle : il est exporté
  dans le concept de repli (`Grouping`) avec la propriété `ae:type`, et retrouve son type au réimport.
- **Changer le type d'un objet à la main** est permis, y compris pour un objet venu d'Archi : le choix survit aux
  réimports tant que son concept ArchiMate ne change pas dans Archi.

Le bandeau en tête de l'onglet résume l'état de la matrice : concepts transcodés, types sans règle, anomalies.

### Propriétés

Une propriété déclarée « liste » avec ses valeurs permises devient une liste déroulante à la saisie ; une propriété
« obligatoire » pour un type est signalée sur la fiche tant qu'elle manque, et comptée dans **Qualité ›
Conformité au métamodèle**. Cocher « Calque sur les vues » la propose en pastille et en couleur sur les diagrammes.

## 4. Saisir dans l'outil

- **Créer** : « Nouvel objet » dans Tous les objets, ou le bouton de création d'un espace (« Nouvelle application »…).
- **Modifier** : « Modifier » sur la fiche. Les champs suivent le métamodèle du type.
- **Relier** : « Ajouter une relation » sur la fiche. Seuls les types de relation et les objets permis par les couples
  autorisés sont proposés.
- **Archiver** plutôt que supprimer : rien n'est jamais effacé.

**Une donnée a un seul maître.** Le nom, la description et les propriétés d'un objet venu d'Archi se modifient dans
Archi ; ce qui vient d'une source externe se modifie dans cette source. L'outil l'indique sur chaque champ en lecture
seule. On peut toujours, dans l'outil, changer le type d'un tel objet, lui ajouter des propriétés et des relations.

## 5. Valider

| État | Qui | Comment |
|---|---|---|
| Brouillon → Proposé | contributeur | « Proposer à la validation » sur la fiche |
| Proposé → Validé | valideur | « Valider » sur la fiche, ou en nombre dans **Contribuer › À valider** |
| Proposé → Brouillon | valideur | « Refuser », avec un commentaire obligatoire |
| Validé → Proposé | automatique | dès qu'un contributeur modifie le fond d'un objet validé |

Les objets importés d'Archi entrent validés (réglable), ceux qui viennent du classeur ou d'une source externe entrent
proposés. La fiche de chaque objet montre son **historique** : qui, quand, et pour chaque champ l'avant et l'après.

## 6. Rapprocher la CMDB et les autres sources

**Administration › Sources externes.** Une source est un outil qui maîtrise une partie de ce que le référentiel décrit :
la CMDB pour les serveurs, l'outil de gestion de projet pour les projets.

1. « Nouvelle source » : partir du modèle « Fichier » (un export CSV ou Excel suffit) ou « API REST » (lecture par l'API de la CMDB ; adresse, pagination et noms de champs se règlent dans l'onglet Connexion).
2. **Correspondances** : charger un aperçu, puis indiquer la colonne de l'identifiant, celle du nom, celle de la nature ;
   associer chaque nature à un type du métamodèle ; choisir les colonnes à reprendre comme propriétés.
3. **Rapprochement** : « Lancer le rapprochement ». Le calcul ne modifie rien. Il classe les lignes : liées, proposées
   (même nom), nouvelles, ambiguës (plusieurs objets portent ce nom), ignorées, invalides.
4. Décider ligne à ligne ou en nombre, puis « Appliquer » : un récapitulatif chiffré précède toute écriture.

L'outil ne fait que lire la source. Un objet lié garde l'identifiant de la source : au rapprochement suivant il est
retrouvé même si son nom a changé.

### Compter les postes de travail par profil

Un profil de poste (« PC bureautique », « PC métier exploitation ») est un Device d'Archi portant la propriété
`asset.subtype` = « Profil de poste ». L'outil lui attribue un code `PP-001`, `PP-002`… visible sur sa fiche ; la CMDB le
reprend dans un champ libre de chaque poste. Sur la source de la CMDB, onglet **Postes de travail** : indiquer ce champ
pivot, et ceux du site, de l'entité et du modèle ; éventuellement ne garder que les lignes de la catégorie « poste de
travail ». « Compter » range chaque poste dans son profil. Les postes sans profil ou avec un code inconnu sont listés, avec
un profil proposé d'après leur entité. Seuls des effectifs sont gardés, aucun nom d'utilisateur.

## 6 bis. Le catalogue des données et des flux

**Administration › Imports › Importer le catalogue.** Le classeur décrit les applications, les objets de données (maître, relais,
consommateurs, owner, steward, sensibilité, données personnelles), leurs attributs, les flux applicatifs, les flux
techniques et la traçabilité entre les deux. L'analyse rapproche chaque ligne du référentiel par son code, puis par son
nom, et propose des noms proches quand rien ne correspond : « Factures abonnés » du catalogue peut ainsi être liée à
« Factures émises » d'Archi. Rien n'est écrit avant « Importer ». L'import se rejoue.

Ensuite :

- **le parcours d'une donnée** (lien « Parcours » dans les espaces, « Voir le parcours » sur une fiche) : la donnée, les
  applications et les flux applicatifs entre elles, la chaîne technique du flux choisi (clic sur sa pastille), les serveurs
  et partages où elle est stockée ou transite, les profils de poste qui y accèdent avec leur nombre de postes. Rien n'est
  choisi à l'ouverture. Un clic (ou Entrée) choisit une case ou la pastille d'un flux : ce qui lui est relié reste en plein
  contraste dans toutes les bandes, le reste s'estompe, et le titre de chaque bande dit ce qu'elle montre (« 3 données
  transportées par FA-003 »). Un flux choisi est relié par un trait jaune aux données qu'il transporte ; sa pastille
  porte son code, ou un numéro, et le nombre de données transportées (« FA-003 · 3 », « · ? » si rien n'est renseigné).
  Dans le panneau, les données, applications et flux cités se choisissent d'un clic. Échap ou un clic sur le fond
  efface le choix. Un double clic (ou Maj+Entrée) recentre le parcours sur l'élément (le bus d'intégration : tout ce
  qui s'arrête s'il tombe ; un serveur : ce qui y est stocké). « Constaté seulement » cache ce qui n'est pas vérifié ; « Suivre les données dérivées »
  prolonge vers les données produites à partir de celle-ci. Le parcours se télécharge en image et s'enregistre comme vue,
  exportable vers Archi. Sous le dessin, ce que le parcours ne sait pas dire (hébergement inconnu, chaîne incomplète,
  passage non vérifié) ;
- **Qualité › Cohérence du catalogue** : taux d'information constatée, et les incohérences (émetteur qui
  n'est ni maître ni relais, sensibilité hors liste, chaîne qui n'atteint pas la cible, nœud « Partage… » à reclasser) ;
- **Explorer › Données personnelles** : les données à protéger, leurs flux (frontière IT/OT signalée) et les partages
  de fichiers qui en hébergent.

Un partage de fichiers est un Node portant `asset.subtype` = « Partage de fichiers » ; les fichiers qui y sont déposés
sont des artefacts qui réalisent l'objet de données.

### Lire une vue

Ouvrir une vue lui donne la place : le menu se réduit à un rail (« » » le rouvre) et l'arborescence se replie (le bouton
**Vues** la rouvre). Une vue ne s'ouvre jamais sous 70 % : si elle ne tient pas à l'écran à ce zoom, elle s'ouvre sur son
coin haut gauche, et **Tout voir** (ou un double clic sur le fond) la montre en entier. En rendu lisible, les liens
suivent les couloirs entre les cartes, à angles droits, et ne passent jamais sur une carte qui n'est pas l'une de leurs
extrémités ; plusieurs liens dans un même couloir sont écartés pour rester distincts. Cliquer sur une carte fait
ressortir ses liens au premier plan. Le rendu « Comme dans Archi » garde le tracé et les points de passage d'Archi. Au-delà de 150 éléments,
une note invite à repérer un élément ou à ouvrir une zone.

En rendu lisible, un nom n'est jamais coupé au milieu d'un mot : il passe à la ligne entre deux mots, sur trois lignes au
plus, puis sa police est réduite jusqu'à 11 px ; s'il ne tient toujours pas, il est abrégé par « … » à la fin d'un mot,
et le nom complet s'affiche au survol. Un élément posé dans une zone n'est pas relié à elle par un trait : l'imbrication
suffit. Chaque flux porte une pastille avec son code du catalogue (FA-015), ou à défaut un numéro ; son nom s'affiche
au survol, et sous la pastille quand la place est libre. **Cliquer sur la pastille** ouvre le panneau du flux : ses
extrémités, les données qu'il transporte, son protocole et son chiffrement. Quand un calque est choisi, une légende
rappelle ce que porte la pastille des cartes (par défaut, la criticité métier de 1 à 4).

La vue s'ouvre en rendu lisible, aux couleurs du type d'élément. Les **Réglages avancés**, ouverts à tous, proposent le
rendu « Comme dans Archi », les couleurs d'Archi, le calque et le fond sombre, ainsi que « Exporter vers Archi » ; à
partir du rôle contributeur, ils proposent aussi « Réorganiser ».

### Réorganiser une vue

Dans **Vues**, **Réglages avancés › Réorganiser** (à partir du rôle contributeur) passe la vue en mode édition :

- glisser une forme la déplace, avec tout ce qu'elle contient ; la poignée en bas à droite la redimensionne ;
- **Organiser automatiquement** range la vue : chaque zone garde son contenu ; quand les liens donnent un sens de
  lecture, les cartes sont posées en colonnes de gauche à droite (émetteurs, relais, récepteurs) et les zones en
  rangées de haut en bas, dans l'ordre qui limite les croisements ; sinon (aucun lien, trop de boucles), en grille, les
  cartes reliées côte à côte ; les cartes prennent une taille commune assez large pour leur nom ;
- rien n'est gardé avant **Enregistrer** ; **Annuler** rend la vue telle qu'elle était.

La disposition enregistrée se superpose à celle d'Archi ou de draw.io : elle survit à leurs réimports et part vers
Archi avec « Exporter vers Archi ». **Revenir à la disposition d'origine** l'efface. Chaque enregistrement est inscrit
au journal. Dans toutes les vues, le nom d'un lien qui tomberait sur une carte, une pastille ou un autre nom n'apparaît
que lorsque l'on clique sur l'une de ses deux extrémités ; la pastille d'un flux, elle, reste toujours affichée.

### Étapes et scénarios d'un projet

Sur la fiche d'un projet, la section **Trajectoires** décrit comment le SI évolue (dès le rôle contributeur) :

- un **scénario** est une proposition de solution, non validée ; il part de l'existant ou d'une étape d'un autre scénario
  (une variante) ; on peut le copier pour en proposer une autre ;
- chaque **étape** décrit ce qui change par rapport à la précédente : ajouter un objet (un objet *envisagé*, qui n'existe
  que dans le scénario), ajouter un lien, retirer un objet ou un lien, modifier un nom ou une propriété, remplacer un
  objet par un autre (ses liens passent au remplaçant, il est retiré) ; toutes les couches sont concernées ;
- **Voir le SI à cette étape** ouvre tous les écrans sur le SI projeté : espaces, vues, matrices, qualité, analyse
  d'impact. Un bandeau rappelle l'état regardé et résume ce qui change ; les objets portent une pastille *ajouté*,
  *modifié* ou *retiré* ; dans les vues, un objet remplacé est dessiné sous les traits de son remplaçant. Le sélecteur
  **SI regardé**, en haut de chaque page, permet de passer d'une étape à l'autre ou de revenir à l'existant.

Le SI projeté est en lecture seule : rien de ce qu'on y voit ne modifie le référentiel. Quand l'existant a bougé
depuis la saisie d'un changement (une propriété déjà modifiée, un objet déjà retiré), le changement est signalé sous
l'étape au lieu d'être appliqué en silence. Un lecteur ne voit que les scénarios proposés, pas les brouillons. Les
dates, la comparaison des scénarios, la décision du comité et la bascule vers l'existant viennent dans les incréments
suivants (voir la spécification EV-03).

## 7. Se protéger d'une erreur

**Administration › Sauvegardes** : une sauvegarde automatique par jour d'activité et avant chaque opération risquée,
« Sauvegarder maintenant » à la demande, restauration en deux clics, elle-même réversible.
**Administration › Journal** : tout ce qui a été fait, par qui, avec l'avant et l'après.

## 8. Où trouver quoi

| Je veux | Où |
|---|---|
| comprendre une couche du SI | Explorer › Capacités, Applications, Données, Flux, Infrastructure : les questions en tête de page |
| préparer un comité | Décider › Portefeuille, Projets ; l'analyse d'impact depuis la fiche d'un objet |
| savoir ce qui manque au référentiel | Contribuer › Qualité |
| retrouver un dessin d'Archi ou de draw.io | Explorer › Vues |
| importer un dessin draw.io | Contribuer › Importer un dessin |
| voir le SI à une étape d'un projet | Décider › Projets › un projet › Trajectoires, ou le bandeau « SI regardé » |
| changer le métamodèle ou la matrice | Administration › Métamodèle |
| ouvrir l'outil à un collègue | Administration › Utilisateurs |

Pour installer l'outil sur un serveur : [installation.md](installation.md). Pour l'exploiter : [exploitation.md](exploitation.md).
