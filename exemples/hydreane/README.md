# Jeu de démonstration : Hydréane

Hydréane est une **entreprise fictive** : opérateur public de l'eau potable et de l'assainissement de la vallée de
l'Arvenne (152 000 abonnés, 3 usines de production, 11 stations d'épuration, 2 450 km de réseaux, 420 salariés).
Ses sites, ses applications, ses personnes et ses chiffres sont inventés ; toute ressemblance avec une organisation
existante serait fortuite. Le jeu sert à essayer l'outil, à le montrer et à le tester avec des données réalistes, sans
rien anonymiser.

| Fichier | Contenu |
|---|---|
| `hydreane-referentiel.xml` | modèle Archi au format d'échange Open Group (ArchiMate 3.1) : 417 éléments, 903 relations, 58 vues |
| `hydreane-referentiel-urbanisation.xlsx` | classeur d'urbanisation : 38 applications, 1 080 preuves de notation, formules recalculées |
| `Hydreane-methode-urbanisation.pptx` | présentation de la méthode de notation et des premiers résultats, chiffres tirés du classeur |

## Charger le jeu

Sur une instance vide, **Tenir à jour › Imports et exports** :

1. importer `hydreane-referentiel.xml` ;
2. puis importer `hydreane-referentiel-urbanisation.xlsx`.

Résultat attendu : aucune anomalie, 38 applications enrichies par le classeur (aucune créée), 7 projets avec leurs
impacts. Le modèle s'ouvre aussi dans Archi (Fichier › Importer › Modèle au format d'échange Open Group).

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

Le test `ExampleDatasetTests` importe ce jeu à chaque exécution des tests et vérifie qu'il reste cohérent.
