# Outil d'architecture d'entreprise – prototype du lot 1

Référentiel d'architecture paramétrable : import d'un modèle Archi, reprise du classeur d'urbanisation,
portefeuille applicatif noté selon la méthode CM + C1 à C4, fiches projet, adhérences, matrices, analyse d'impact.

Backend .NET 8 (API REST), interface Angular 22, **aucune dépendance NuGet** et, côté interface, uniquement les paquets `@angular/*`.

## Lancer

Prérequis : SDK .NET 8. Node.js 22 seulement pour reconstruire l'interface (une version compilée est fournie dans `wwwroot`).

```bash
dotnet run --project backend/src/AeTool.Api -c Release --urls http://localhost:5080
```

Ouvrir http://localhost:5080, puis **Imports et exports** :

1. importer l'export Archi (Fichier › Exporter › Modèle au format d'échange Open Group) ;
2. importer ensuite le classeur d'urbanisation (.xlsx), rapproché par nom avec les applications du modèle.

Le référentiel est enregistré dans un fichier JSON (`data/referentiel.json` à côté de l'exécutable, ou le chemin donné par `DataPath`).

Tout reconstruire et tester : `./build.sh`. Tests seuls :

```bash
dotnet run --project backend/tests/AeTool.Tests
dotnet run --project backend/tests/AeTool.Tests -- --fichier chemin/vers/export-archi.xml   # contrôle sur un vrai modèle
```

## Organisation du code

| Dossier | Rôle | Dépend de |
| --- | --- | --- |
| `backend/src/AeTool.Core` | Modèle du référentiel, métamodèle, notation, analyses | rien |
| `backend/src/AeTool.Connectors` | Format d'échange ArchiMate (import, export), classeur d'urbanisation, lecteur xlsx | Core |
| `backend/src/AeTool.Api` | Routes REST, stockage, service de l'interface | Core, Connectors |
| `backend/tests/AeTool.Tests` | Tests automatisés (exécutable console, sans framework) | Core, Connectors |
| `frontend` | Interface Angular (composants autonomes, signaux) | API |

Règles tenues : le cœur ne connaît ni le web, ni le stockage, ni Archi ; un connecteur ne modifie le référentiel
que par le modèle du cœur ; le stockage est derrière l'interface `IRepositoryStore`.

## Ce que fait le prototype

- **Métamodèle paramétrable** : couches, types, relations, propriétés, visuels, grille de notation et seuils sont des données,
  exportables et importables en JSON. Gabarit par défaut ArchiMate 3.
- **Import Archi rejouable** : objets, relations (y compris relation vers relation), propriétés, dossiers, vues, positions, couleurs.
  Clé = identifiant XML ; les propriétés sont rattachées par leur nom, car Archi renumérote les `propid-N` à chaque export.
  Réimport du même modèle : mises à jour détectées, objets disparus archivés, données saisies dans l'outil conservées.
- **Contrôles d'import** : identifiant absent ou en double, type inconnu, source ou cible manquante, occurrence graphique orpheline,
  définition de propriété absente, relation non conforme au métamodèle. Signalés dans un rapport, jamais corrigés en silence.
- **Export ArchiMate** du référentiel ou d'une seule vue, réimportable dans Archi ; export JSON complet ; export CSV.
- **Reprise du classeur d'urbanisation** : applications, grille des preuves, seuils, preuves. Les colonnes calculées
  ne sont pas reprises : l'outil recalcule notes, maîtrise, quadrant, règle de garde, priorité et surcouches C3 / C4.
- **Projets** : impacts déduits des vues rangées dans `Projets / <nom du projet>`, à qualifier (création, évolution,
  remplacement, retrait, hors périmètre) ; matrice d'adhérences avec les objets partagés.
- **Analyses** : impact amont et aval à profondeur réglable, matrice entre deux types (relation ou imbrication dans une vue),
  qualité des données par type.
- **Vues** : affichage SVG des diagrammes importés, avec les couleurs d'Archi ou celles du métamodèle (type, puis élément).
- **Journal** des opérations.

## Ce qu'il ne fait pas encore

| Sujet | État | Lot du cahier des charges |
| --- | --- | --- |
| Base relationnelle | Fichier JSON derrière `IRepositoryStore` ; implémentation SQL à écrire | Lot 1 (ET-04) |
| Authentification, rôles | Aucune ; l'en-tête `X-User` alimente le journal | Lot 1 (EF-50) |
| Circuit de validation par objet | Statut stocké, pas de circuit | Lot 1 (EF-51) |
| Édition du métamodèle | Libellé, couche et visuel des types à l'écran ; le reste par import JSON ou API | Lot 1 |
| Connecteurs EasyVista et outil de gestion de projet | Non commencé | Lot 2 |
| Édition graphique des diagrammes | Lecture seule | Lot 3 |
| Scénarios et steps de transition | Non commencé | Lot 4 |

Avant toute mise en service : authentification, base SQL, sauvegarde, homologation DSI.

## Choix à connaître

- **Impacts déduits** : tout objet présent dans une vue du dossier projet est proposé comme impacté, sauf les types
  « fond de carte » (capacités, regroupements, lieux ; réglage `impactExcludedTypeKeys` du métamodèle).
  Tant qu'ils ne sont pas qualifiés, les adhérences sont surestimées.
- **Sens de l'analyse d'impact** : « aval » suit le sens des relations, « amont » le remonte.
- **Preuves** : une preuve chiffrée sans référence n'est pas retenue, et « NC » bloque la note, comme dans le classeur.
- **Types de couche** : `Grouping` et `ApplicationFunction` sont rangés en couche fonctionnelle, `BusinessObject` et `DataObject` en couche données.
