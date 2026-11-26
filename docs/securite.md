# Sécurité : auto-évaluation

Ce document dit ce que l'outil protège, comment, et ce qui reste à la charge de l'exploitant. C'est une auto-évaluation
faite pendant le développement : **elle ne remplace ni une homologation par la DSI et le RSSI, ni un test d'intrusion.**
Chaque mesure citée renvoie au code qui la porte et, quand il existe, au test qui la vérifie.

## 1. Ce qu'il y a à protéger

| Bien | Pourquoi il compte |
|---|---|
| Le référentiel | c'est la cartographie du SI, informatique et industriel : applications, flux, protocoles, serveurs, réseaux, zones. Entre de mauvaises mains, c'est un plan d'attaque. À traiter comme une information à diffusion restreinte. |
| Les comptes | un compte administrateur donne tout le référentiel et le droit de le modifier |
| Les secrets des sources externes | le compte de lecture de la CMDB |
| Le journal | il fonde la confiance dans le référentiel : qui a changé quoi |

Hypothèses du pilote : l'outil est sur le réseau interne, derrière HTTPS, pour quelques dizaines de personnes de la DSI.
**Il n'a aucune connexion vers les systèmes industriels** et n'écrit dans aucun système tiers.

## 2. Mesures en place

### Authentification

| Mesure | Détail | Code | Test |
|---|---|---|---|
| Mots de passe hachés | PBKDF2, HMAC-SHA512, 100 000 itérations, sel aléatoire de 128 bits par mot de passe (`PasswordHasher` d'ASP.NET Core) | `Auth/AuthService.cs` | `AuthTests` |
| Longueur minimale | 12 caractères, 128 au plus, différent de l'identifiant | `Auth/AuthService.cs` | `AuthTests` |
| Verrouillage | 5 échecs de suite bloquent le compte 15 minutes | `Auth/AuthService.cs` | `AuthTests` |
| Limitation de débit | 10 tentatives par minute et par adresse sur la connexion et le premier lancement | `Auth/AuthSetup.cs` | `AuthTests` |
| Pas d'indice sur l'existence d'un compte | même message et même temps de calcul pour un identifiant inconnu et pour un mot de passe faux | `Auth/AuthService.cs` | `AuthTests` |
| Premier lancement protégé | le premier compte ne se crée qu'avec un code à usage unique, lisible seulement sur le serveur | `Auth/SetupCode.cs` | `AuthTests` |
| Mot de passe temporaire | un compte créé ou réinitialisé doit changer son mot de passe avant toute autre action | `Auth/AuthSetup.cs` | `AuthTests` |
| Connexion d'entreprise | Microsoft Entra ID, flux « authorization code » avec PKCE ; le jeton d'accès n'est pas conservé | `Auth/AuthSetup.cs`, `Auth/EntraDirectory.cs` | transformations testées ; **flux réel non essayé** |

### Session et requêtes

| Mesure | Détail | Code |
|---|---|---|
| Cookie de session | `HttpOnly`, `SameSite=Strict`, `Secure` dès que la connexion est en HTTPS ; il disparaît à la fermeture du navigateur, et la session expire après 8 heures d'inactivité | `Auth/AuthSetup.cs` |
| Révocation immédiate | le compte est relu à chaque requête : désactivation ou changement de rôle prennent effet tout de suite | `Auth/AuthSetup.cs` |
| Falsification de requête (CSRF) | jeton exigé dans un en-tête sur toute écriture de l'API | `Auth/AuthSetup.cs` |
| En-têtes | politique de contenu `default-src 'self'` sans script en ligne, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: same-origin` ; HSTS sur toute réponse HTTPS | `Auth/AuthSetup.cs`, `Hosting.cs` |
| Mandataire inverse | les en-têtes `X-Forwarded-*` ne sont crus que venant des adresses déclarées | `Hosting.cs` |

### Habilitations

- Quatre rôles : lecteur, contributeur, valideur, administrateur.
- **Une seule table** fixe le rôle minimal de chaque route (`Auth/AccessRules.cs`, testée ligne à ligne par `AccessRulesTests`).
  Elle est appliquée à chaque requête, avant d'atteindre la route : une route ajoutée sans y être prévue est réservée
  aux administrateurs.
- Rien n'est lisible sans session, hormis la page de connexion et la sonde `/api/health`, qui ne dit rien du contenu.
- L'interface masque ce que le rôle n'autorise pas ; **le contrôle qui fait foi est celui du serveur.**
- Le circuit de validation ne se contourne pas : l'état d'un objet ne se change que par les routes dédiées, et une
  modification de fond par un contributeur renvoie un objet validé à l'état « proposé » (`Core/Validation.cs`, `ValidationTests`).

### Données reçues

| Entrée | Mesure | Code |
|---|---|---|
| Fichier Archi, classeur Excel | toute déclaration DOCTYPE est refusée, aucune ressource externe n'est résolue | `Connectors/SafeXml.cs` |
| Tout envoi de fichier | 100 Mo au plus ; 100 000 lignes au plus pour un CSV ou un classeur | `AppHost.cs`, `Connectors/Tabular.cs` |
| Corps des requêtes de connexion et de gestion des comptes | 16 ko au plus | `Auth/AuthSetup.cs` |
| Nom de sauvegarde à télécharger ou restaurer | comparé à un motif strict ; aucun chemin fourni n'est jamais ouvert | `Storage/BackupStore.cs` |
| Valeurs saisies | contrôlées par le serveur contre le métamodèle : type, liste de valeurs, couple source et cible d'une relation | `Core/Conformity.cs`, `Endpoints/ObjectEndpoints.cs` |
| Données maîtrisées ailleurs | nom, description et propriétés venus d'Archi ou d'une source externe : le serveur refuse de les modifier et les conserve si une demande les omet | `Core/Mastery.cs` |
| Affichage | Angular échappe tout texte affiché ; le code n'injecte jamais de HTML brut | interface |

Il n'y a ni base SQL ni interpréteur de commandes : les injections correspondantes sont sans objet.

### Secrets

- Le secret d'une source externe est chiffré au repos par la protection de données d'ASP.NET Core ; les clés sont dans
  `cles/`, elles-mêmes chiffrées par DPAPI sous Windows (`KeyStorage.cs`, `Sources/SourceStore.cs`).
- Il n'est **jamais renvoyé** par l'API, qui dit seulement s'il est défini, ni écrit dans un journal (`SourcesApiTests`).
- Le client API REST ne fait que des lectures, refuse une adresse en HTTP hors de la machine locale, ne suit aucune
  redirection ni aucun lien de page suivante vers un autre hôte (`Connectors/RestSourceClient.cs`, `RestSourceClientTests`).
- Le secret de l'application Entra ID se donne par variable d'environnement ; l'outil ne l'écrit nulle part.

### Traçabilité et intégrité

- Journal en ajout seul, écrit avant le référentiel, avec l'avant et l'après de chaque champ (`Storage/JournalFile.cs`, `Core/History.cs`).
- Connexions, connexions refusées sur un compte existant, et opérations sur les comptes y figurent ; jamais un mot de
  passe ni un hachage. Une tentative sur un identifiant inconnu n'est pas journalisée : ce champ reçoit trop souvent, par
  erreur, un mot de passe.
- Écritures atomiques : une coupure ne laisse jamais un fichier tronqué (`Storage/AtomicFile.cs`).
- Sauvegarde automatique avant toute opération risquée, restauration réversible (voir [exploitation.md](exploitation.md)).

### Chaîne logicielle

- Serveur : aucune dépendance en dehors du runtime .NET, corrigé par Microsoft (décision 006).
- Interface : les seuls paquets livrés sont ceux d'Angular et leurs dépendances directes, listés dans
  [nomenclature/interface.cdx.json](nomenclature/interface.cdx.json) (format CycloneDX), régénérable par
  `node outils/nomenclature.mjs` depuis `frontend/`.
- Aucune ressource n'est chargée depuis Internet par le navigateur : ni police, ni script, ni feuille de style.

## 3. Risques résiduels

| Risque | Niveau | Ce qu'il faut faire |
|---|---|---|
| Exposition en HTTP : mots de passe et sessions en clair | élevé si ignoré | **HTTPS obligatoire** hors du poste local ; l'outil avertit au démarrage s'il écoute en clair sur le réseau |
| Pas de second facteur en mode `local` | moyen | préférer le mode `entra`, qui hérite de l'authentification forte de l'entreprise ; sinon réserver l'accès au réseau interne |
| Mode `entra` jamais essayé sur un locataire réel | moyen | le valider en recette : connexion, rôle par défaut, administrateurs désignés, déconnexion |
| Données au repos non chiffrées par l'outil | moyen | chiffrer le volume (BitLocker, LUKS) et limiter les droits du dossier de données au compte de service |
| Sauvegardes de l'outil sur le même disque que les données | moyen | sauvegarde quotidienne du dossier entier par l'infrastructure, hors du serveur |
| Cadre .NET 8 non maintenu depuis le 10 novembre 2026 | moyen si utilisé | construire et héberger sur .NET 10 (décision 008) ; appliquer les correctifs mensuels du runtime |
| Pas de test d'intrusion ni de revue externe du code | moyen | à prévoir avant d'ouvrir l'outil au-delà de l'équipe d'architecture |
| Un administrateur peut tout exporter | accepté | peu d'administrateurs, nommés ; l'export complet est une lecture, il n'est pas journalisé |
| Journal modifiable par qui a la main sur le serveur | accepté | droits du dossier ; copie quotidienne hors du serveur |
| Décompression sans plafond (draw.io, xlsx) : un fichier piégé de quelques Mo pouvait saturer la mémoire ; l'import draw.io est ouvert au contributeur | **corrigé en 0.15.4** | `DecompressionBudget` : 50 Mo décompressés par fichier, cumulés sur toutes ses parties (pages, fichiers du classeur) ; 7 tests sur fichiers piégés (`DecompressionTests`) ; vérifié le 26/11/2026 |
| XML non compressé chargé en mémoire en entier (export Archi, dessin draw.io en clair) : jusqu'à 100 Mo reçus, plusieurs fois cette taille en mémoire | moyen, à mesurer | limite de 100 Mo par requête (`AppHost.cs`) ; mesurer la mémoire sur un fichier de 100 Mo avant ouverture hors de l'équipe d'architecture |
| Code produit par une IA, jamais relu par un humain (ET-13) | élevé | revue humaine des lots 1 à 3 (authentification, entrées hostiles, stockage) avant toute ouverture |
| Stockage par fichier, une seule instance | accepté pour le pilote | pas de haute disponibilité ; base SQL avant généralisation (décision 004) |
| Chaîne de construction npm | faible | les paquets de construction ne sont pas livrés ; construire sur un poste maîtrisé, avec `npm ci` (versions figées par `package-lock.json`) |

## 4. Avant la mise en service

- [ ] HTTPS en place ; aucun avertissement au démarrage.
- [ ] Dossier de données hors du dossier du programme, accessible au seul compte de service, sur un volume chiffré.
- [ ] Sauvegarde quotidienne du dossier de données par l'infrastructure ; un essai de restauration fait.
- [ ] Runtime .NET 10 à jour de ses correctifs.
- [ ] Mode d'authentification choisi ; s'il s'agit d'Entra ID, recette faite sur le locataire.
- [ ] Deux administrateurs nommés au moins, pour ne pas dépendre d'une seule personne.
- [ ] `code-installation.txt` absent du dossier de données (il disparaît à la création du premier compte).
- [ ] Sonde `/api/health` branchée sur la supervision.
- [x] Plafond de décompression en place à l'import draw.io et xlsx, avec un test sur un fichier piégé (0.15.4).
- [ ] Mémoire mesurée à l'import d'un XML non compressé de 100 Mo (export Archi, dessin draw.io).
- [ ] Revue humaine faite (ET-13) sur l'authentification, la lecture des fichiers reçus et le stockage.
- [ ] Avis du RSSI sur la sensibilité du référentiel et sur le compte de lecture de la CMDB.
