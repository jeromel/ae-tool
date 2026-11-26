# 012 — Données sensibles : des personnes nommées, et un périmètre de lecture selon le besoin d'en connaître

**État : Acceptée** (26/11/2026).

## Contexte

L'exigence ED-07 demande un accès restreint aux données sensibles. Le référentiel décrit tout le SI, IT et OT : équipements
industriels, serveurs, réseaux, zones, flux qui traversent la frontière IT/OT, données classées du catalogue. Aujourd'hui,
l'accès se règle par rôle, et **tout lecteur voit tout le référentiel**.

La règle visée, établie le 26/11/2026, est le **besoin d'en connaître** :

- seules des personnes nommées et approuvées ont un compte ;
- les gens de l'informatique voient tout ;
- les responsables métier et les gens d'un service métier voient les processus et les applications de leur métier,
  mais pas l'infrastructure.

Constats sur le code :

- **en comptes locaux**, seul un administrateur crée un compte, et il n'y a pas d'inscription libre ;
- **en mode Entra ID**, toute personne du locataire qui se connecte reçoit un compte au rôle `Auth:Entra:DefaultRole`
  (`lecteur` par défaut), sans que personne l'ait approuvée (`AuthService.SignInEntra`). Elle voit tout, OT compris ;
- **toutes les lectures du référentiel passent par `Workspace.Read`** (43 appels). C'est là que l'état projeté d'un
  scénario est déjà substitué au référentiel, et tous les espaces, vues et analyses le reçoivent sans code propre ;
- **l'infrastructure est identifiable** : la couche `technique` (« Technique et infrastructure ») et le rôle fonctionnel
  `infrastructure`. Les équipements industriels (OT) portent ce rôle ;
- **l'appartenance d'un objet à un métier n'existe pas dans le modèle** : ni attribut de domaine, ni règle. Les dossiers
  d'Archi et la propriété `org.owner` n'en tiennent pas lieu de façon fiable.

## Décision

1. **Des personnes nommées.** En mode Entra ID, seules les personnes affectées à l'application dans Entra ID peuvent se
   connecter : affectation des utilisateurs obligatoire sur l'application d'entreprise, et un groupe de personnes
   approuvées. C'est un réglage d'Entra ID, pas du code. L'administrateur revoit la liste des comptes à chaque ouverture à
   une nouvelle population, et au moins une fois par an.
2. **Un périmètre de lecture par compte : « tout » ou « métier ».**
   - « Tout » est le périmètre de l'informatique, et celui de tout contributeur, valideur et administrateur.
   - « Métier » n'existe que pour le rôle lecteur. Il retire les objets de la couche `technique` et ceux qui portent le
     rôle `infrastructure`, les relations qui y aboutissent, et leurs formes dans les vues.
   - Un nouveau compte lecteur reçoit le périmètre « métier ». C'est l'administrateur qui ouvre « tout ».
3. **Le filtre est une projection du référentiel, appliquée dans `Workspace.Read`**, comme l'état projeté d'un scénario,
   et calculée une fois par périmètre et par version du référentiel. Un état projeté se filtre après projection. Les
   lectures qui ne passent pas par `Workspace.Read` (`ReadCurrent` : scénarios, trajectoires), le journal et les exports
   complets sont fermés au périmètre « métier ». Le filtre est dans le Core, sans dépendance.
4. **Le cloisonnement entre métiers est différé.** Il ne sera décidé que si le besoin reste réel une fois le point 2 en
   service, et que l'appartenance d'un objet à un métier a une définition que l'on sait tenir à jour (Haussman).

## Conséquences

- Point 1 : rien à coder. Le guide d'installation décrit le réglage d'Entra ID (Emmett), et `securite.md` l'ajoute à la
  liste de contrôle (Colbert).
- Points 2 et 3 : **3 à 4 jours** (Emmett). La moitié de ce temps sert aux tests de fuite : pour chaque espace, vue,
  analyse, recherche, parcours, export et état projeté, un compte « métier » ne reçoit aucun objet d'infrastructure, ni
  son identifiant, ni son nom. Un test vérifie aussi que les routes de lecture passent par `Workspace.Read` ou sont
  fermées au périmètre « métier », pour qu'une nouvelle route ne fuie pas sans qu'on le voie.
- Le périmètre est un champ du compte (`utilisateurs.json`). Ce n'est pas un nouveau rôle : la table d'habilitations
  reste seule à dire qui écrit quoi.
- Une analyse d'impact vue au périmètre « métier » s'arrête où commence l'infrastructure : elle ne dit pas qu'une
  application dépend d'un serveur. C'est le prix du besoin d'en connaître.
- Une projection par périmètre tient en mémoire, comme une projection de scénario. Avec deux périmètres, le coût est
  négligeable.
- Masquer un menu dans l'interface n'est pas une protection. C'est le serveur qui filtre ; l'écran peut aussi retirer les
  espaces vides pour le périmètre « métier » (Camille).
- Un cloisonnement entre métiers (point 4) coûterait au moins 3 à 5 jours de plus. Il coûterait surtout une tâche de
  données sans fin : rattacher chaque objet à un ou plusieurs métiers, traiter les objets partagés, décider de ce qu'un
  flux montre de l'autre métier, choisir le défaut pour un objet non rattaché.

## Écarté

- **Un filtrage de chaque route, l'une après l'autre** : 43 lectures à reprendre, et chaque nouvelle route à penser.
  La projection unique les couvre toutes.
- **Un nouveau rôle « lecteur métier »** : les rôles disent ce qu'on peut faire, le périmètre dit ce qu'on peut voir.
  Les mêler multiplie les rôles à chaque nouveau périmètre.
- **Une instance séparée pour l'infrastructure ou l'OT** : elle couperait les flux IT/OT que le catalogue doit montrer, et
  doublerait l'exploitation.
- **Le cloisonnement entre métiers dès maintenant** : voir le point 4.
