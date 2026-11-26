# Jeu de démonstration : Hydréane

Hydréane est une **entreprise fictive** : opérateur public de l'eau potable et de l'assainissement de la vallée de
l'Arvenne (152 000 abonnés, 3 usines de production, 11 stations d'épuration, 2 450 km de réseaux, 420 salariés).
Ses sites, ses applications, ses personnes et ses chiffres sont inventés ; toute ressemblance avec une organisation
existante serait fortuite. Le jeu sert à essayer l'outil, à le montrer et à le tester avec des données réalistes, sans
rien anonymiser.

| Fichier | Contenu |
|---|---|
| `hydreane-referentiel.xml` | modèle Archi au format d'échange Open Group (ArchiMate 3.1) : 426 éléments, 917 relations, 58 vues ; partages de fichiers et profils de poste sous-typés par la propriété `asset.subtype` |
| `hydreane-catalogue-donnees-flux.xlsx` | catalogue des données et des flux : 23 applications, 18 objets de données, 87 attributs, 30 flux applicatifs, 11 flux techniques, 57 liens de traçabilité, listes de référence |
| `hydreane-cmdb-postes.csv` | export de la CMDB : 94 postes de travail (et deux autres équipements), avec site, entité, modèle et le code de leur profil de poste |
| `hydreane-referentiel-urbanisation.xlsx` | classeur d'urbanisation : 38 applications, 1 080 preuves de notation, formules recalculées |
| `hydreane-facturation-saas.drawio` | dessin draw.io d'une équipe projet : la facturation en SaaS en formes ArchiMate (page 1) et un croquis de l'existant en formes ordinaires, page compressée (page 2) |
| `facturation-cible.drawio`, `facturation-cible-v2.drawio`, `infra-facturation-2027.drawio` | dessins « de collègue » pour la recette du rapprochement (EV-05) : formes ordinaires, noms approchés, aucun `ae.id` ; la v2 est une copie de la v1 renvoyée sous un autre nom |
| `Hydreane-methode-urbanisation.pptx` | présentation de la méthode de notation et des premiers résultats, chiffres tirés du classeur |

## Charger le jeu

Sur une instance vide, **Tenir à jour › Imports et exports** :

1. importer `hydreane-referentiel.xml` ;
2. puis importer `hydreane-referentiel-urbanisation.xlsx`.

Résultat attendu : aucune anomalie, 38 applications enrichies par le classeur (aucune créée), 7 projets avec leurs
impacts. Ensuite, **Tenir à jour › Importer un dessin** avec `hydreane-facturation-saas.drawio` : 12 formes et liens
rattachés au référentiel, une application et quatre flux à créer, trois formes gardées comme dessin. Le modèle s'ouvre aussi dans Archi (Fichier › Importer › Modèle au format d'échange Open Group).

## Ce que le jeu couvre

| Couche | Contenu |
|---|---|
| Stratégie | 72 capacités sur 3 niveaux, 4 chaînes de valeur, moteurs, objectifs et principes |
| Métier | 19 processus, acteurs, rôles, services et événements, 25 objets métier |
| Fonctionnelle | plan d'occupation des sols : 5 zones, leurs quartiers et blocs fonctionnels |
| Applicative | 51 applications, services et interfaces, 69 flux portant protocole et chiffrement |
| Données | objets de données et référentiel maître de chaque donnée |
| Technique | nœuds, logiciels, réseaux avec VLAN, sites, chemins et artefacts |
| Industrielle | usines, stations, équipements d'exploitation, matières et réseaux d'eau |
| Transformation | 7 projets (télérelève, portail abonnés, facturation en SaaS, segmentation industrielle, GMAO, données, messagerie), chacun avec ses vues existant et cible |

Situations volontairement présentes pour exercer l'outil : 13 applications du modèle absentes du classeur, une
application retirée et une à l'usage arrêté, deux notations incomplètes, neuf décisions « À instruire » dont ONDINE
(facturation, déclencheur rouge sur C1), des décisions retenues en comité qui s'écartent de la décision calculée.

### Le catalogue des données et des flux

Après le modèle Archi, **Tenir à jour › Importer le catalogue** avec `hydreane-catalogue-donnees-flux.xlsx` : les 23
applications et 10 objets de données sont rapprochés par leur nom. Lier DO-003 « Factures abonnés » à « Factures émises »
(proposé comme nom proche), le reste peut être créé. Puis, sur une source « Fichier » de la CMDB, onglet **Postes de
travail** : champ pivot « Profil de poste », site « Site », entité « Entité », modèle « Modèle », lignes dont « Catégorie »
vaut « Poste de travail », et `hydreane-cmdb-postes.csv`. Le parcours de « Factures émises » répond alors à la question
« où sont mes factures » : ONDINE et VM-FACT-01 (base Oracle), le portail sur VM-WEB-01, le bus sur VM-ECH-01 en transit, le
duplicata PDF sur le partage Clientèle, 27 postes du profil PC bureautique Direction clientèle.

Situations laissées exprès : « Partage Exploitation », nœud nommé « Partage… » sans sous-type, à reclasser ; le profil
« PC métier exploitation » sans code (l'outil lui attribue PP-004) ; dans le catalogue, une sensibilité tirée d'un autre
vocabulaire que la liste de référence, des chaînes techniques qui n'atteignent pas l'application cible (FA-028 vers
l'archivage), un destinataire « EXTERNE » hors de l'onglet Applications, des consommateurs qui ne sont pas des
applications ; dans la CMDB, des postes sans profil, trois avec un code inconnu et deux qui portent le nom du profil au
lieu de son code.

Le test `ExampleDatasetTests` importe ce jeu à chaque exécution des tests et vérifie qu'il reste cohérent.

### Les dessins « de collègue » (recette d'EV-05)

Contenu métier : `note-dessins-collegues-hydreane-haussman` (instance de l'équipe). **Base de départ** : modèle Archi,
classeur et catalogue chargés, **sans** `hydreane-facturation-saas.drawio`, sinon « Facturation SaaS » existe déjà.

- `facturation-cible.drawio`, par le chef de projet « Facturation en SaaS » : une page « Facturation cible », 12 formes
  (dont un rectangle grisé « Ondine (à décommissionner) », « Bus + ETL » qui désigne deux applications, un cylindre
  « Oracle factu », un fichier « Suivi relances (Excel) » et une note) et 9 flèches, dont une sans libellé. Aucune flèche
  entre CRM et Ondine, alors que le référentiel relie CRM Usagers à ONDINE : c'est un lien manquant attendu.
- `facturation-cible-v2.drawio`, trois semaines plus tard : **copie** de la v1, donc même identifiant de page et mêmes
  identifiants de formes, sous un autre nom de fichier. « Portail » devient « Mon Espace Eau (portail) » ; « Archivage »
  (avec sa flèche « factures PDF ») et « Coffre-fort abonnés » sont ajoutés ; « Suivi relances (Excel) » et sa flèche
  disparaissent. À l'import, la question « même dessin ? » doit se poser, et la bonne réponse est oui.
- `infra-facturation-2027.drawio`, par l'équipe infrastructure : autre fichier, autre identifiant de page, une page
  « Hébergement facturation », 5 formes et 3 flèches. « Portail » doit être rapproché de Mon Espace Eau grâce au nom
  retenu au dessin 1.

Ce que la version 0.15 fait aujourd'hui de ces dessins (avant EV-05) : seuls les noms identiques à la casse près sont
rattachés (Capta, Ondine, VM-FACT-01) ; toutes les autres formes restent du dessin, sans proposition. Le résultat attendu
de chaque forme, critère par critère, est dans la note de Haussman.
