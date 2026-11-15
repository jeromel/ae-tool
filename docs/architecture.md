# Dossier d'architecture de l'outil

Version décrite : 0.9.0 (pilote). Ce document dit ce qu'est l'outil, comment il est construit et pourquoi.
Les choix structurants sont détaillés dans [les décisions d'architecture](decisions/README.md).

## 1. Couche métier : à quoi sert l'outil

L'outil est le **référentiel d'architecture** de la DSI : un seul endroit où sont décrits les objets du système
d'information, informatique et industriel, et leurs relations. Il sert trois intentions, qui structurent le menu.

| Intention | Qui | Ce que l'outil apporte |
|---|---|---|
| Comprendre | architectes, chefs de projet, exploitants | une lecture par couche : capacités, applications, flux, données, infrastructure |
| Décider | comité d'architecture, comité de portefeuille | portefeuille applicatif noté, fiches projet, adhérences, analyse d'impact |
| Tenir à jour | architecte d'entreprise, contributeurs, valideurs | saisie guidée, circuit de validation, contrôles de qualité, imports et rapprochements |

Processus de gouvernance outillés :

- **Instruction d'un projet** : périmètre et impacts du projet sur les objets du référentiel, adhérences entre projets.
- **Revue de portefeuille** : notation des applications (criticité métier et critères C1 à C4), décision retenue.
- **Tenue du référentiel** : un contributeur propose, un valideur valide ou refuse avec un commentaire ; toute
  modification est historisée (qui, quand, avant, après).

## 2. Couche fonctionnelle : les blocs de l'outil

| Bloc | Fonctions |
|---|---|
| Référentiel | objets, relations, propriétés ; recherche ; archivage sans suppression |
| Métamodèle | types, relations, propriétés typées, couches, rôles fonctionnels ; matrice de transcodification ArchiMate |
| Espaces de lecture | vues générées par question (« quelles applications portent cette capacité ? ») ; vues Archi importées |
| Analyses | impact amont et aval, matrices, adhérences entre projets, conformité au métamodèle, qualité |
| Portefeuille | notation, synthèse, décisions |
| Échanges | import et export Archi (format d'échange Open Group), reprise du classeur d'urbanisation, export CSV |
| Sources externes | rapprochement avec un export de fichier ou la CMDB EasyVista : comparer, puis appliquer des décisions |
| Validation et historique | états brouillon, proposé, validé ; journal filtrable ; avant et après de chaque modification |
| Administration | comptes et rôles, sauvegardes et restauration, état du stockage |

Deux principes tiennent l'ensemble :

- **Les cas d'usage se branchent sur des rôles, pas sur des types.** L'espace « Applications » lit les types qui portent
  le rôle `application`, quel que soit leur nom. Renommer ou ajouter un type ne casse aucun écran (décision 002).
- **Une donnée a un seul maître.** Ce qui vient d'Archi se modifie dans Archi, ce qui vient d'une source externe se
  modifie dans cette source ; l'outil le dit sur chaque champ en lecture seule, et le serveur refuse la modification.

## 3. Couche applicative : les composants

```
Navigateur ── HTTPS ──► API REST (.NET, un seul processus) ──► dossier de données
   interface Angular        │
   (fichiers statiques      ├── AeTool.Core        domaine pur : modèle, métamodèle, transcodification, analyses,
    servis par l'API)       │                      notation, validation, historique, rapprochement
                            ├── AeTool.Connectors  formats : Archi (XML), classeur xlsx, CSV, client EasyVista
                            └── AeTool.Api         routes, sessions et habilitations, stockage, hébergement
```

- **AeTool.Core** ne connaît ni le web, ni le stockage, ni Archi. Toute règle métier y est testable sans serveur.
- **AeTool.Connectors** traduit des formats ; il ne modifie le référentiel que par le modèle du cœur.
- **AeTool.Api** assemble : une classe de routes par domaine (`Endpoints/`), la table des habilitations (`Auth/AccessRules`),
  le stockage derrière l'interface `IRepositoryStore` (`Storage/`).
- **L'interface** (Angular, composants autonomes et signaux) n'a pas d'autre accès aux données que l'API.

Aucune dépendance tierce côté serveur : seul le cadre .NET est requis (décision 006). Côté interface, seuls les paquets
Angular et leurs dépendances directes sont livrés ; leur liste est dans [nomenclature/interface.cdx.json](nomenclature/interface.cdx.json).

### Flux avec l'extérieur

| Flux | Sens | Déclenchement | Remarque |
|---|---|---|---|
| Fichier Archi (XML) | entrant et sortant | manuel, par un administrateur | la matrice de transcodification fait la correspondance |
| Classeur d'urbanisation (xlsx) | entrant | manuel | rapproché par nom avec les applications |
| Export de fichier d'une source (CSV, xlsx) | entrant | manuel | rapprochement en deux temps : comparer, puis appliquer |
| API REST EasyVista | sortant, **lecture seule** | manuel | seul flux sortant vers un système de l'entreprise ; aucun appel d'écriture n'existe dans le code |
| Microsoft Entra ID et Graph | sortant | à la connexion, si le mode « entra » est choisi | lecture de l'identité de la personne qui se connecte |

L'outil n'appelle aucun service sur Internet en dehors de ces deux derniers flux, tous deux facultatifs.
**Il ne touche à aucun système industriel** : la description de l'OT entre par Archi ou par un export de fichier.

## 4. Couche données

### Le modèle

- **Objet** : identifiant, type du métamodèle, nom, description, responsable, propriétés, source (`archi`, `urba`,
  `saisie` ou la clé d'une source externe), concept ArchiMate d'origine, liens vers les sources externes, état de
  validation et ses auteurs.
- **Relation** : type, source, cible, nom, propriétés, concept ArchiMate d'origine.
- **Vue** : diagramme importé d'Archi (nœuds, liens, géométrie).
- **Projet, impact, notation** : rattachés aux objets par leur identifiant.
- **Métamodèle** : couches, types d'objets et de relations (avec rôles et visuel), définitions de propriétés, matrice de
  transcodification, grille de notation, réglages. C'est une donnée du référentiel : il s'exporte et s'importe.
- **Journal** : une entrée par écriture, avec la cible et les champs modifiés (avant, après).

### Le stockage

Tout tient dans **un dossier de données**, hors du dossier du programme (décision 004) :

| Fichier | Contenu | Couvert par les sauvegardes de l'outil |
|---|---|---|
| `referentiel.json` | objets, relations, vues, projets, notations, métamodèle | oui |
| `journal.jsonl` | journal, une entrée par ligne, en ajout seul | oui |
| `sauvegardes/` | copies compressées datées | (ce sont les sauvegardes) |
| `utilisateurs.json` | comptes, rôles, mots de passe hachés | non : à sauvegarder avec le dossier |
| `sources.json`, `rapprochement-*.json` | profils des sources externes (secret chiffré), dernier rapport | non |
| `cles/` | clés de chiffrement de l'application | non |

Le référentiel est chargé en mémoire au démarrage et réécrit, de façon sûre, à chaque modification. Une version de schéma
est inscrite dans le fichier ; une version plus ancienne est migrée à la lecture, après copie de l'original.
**Une seule instance de l'application écrit dans un dossier donné.**

## 5. Couche technique

- **Exécution** : un processus .NET (Kestrel). Cadres validés : .NET 8 et .NET 10 ; .NET 10 est à retenir pour un
  serveur, .NET 8 n'étant plus maintenu depuis novembre 2026.
- **Hébergement** : poste de l'architecte (pilote), ou serveur Windows derrière IIS, ou Linux derrière un mandataire
  inverse. HTTPS est porté par IIS, par le mandataire, ou par Kestrel avec un certificat. Voir [installation.md](installation.md).
- **Dimensionnement** : un référentiel réel de taille moyenne (environ 1 800 objets, 1 650 relations, 130 vues) occupe environ 6 Mo sur disque et
  quelques dizaines de Mo en mémoire. Un serveur modeste suffit (1 processeur, 1 Go).
- **Supervision** : `GET /api/health` (sans session) ; journaux sur la sortie standard et, sous Windows, dans
  l'observateur d'événements.

## 6. Sécurité

Résumé ; le détail et les risques résiduels sont dans [securite.md](securite.md).

- Authentification locale (mots de passe hachés, verrouillage après cinq échecs) ou Microsoft Entra ID.
- Quatre rôles gérés dans l'outil : lecteur, contributeur, valideur, administrateur. Une table unique fixe le rôle
  minimal de chaque route ; une écriture inconnue de la table exige un administrateur.
- Session par cookie, protection contre la falsification de requête, en-têtes de sécurité, politique de contenu stricte.
- Secrets des sources externes chiffrés au repos ; jamais renvoyés par l'API ni écrits dans les journaux.
- Journal en ajout seul de toutes les écritures et des événements de comptes.

## 7. Limites connues et trajectoire

| Limite | Conséquence | Trajectoire |
|---|---|---|
| Stockage par fichier, une seule instance | pas de haute disponibilité ; convient à quelques dizaines d'utilisateurs | implémentation SQL de `IRepositoryStore` avant généralisation (décision 004) |
| Connexion Entra ID et client EasyVista non essayés sur les plateformes réelles | à valider en recette avec la DSI | essai sur le locataire et sur la préproduction EasyVista |
| Vues Archi importées en lecture seule | le dessin reste dans Archi | vues générées par cas d'usage, déjà engagées (décision 007) |
| Pas d'interface de programmation documentée pour des tiers | les intégrations passent par les fichiers | contrat d'API publié si un besoin apparaît |
| Rapprochement par nom | des homonymes demandent un choix manuel | clés de rapprochement supplémentaires par source |
