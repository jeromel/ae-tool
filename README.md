# Référentiel d'architecture d'entreprise

Un outil pour décrire le système d'information, informatique et industriel, et s'en servir : comprendre une couche,
préparer un comité, tenir les données à jour. Version 0.15.0, pilote.

- **Un métamodèle à vous.** Types, relations, propriétés et couches sont des données de l'outil, réglables à l'écran.
  ArchiMate est un format d'échange, relié au métamodèle par une **matrice de transcodification** que vous réglez aussi.
- **Archi reste l'atelier de dessin.** Import rejouable du modèle, export réimportable, aller-retour sans perte.
- **Des réponses, pas des dessins à entretenir.** Chaque espace part de questions (« quelles applications portent cette
  capacité ? ») et génère la vue depuis les relations.
- **Un référentiel tenu.** Saisie guidée par le métamodèle, circuit de validation, historique avant et après, journal,
  sauvegardes, rapprochement avec la CMDB.
- **Peu de choses à maintenir.** API .NET sans aucun paquet tiers, interface Angular sans autre bibliothèque, un dossier de
  fichiers pour tout stockage.

## Lancer

Il faut un SDK .NET (10 recommandé ; 8 et 10 sont validés) et Node.js 22.22.3 ou plus récent.

```powershell
.\lancer.ps1          # Windows
```
```bash
./lancer.sh           # Linux, macOS, Git Bash
```

Au premier lancement, le script construit l'outil et lance ses tests ; puis il le démarre sur http://localhost:5080.
Les données vont dans le dossier `donnees`. L'outil demande d'abord de créer le compte administrateur, avec le code
affiché dans la console.

Le cadre .NET visé suit le SDK installé le plus récent. Avec un SDK autre que 8 ou 10, le script prévient que la version
n'a pas été validée et laisse les tests trancher ; pour revenir au SDK 8 s'il est installé, remplacer `latestMajor` par
`latestFeature` dans `global.json` ([décision 008](docs/decisions/008-cadre-dotnet-suit-le-sdk.md)).

## Documentation

| Document | Pour |
|---|---|
| [Prise en main](docs/prise-en-main.md) | se servir de l'outil : imports, métamodèle et matrice, saisie, validation, rapprochement |
| [Installation](docs/installation.md) | poste de l'architecte, serveur Windows derrière IIS, Linux derrière un mandataire ; réglages ; mise à jour |
| [Exploitation](docs/exploitation.md) | dossier de données, sauvegardes, restauration, comptes, supervision, incidents |
| [Sécurité](docs/securite.md) | mesures en place, risques résiduels, liste de contrôle avant mise en service |
| [Architecture](docs/architecture.md) | l'outil décrit par couche, ses flux, son stockage, ses limites |
| [Couverture du cahier des charges](docs/couverture-cahier-des-charges.md) | l'état de chaque exigence, lot par lot |
| [Jeu de démonstration](exemples/hydreane/README.md) | Hydréane, entreprise fictive : modèle Archi, classeur et présentation, à importer pour essayer l'outil |
| [Décisions d'architecture](docs/decisions/README.md) | les huit choix structurants et ce qui a été écarté |
| [Nomenclature logicielle](docs/nomenclature/interface.cdx.json) | paquets livrés avec l'interface (CycloneDX) |

## Ce que fait l'outil

| Domaine | Fonctions |
|---|---|
| Métamodèle | types d'objets et de relations avec rôles fonctionnels et visuel, propriétés typées, couches, couples source → cible autorisés, fusion de types ; export et import en JSON |
| Transcodification ArchiMate | règles par type, règles conditionnelles sur une propriété, concept de repli et propriété marqueur pour les types sans équivalent ; simulation puis réapplication aux objets déjà importés |
| Échanges | import Archi rejouable avec rapport d'anomalies ; export du référentiel ou d'une vue ; reprise du classeur d'urbanisation ; export JSON complet et CSV |
| Comprendre | espaces Capacités, Applications, Flux, Données, Infrastructure, interrogés par questions ; vues Archi en rendu soigné, avec calques |
| Décider | portefeuille noté (criticité métier et critères C1 à C4), projets, impacts et adhérences, analyse d'impact amont et aval, matrices |
| Tenir à jour | création et modification d'objets et de relations selon le métamodèle ; validation (brouillon, proposé, validé) ; historique ; qualité et conformité au métamodèle |
| Sources externes | rapprochement en deux temps avec un export CSV ou Excel, ou avec une CMDB lue par son API REST, en lecture seule ; comptage des postes de travail par profil de poste |
| Données et flux | import du catalogue des données et des flux ; parcours d'une donnée (applications, flux applicatifs, chaîne technique, serveurs, partages, postes) ; contrôles de cohérence ; registre des données personnelles |
| Administration | comptes locaux ou Microsoft Entra ID, quatre rôles, journal, sauvegardes et restauration |

## Organisation du code

| Dossier | Rôle | Dépend de |
|---|---|---|
| `backend/src/AeTool.Core` | domaine pur : modèle, métamodèle, transcodification, analyses, notation, validation, historique, rapprochement | rien |
| `backend/src/AeTool.Connectors` | formats : Archi (XML), draw.io, classeur xlsx, CSV, client API REST générique | Core |
| `backend/src/AeTool.Api` | routes REST, sessions et habilitations, stockage, hébergement | Core, Connectors |
| `backend/tests/AeTool.Tests` | tests automatisés, exécutable console sans cadre de test | les trois |
| `frontend` | interface Angular : composants autonomes, signaux | l'API |

Règles tenues : le cœur ne connaît ni le web, ni le stockage, ni Archi ; un connecteur ne modifie le référentiel que par le
modèle du cœur ; le stockage est derrière `IRepositoryStore` ; une seule table fixe le rôle exigé par chaque route.

```bash
./build.sh                                              # tout construire et tester (build.ps1 sous Windows)
dotnet run --project backend/tests/AeTool.Tests         # les tests seuls
dotnet run --project backend/tests/AeTool.Tests -- --fichier chemin/vers/export-archi.xml   # contrôle sur un vrai modèle
./publier.sh                                            # livraison pour un serveur (publier.ps1 sous Windows)
```

## Ce qui n'est pas fait, ou pas vérifié

| Sujet | État |
|---|---|
| Connexion Microsoft Entra ID | écrite et testée hors ligne ; **jamais essayée sur un locataire réel** |
| Client API REST | générique (adresse, pagination et noms de champs réglables) et testé contre un serveur simulé ; **jamais essayé sur une API réelle**. Le rapprochement par fichier, lui, est vérifié de bout en bout |
| SDK .NET 11 et suivants | non validés : les tests du script de construction font foi |
| Base relationnelle | non : stockage par fichier, une seule instance ([décision 004](docs/decisions/004-stockage-fichier.md)) |
| Outil de gestion de projet | par export de fichier seulement |
| Édition graphique des diagrammes | non : les vues Archi sont en lecture seule, le dessin reste dans Archi |
| Scénarios et trajectoires de transformation | non commencé |
| Test d'intrusion, homologation | à faire avant toute ouverture au-delà de l'équipe d'architecture ([sécurité](docs/securite.md)) |
