# Installation

Trois façons d'installer l'outil, de la plus simple à la plus complète. Dans tous les cas : **un dossier pour le
programme, un autre pour les données**. Le premier se remplace à chaque mise à jour ; le second ne se remplace jamais.

## Ce qu'il faut

| Pour | Il faut |
|---|---|
| Construire | un SDK .NET (8, ou 10 recommandé) et Node.js 22.22.3 ou plus récent |
| Exécuter sur un serveur | le runtime ASP.NET Core de la même version majeure que le SDK de construction ; sous IIS, le « Hosting Bundle » |
| Utiliser | un navigateur récent (Edge, Chrome, Firefox) |

Aucun serveur de base de données, aucun autre composant. La construction ne télécharge rien côté serveur ; côté
interface, `npm ci` télécharge les paquets Angular depuis le registre npm.

Le cadre .NET visé suit le SDK utilisé : SDK 8 donne `net8.0`, SDK 10 donne `net10.0`. L'outil est validé sur ces deux
versions. Avec un SDK plus récent, la construction avertit que la version n'a pas été validée ; les tests lancés par le
script font foi. Pour forcer le SDK 8 s'il est installé : dans `global.json`, remplacer `latestMajor` par `latestFeature`.

## 1. Sur le poste de l'architecte (pilote)

```powershell
.\lancer.ps1          # Windows ; construit au premier lancement, puis démarre sur http://localhost:5080
```
```bash
./lancer.sh           # Linux, macOS, Git Bash
```

Les données vont dans le dossier `donnees`, à côté du code, jamais dans le dossier de compilation. Si une version
antérieure avait rangé un référentiel dans le dossier de compilation, il est repris automatiquement au premier lancement.
L'ancien format est migré à la première lecture ; l'original est conservé à côté (`referentiel.avant-migration-v1.json`).

Au premier lancement, l'outil demande de créer le compte administrateur. Le **code d'installation** demandé est affiché
dans la console et écrit dans `donnees\code-installation.txt` ; il ne sert qu'une fois.

## 2. Sur un serveur Windows, derrière IIS

1. Sur un poste de construction : `.\publier.ps1`. Le dossier `livraison\ae-tool` et l'archive
   `livraison\ae-tool-<version>.zip` ne contiennent aucune donnée.
2. Sur le serveur : installer le « ASP.NET Core Hosting Bundle » de la version majeure utilisée pour construire.
3. Déposer le contenu de l'archive dans un dossier, par exemple `D:\Applications\ae-tool`.
4. Créer le dossier de données, par exemple `D:\Donnees\ae-tool`, et y donner les droits de modification au seul compte
   du pool d'applications.
5. Dans IIS : un site (ou une application) pointant sur le dossier du programme, pool d'applications « Aucun code
   managé », liaison **HTTPS** avec le certificat de l'entreprise.
6. Régler l'outil par des variables d'environnement du pool d'applications (ou du système) :

   | Variable | Valeur |
   |---|---|
   | `AETOOL_DataPath` | `D:\Donnees\ae-tool\referentiel.json` |
   | `AETOOL_Auth__Mode` | `local` (défaut) ou `entra` |

7. Ouvrir l'adresse du site : l'écran de premier lancement demande le code d'installation, écrit dans
   `D:\Donnees\ae-tool\code-installation.txt`.

## 3. Sur un serveur Linux, derrière un mandataire inverse

```bash
./publier.sh                                   # produit livraison/ae-tool-<version>.tar.gz
sudo mkdir -p /opt/ae-tool /var/lib/ae-tool    # programme, données
sudo tar -xzf ae-tool-<version>.tar.gz -C /opt                  # crée /opt/ae-tool
sudo useradd --system --home /var/lib/ae-tool ae-tool && sudo chown -R ae-tool: /var/lib/ae-tool && sudo chmod 700 /var/lib/ae-tool
```

Unité systemd `/etc/systemd/system/ae-tool.service` :

```ini
[Unit]
Description=Référentiel d'architecture
After=network.target

[Service]
User=ae-tool
WorkingDirectory=/opt/ae-tool
ExecStart=/usr/bin/dotnet /opt/ae-tool/AeTool.Api.dll --urls http://127.0.0.1:5080
Environment=AETOOL_DataPath=/var/lib/ae-tool/referentiel.json
Environment=AETOOL_Proxy__Trusted__0=127.0.0.1
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Le mandataire (nginx, Apache, HAProxy) porte HTTPS et transmet à `127.0.0.1:5080` avec les en-têtes `X-Forwarded-For` et
`X-Forwarded-Proto`. **Déclarer son adresse dans `AETOOL_Proxy__Trusted__0`** : sans cela l'outil ignore ces en-têtes,
ne sait pas que la connexion d'origine est chiffrée, et le cookie de session n'est pas marqué « Secure ».

## Réglages

Chaque réglage se donne par variable d'environnement préfixée `AETOOL_` (les deux-points deviennent deux tirets bas), par
la ligne de commande (`--Auth:Mode=entra`), ou dans un fichier `appsettings.Production.json` du dossier du programme.
La ligne de commande l'emporte. Préférer les variables d'environnement : une mise à jour ne les efface pas.

| Réglage | Défaut | Rôle |
|---|---|---|
| `DataPath` | `data/referentiel.json` dans le dossier du programme | fichier du référentiel ; son dossier est le dossier de données. **À régler sur un serveur.** |
| `Auth:Mode` | `local` | `local` (comptes de l'outil), `entra` (Microsoft Entra ID), `ouvert` (aucune connexion ; refusé hors environnement de développement) |
| `Auth:Entra:TenantId`, `ClientId`, `ClientSecret` | | inscription d'application Entra ID (mode `entra`) |
| `Auth:Entra:Administrators:0`, `:1`… | | identifiants (UPN) qui deviennent administrateurs à leur première connexion |
| `Auth:Entra:DefaultRole` | `lecteur` | rôle des autres personnes à leur première connexion |
| `Proxy:Trusted:0`, `:1`… | aucun | adresses IP des mandataires inverses dont les en-têtes `X-Forwarded-*` sont crus |
| `Storage:BackupsToKeep` | 40 | sauvegardes manuelles et d'avant opération conservées |
| `Storage:DailyBackupsToKeep` | 30 | sauvegardes quotidiennes conservées |
| `urls` | `http://localhost:5000` | adresse d'écoute de Kestrel (sans objet sous IIS) |

Au démarrage, l'outil journalise sa version et son dossier de données, et **avertit** dans deux cas : le dossier de
données est dans le dossier du programme ; l'écoute est en HTTP sur une adresse du réseau sans mandataire déclaré.

### HTTPS sans IIS ni mandataire

Kestrel peut porter le certificat lui-même, par les réglages standard d'ASP.NET Core :

```
--urls https://0.0.0.0:443
AETOOL_Kestrel__Certificates__Default__Path=/chemin/certificat.pfx
AETOOL_Kestrel__Certificates__Default__Password=…
```

### Connexion par Microsoft Entra ID

1. Dans Entra ID, inscrire une application « web », avec l'adresse de redirection `https://<adresse de l'outil>/signin-entra`,
   une clé secrète, et l'autorisation déléguée `User.Read` de Microsoft Graph.
2. Régler `Auth:Mode=entra`, `Auth:Entra:TenantId`, `ClientId`, `ClientSecret`, et au moins un identifiant dans
   `Auth:Entra:Administrators`.
3. Le serveur doit pouvoir joindre `login.microsoftonline.com` et `graph.microsoft.com`.

Les rôles restent gérés dans l'outil : Entra ID dit qui est la personne, l'administrateur de l'outil dit ce qu'elle peut faire.
**Ce mode n'a pas pu être essayé sur un locataire réel pendant le développement : le valider en recette avant de s'y fier.**

## Mettre à jour

1. Sauvegarder (menu Administrer › Sauvegardes › « Sauvegarder maintenant »), ou copier le dossier de données.
2. Arrêter l'application.
3. Remplacer le contenu du dossier du programme par la nouvelle livraison. **Ne pas toucher au dossier de données.**
4. Redémarrer. Si le format des données a évolué, la migration se fait à la première lecture, après copie de l'original
   (`referentiel.avant-migration-v<n>.json`).
5. Vérifier `https://<adresse>/api/health` : la version annoncée est la nouvelle.

Revenir en arrière : remettre l'ancienne livraison **et** l'ancien fichier (la copie d'avant migration, renommée
`referentiel.json`). Une version ancienne ne sait pas lire un format plus récent.

## Vérifier l'installation

- `GET /api/health` répond `{"status":"ok","version":"…"}` sans connexion.
- La page d'accueil demande une connexion ; après connexion, Administrer › Sauvegardes montre le dossier de données attendu.
- Le journal de démarrage ne contient aucun des deux avertissements cités plus haut.
