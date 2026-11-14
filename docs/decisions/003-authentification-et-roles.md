# 003 — Comptes locaux ou Entra ID ; les habilitations sont gérées dans l'outil

## Contexte

Le cahier des charges demande une authentification par l'annuaire d'entreprise et quatre rôles (EF-50). Le prototype
n'en avait aucune. Deux usages doivent être servis : un architecte qui installe l'outil sur son poste pour s'en servir
tout de suite, et un pilote puis un déploiement où l'identité vient de Microsoft Entra ID.

## Décision

1. **Trois modes d'authentification**, choisis par configuration :
   - `local` (par défaut) : comptes de l'outil, mots de passe hachés par le composant du cadre .NET, verrouillage après
     cinq échecs ;
   - `entra` : Microsoft Entra ID, par le flux OAuth 2.0 « authorization code » avec PKCE ; l'identité est lue auprès de
     Microsoft Graph avec le jeton obtenu ;
   - `ouvert` : sans authentification, accepté seulement en environnement de développement.
2. **La session est un cookie** (HttpOnly, SameSite strict), les écritures exigent un jeton antifalsification.
3. **Quatre rôles hiérarchisés** : lecteur, contributeur, valideur, administrateur. L'annuaire authentifie, **l'outil
   habilite** : le rôle de chaque compte est géré dans l'outil par un administrateur, quel que soit le mode.
4. **Les habilitations sont une table** (méthode et chemin → rôle minimal), appliquée en un seul point. Une écriture sur
   un chemin non prévu exige le rôle administrateur.
5. **Premier lancement protégé** : sans compte, un code d'installation à usage unique, visible seulement de celui qui a
   accès au serveur, est exigé pour créer le premier administrateur.

## Conséquences

- L'outil est utilisable immédiatement, sans dépendre d'une inscription d'application dans Entra ID.
- Le passage à Entra ID est un changement de configuration ; les rôles déjà attribués restent valables si les
  identifiants concordent.
- Les comptes et leurs hachages sont dans un fichier séparé du référentiel : aucun export du référentiel n'en contient.
- La table des habilitations se lit et se teste d'un bloc ; elle sert de base à la revue de sécurité.

## Limites connues

- Le mode Entra ID n'a pas pu être essayé contre un vrai tenant pendant le développement. Il doit être recetté avant tout
  usage, avec l'équipe qui gère Entra ID.
- Les rôles ne sont pas déduits des groupes de l'annuaire. C'est un choix de simplicité pour le pilote ; une
  correspondance groupe → rôle pourra s'ajouter sans remettre en cause le reste.
- Les habilitations portent sur les fonctions, pas sur un périmètre de données (pas de cloisonnement par domaine).

## Écarté

- **Les bibliothèques OpenID Connect de Microsoft** : ce sont des paquets NuGet, contraires à la décision 006 et
  indisponibles dans l'environnement de développement. Le flux OAuth générique du cadre partagé couvre le besoin.
- **L'authentification Windows intégrée** : liée à un hébergement IIS dans le domaine, elle ne couvre ni le poste isolé ni
  un hébergement hors domaine.
