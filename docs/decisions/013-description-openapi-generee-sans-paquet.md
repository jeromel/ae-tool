# 013 — La description OpenAPI est produite par l'outil à partir de ses routes, sans paquet

**État : Acceptée** (26/11/2026).

## Contexte

L'exigence ET-01 demande une description OpenAPI de l'API. Elle manque : la couverture du cahier des charges la note
« partiel ».

Faits mesurés le 26/11/2026 :

- l'API compte **121 routes** (54 lectures, 38 créations, 18 remplacements, 11 suppressions), toutes en API minimale ;
- **aucune ne déclare son type de réponse**, et environ **96 réponses sont des objets anonymes** (`new { … }`) ;
- les entrées sont des types nommés (`Dto.cs`) ;
- le générateur de Microsoft (`Microsoft.AspNetCore.OpenApi`) est un **paquet NuGet**, hors du cadre partagé :
  la décision 006 ne l'admet que par dérogation ;
- la table d'habilitations (`AccessRules`) fixe le rôle minimal de chaque route ; c'est une information utile à qui
  appelle l'API, et aucun générateur ne la connaît.

Un générateur, quel qu'il soit, ne peut pas décrire une réponse anonyme. Le paquet de Microsoft n'apporterait donc les
schémas de réponse qu'après avoir annoté les 121 routes et remplacé les objets anonymes par des types nommés.

## Décision

1. **L'outil produit lui-même sa description OpenAPI 3.1**, servie par `GET /api/openapi.json` (rôle lecteur), à partir de
   la liste des routes d'ASP.NET Core (`EndpointDataSource`) :
   - chemin, méthode, paramètres de route et de requête, lus sur la méthode de chaque route ;
   - corps de requête : le schéma du type nommé attendu ;
   - **rôle minimal de chaque route, lu dans `AccessRules`** : la description dit aussi qui a le droit d'appeler quoi ;
   - réponse : le schéma du type déclaré par `Produces<T>()` quand la route le déclare (métadonnée du cadre partagé),
     sinon « objet JSON ».
2. **Aucun paquet.** Le code tient dans le projet Api, sans toucher au Core.
3. **Les schémas de réponse arrivent route par route, sans chantier dédié** : toute route nouvelle ou reprise renvoie un
   type nommé et le déclare par `Produces<T>()`. Les routes existantes restent telles qu'elles sont tant qu'on n'y touche
   pas.
4. **Un test garantit que la description ne ment pas** : chaque route de l'application y figure, avec le rôle que donne
   `AccessRules`, et le document est un JSON valide au format OpenAPI 3.1.

5. **Cible (pas pour tout de suite) : l'API doit être simple à utiliser depuis Power BI ou une autre interface.** Elle ne
   s'ouvre pas en publiant les 121 routes de l'interface : elles suivent l'écran et changent avec lui. Le jour venu, une
   décision à part fixera :
   - **une petite API de lecture publique, versionnée** (`/api/v1/…`) : objets, relations, propriétés, vues, catalogue,
     en tables plates et paginées, toutes les réponses typées et décrites. C'est ce que Power BI sait lire. Elle passe par
     `Workspace.Read`, donc par le périmètre de lecture de la décision 012 ;
   - **une authentification hors navigateur** : la session par cookie et la protection CSRF ne conviennent pas à Power BI.
     Il faudra des jetons d'API par compte (hachés, révocables, journalisés, avec le rôle et le périmètre du compte), ou
     Entra ID pour un client applicatif. C'est une extension de la décision 003 ;
   - pour Power BI en service, l'accès au serveur passe par la passerelle de données de l'exploitation si l'outil n'est
     pas joignable depuis l'extérieur.
   Ordre de grandeur : 2 à 3 jours pour l'API de lecture, 2 à 3 jours pour les jetons, sans dépendance.

## Conséquences

- Coût : **1 à 1½ jour** (Emmett), test compris. Aucune maintenance par route : une route ajoutée est décrite d'office.
- ET-01 passe à « couvert ». La description est exacte dès le premier jour sur les chemins, les paramètres, les entrées et
  les droits. Elle reste pauvre sur les réponses, et s'enrichit au fil des reprises.
- Le schéma d'un type se calcule avec `JsonSchemaExporter` (`System.Text.Json`, cadre partagé), qui n'existe qu'à partir de
  .NET 9. Sous .NET 8, encore validé par la décision 008, les corps sont décrits comme « objet JSON ». Le jour où .NET 8 sort
  de la liste des cadres validés, cette réserve tombe.
- La liste des routes, administration comprise, devient lisible par tout compte. Elle ne donne aucun droit, et chaque route
  reste protégée par `AccessRules`. Si le RSSI juge cela trop bavard, il suffit de réserver la route à l'administrateur.
- Pas d'interface de consultation intégrée (Swagger UI) : ce serait une bibliothèque d'interface. Le fichier se lit dans
  n'importe quel outil OpenAPI.

## Écarté

- **`Microsoft.AspNetCore.OpenApi` par dérogation** : un paquet à suivre, pour un résultat identique tant que les 121 routes
  ne sont pas annotées (2 à 4 jours de plus pour le faire), et qui ignore les droits de `AccessRules`.
- **Un fichier OpenAPI écrit à la main** : il divergerait du code dès la première route ajoutée, et décrire 121 routes à la
  main coûte plus cher que de les lire.
- **Typer toutes les réponses maintenant** : 2 à 4 jours, et la cible (point 5) n'en a pas besoin : elle s'appuiera sur une
  petite API publique, pas sur les 121 routes de l'interface.
- **Publier les routes de l'interface comme API de la cible** : chaque évolution d'un écran casserait les rapports Power BI.
