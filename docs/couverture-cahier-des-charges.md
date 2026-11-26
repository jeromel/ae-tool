# Couverture du cahier des charges

État de la version 0.15.0 face à chaque exigence du cahier des charges (révision du 13 novembre 2026).
**Couvert** : fait et vérifié par un test ou par la recette dans un navigateur. **Partiel** : une partie manque, elle est nommée.
**Non fait** : rien n'existe. **À la DSI** : ne relève pas du code.

## Où en sont les lots

| Lot | État | Ce qui reste |
|---|---|---|
| 1, référentiel | **Livré**, avec une dérogation et trois exigences partielles | base relationnelle (ET-04, dérogation de la décision 004) ; filtres enregistrés (EF-03) ; export Excel (EF-04) ; fraîcheur par propriétaire (EF-53) |
| 2, CMDB et projets | **Commencé** | valider le client API REST sur la CMDB réelle ; synchronisation planifiée ; outil de gestion de projet par son API ; avis d'architecture (EF-24) |
| 3, modélisation | Amorcé par le rendu des vues | édition graphique des diagrammes (EF-60, 62, 65, 67) |
| 4, trajectoires | Non commencé | scénarios, steps, journal des décisions (EF-30 à 36, EF-54) |

## Besoins exprimés après le cahier des charges (15 novembre 2026)

| Besoin | Priorité | Lot | État | Précision |
|---|---|---|---|---|
| EV-01 Importer des schémas draw.io | 0 | 3 | Couvert (0.10.0), schémas BPMN (0.11.0) | fichiers `.drawio` (pages compressées ou non), formes ArchiMate de draw.io traduites par la matrice de transcodification, autres formes rapprochées du référentiel par leur libellé ; schéma conservé comme vue |
| EV-02 Dessiner dans l'outil avec draw.io intégré, à partir des objets du référentiel | 1 | 3 | Non fait | éditeur draw.io hébergé avec l'outil (aucun appel extérieur), bibliothèque de formes tirée du référentiel ; répond à EF-60, 62, 65, 67 |
| EV-04 Catalogue des données et des flux : où vit une donnée, par où elle passe | 1 | 2 | Couvert (0.15.0) | import du catalogue rapproché du référentiel ; parcours d'une donnée sur quatre bandes (données, applications et flux applicatifs, chaîne technique, hébergement) plus les profils de poste ; partages de fichiers ; comptage des postes de la CMDB par profil ; contrôles de cohérence ; registre des données personnelles |
| EV-03 Étapes de transition et scénarios des projets, SI vu à chaque étape | 2 | 4 | Incrément 1 livré (0.13.0) : scénarios, étapes, changements sur toutes les couches, objets envisagés, SI projeté dans tous les écrans ; incréments 2 à 6 à venir | dessin des étapes et des scénarios par les chefs de projet et architectes ; vue du SI à une étape choisie ; scénarios par étape ; reprend EF-30 à 36 |

## Exigences fonctionnelles

| Exigence | État | Précision |
|---|---|---|
| EF-01 Créer, modifier, archiver un objet | Couvert | fiche d'objet et « Nouvel objet » ; ce qui vient d'Archi se modifie dans Archi |
| EF-02 Relier par une relation autorisée | Couvert | couples source → cible contrôlés par le serveur |
| EF-03 Rechercher, filtrer sur tout attribut, enregistrer un filtre | Partiel | recherche par nom sans accents, filtres par couche, type et état, questions prêtes par espace, adresse partageable ; **manquent** le filtre sur un attribut quelconque et l'enregistrement d'un filtre |
| EF-04 Import et export en masse, CSV ou Excel | Partiel | export CSV ; import CSV ou Excel par les sources externes, avec rapprochement ; **manque** l'export Excel |
| EF-05 Propriétaire, source, dernière mise à jour | Couvert | sur la fiche |
| EF-10 Fiche par application | Couvert | attributs portés par des propriétés du métamodèle, reprises du classeur |
| EF-11 Notation selon la grille paramétrée | Partiel | criticité métier, critères C1 à C4, preuves ; **manque** la séparation « qui note quoi » entre métiers et DSI |
| EF-12 Décision de comité, date, instance | Partiel | décision retenue ; **manquent** la date et l'instance (l'historique donne la date de saisie) |
| EF-13 Signaler les risques | Partiel | portés par les propriétés du classeur et par la règle de garde ; pas d'alerte dédiée |
| EF-14 Liste, matrice valeur et technique, par échéance | Partiel | liste et quadrant ; **manque** la restitution par échéance |
| EF-20 Fiche projet saisie ou synchronisée | Partiel | un projet est un objet : saisi, ou importé d'Archi ; synchronisation par fichier seulement |
| EF-21 Objets impactés et nature de l'impact | Couvert | |
| EF-22 Adhérences | Couvert | objets partagés et objets directement liés |
| EF-23 Matrice des adhérences | Couvert | |
| EF-24 Avis d'architecture | Non fait | |
| EF-30 à EF-36 Scénarios et steps | Non fait | lot 4 |
| EF-40 Accueil par profil | Couvert | questions et chiffres selon le profil choisi |
| EF-41 Matrices croisées | Couvert | par relation ou par imbrication dans une vue |
| EF-42 Graphe de dépendances navigable | Partiel | relations navigables et analyse d'impact à profondeur réglable ; pas de dessin de graphe |
| EF-43 Analyse d'impact amont et aval | Couvert | |
| EF-44 Export de toute vue en CSV, Excel, PDF | Partiel | objets en CSV, vue au format ArchiMate ; ni Excel ni PDF |
| EF-50 Annuaire d'entreprise et quatre rôles | Couvert, **à valider** | comptes locaux vérifiés ; Entra ID écrit mais jamais essayé sur un locataire. Écarts : les rôles sont gérés dans l'outil et non par groupes Entra (décision 003) ; pas de compte local de secours en mode Entra |
| EF-51 Circuit de validation | Couvert | |
| EF-52 Historique : qui, quand, avant, après | Couvert | |
| EF-53 Complétude et fraîcheur par type et par propriétaire | Partiel | complétude et conformité par type ; **manquent** la fraîcheur et la ventilation par propriétaire |
| EF-54 Journal des décisions d'architecture | Non fait | lot 4 |
| EF-60, 62, 65, 66, 67 Édition graphique, export d'image | Partiel (0.12.0) | réorganisation des vues : déplacer, redimensionner, organisation automatique, disposition gardée aux réimports et exportée vers Archi ; ajout d'objets et de liens dans le dessin : EV-02 |
| EF-61 Un objet, plusieurs diagrammes | Couvert en lecture | |
| EF-63 Visuel du type, visuel propre à l'élément | Couvert | |
| EF-64 Colorer selon un attribut | Couvert | calques |
| EF-68 Conversion d'un diagramme vers Archi | Couvert | export d'une vue |
| EF-69 Règle de repli pour un type sans équivalent | Couvert | concept de repli et propriété marqueur, réglables dans la matrice |

## Interfaces

| Système | État | Précision |
|---|---|---|
| Archi | Couvert | import rejouable, export, aller-retour sans perte vérifié sur le modèle réel |
| CMDB par API REST | Partiel, **à valider** | lecture par l'API (client générique réglable) et rapprochement par identifiant écrits et testés contre un serveur simulé ; jamais essayés sur une CMDB réelle ; déclenchement manuel, pas de synchronisation planifiée |
| Outil de gestion de projet | Par fichier | export CSV ou Excel rapproché comme toute source |
| Annuaire | Partiel | flux « authorization code » avec PKCE et lecture de l'identité sur Graph ; pas de lecture des groupes |
| API de l'outil | Partiel | l'interface n'utilise que l'API REST ; **manque** sa description OpenAPI |

## Exigences techniques

| Exigence | État | Précision |
|---|---|---|
| ET-01 .NET à support long, OpenAPI, Angular supporté | Partiel | .NET 10 et Angular 22 ; pas de description OpenAPI |
| ET-02 Un module par domaine | Couvert | |
| ET-03 Cœur indépendant | Couvert | |
| ET-04 Base relationnelle | **Dérogation** | stockage par fichier pour le pilote (décision 004) ; à lever avant généralisation |
| ET-05 Pas de composant lourd | Couvert | un processus, un dossier |
| ET-10 Spécifications et tests dans Git | Couvert | |
| ET-11 Tests automatisés | Couvert | 361 tests ; les scripts échouent si un test échoue ; pas de chaîne d'intégration continue |
| ET-12 Analyse statique et formatage | Partiel | avertissements du compilateur bloquants ; pas de règle de formatage automatisée |
| ET-13 Revue par une seconde personne | **À la DSI** | le code a été produit avec une IA : la revue humaine reste entièrement à faire |
| ET-14 Documentation minimale | Couvert | prise en main, architecture, décisions, exploitation, installation, sécurité |
| ET-20 Registre des dépendances | Couvert | aucune dépendance tierce côté serveur (décision 006) |
| ET-21 Bibliothèque tierce isolée | Sans objet | aucune bibliothèque de diagrammes : le rendu est écrit dans l'outil |
| ET-22 Versions verrouillées, SBOM | Couvert | `package-lock.json` ; nomenclature CycloneDX régénérable |
| ET-23 Recherche de vulnérabilités | Partiel | faite à chaque livraison par le script, une faille critique la bloque ; le contrôle hebdomadaire est un geste d'exploitation. Au 14 novembre 2026 : aucune faille connue |
| ET-24 Mises à jour planifiées | À la DSI | une montée de version .NET ne demande aucune modification du code (décision 008) |
| ET-30 Hébergement interne | Couvert | aucune donnée ne sort |
| ET-31 Aucun flux vers l'OT | Couvert | |
| ET-32 Authentification unique, rôles, journalisation | Couvert, à valider | voir EF-50 |
| ET-33 Chiffrement, secrets, OWASP ASVS | Partiel | HTTPS par l'hébergement, secrets hors du code ; auto-évaluation seulement |
| ET-34 Homologation | À la DSI | |

## Données

| Exigence | État | Précision |
|---|---|---|
| ED-01 Export complet | Couvert | JSON : objets, relations, métamodèle, historique |
| ED-02 Export ArchiMate | Couvert | |
| ED-03 Identifiants stables | Couvert | |
| ED-04 Source maître, donnée synchronisée non modifiable | Couvert | l'écran grise le champ et nomme son maître ; le serveur refuse la modification |
| ED-05 Propriétaire nommé | Partiel | champ « Responsable », non obligatoire |
| ED-06 Test de réversibilité | Couvert | fait le 14 novembre 2026 : export complet, démarrage d'une instance vierge sur ce fichier, état identique |
| ED-07 Accès restreint aux données sensibles | Partiel | accès par rôle ; **tout lecteur voit tout le référentiel**, OT compris |
| ED-08 Clause SaaS | Sans objet | |

## Exploitation

| Exigence | État | Précision |
|---|---|---|
| EX-01, 02, 06, 07, 08 Organisation | À la DSI | |
| EX-03 Environnements séparés, déploiement reproductible | Partiel | scripts de livraison ; pas de chaîne automatisée |
| EX-04 Sauvegarde quotidienne, restauration testée | Couvert | sauvegardes de l'outil et procédure ; la sauvegarde du dossier par l'infrastructure reste à mettre en place |
| EX-05 Supervision | Partiel | sonde de disponibilité ; les rapprochements étant manuels, il n'y a pas d'échec de synchronisation à superviser |

## Recette technique demandée par le cahier des charges

| Contrôle | Résultat |
|---|---|
| Import rejoué sans doublon | vérifié par les tests, sur le modèle Archi réel |
| Export complet puis réimport | vérifié le 14 novembre 2026 |
| Restauration de sauvegarde | vérifiée dans un navigateur : la restauration rend l'état d'avant l'opération |
| Fichier piégé à l'import (draw.io, xlsx) | refusé au-delà de 50 Mo décompressés, plafond cumulé sur tout le fichier : vérifié par les tests sur des fichiers fabriqués (0.15.4) ; revue humaine à faire (ET-13) |
| Rapport de vulnérabilités | `npm audit` : aucune faille connue au 14 novembre 2026 ; serveur sans dépendance tierce |
