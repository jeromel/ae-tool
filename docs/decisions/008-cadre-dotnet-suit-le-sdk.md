# 008 — Le cadre .NET visé suit le SDK de construction

## Contexte

L'outil a été commencé sur .NET 8. Microsoft a mis fin au support de .NET 8 le 10 novembre 2026 : plus aucun correctif
de sécurité. La version maintenue sur le long terme est .NET 10 (jusqu'en novembre 2028) ; .NET 11, sortie en
novembre 2026, n'est maintenue que deux ans.

Deux contraintes pèsent sur le choix :

- la construction côté serveur ne doit rien télécharger (décision 006) : or viser un cadre autre que celui du SDK installé
  oblige le SDK à télécharger un paquet de ciblage ;
- les postes n'ont pas tous le même SDK. Celui de l'architecte porte les SDK 8 et 11, pas le 10.

## Décision

- **Le cadre visé est celui du SDK qui construit** : SDK 8 donne `net8.0`, SDK 10 donne `net10.0`, et ainsi de suite
  (`backend/Cadre.props`). `global.json` laisse choisir le SDK le plus récent installé.
- **Le langage est figé en C# 12**, quel que soit le SDK : le même code, lu de la même façon partout.
- **Les SDK 8 et 10 sont validés** : construction sans avertissement et tous les tests réussis sur les deux.
  Sur un SDK plus récent, la construction avertit que la version n'a pas été validée, les avertissements du compilateur ne
  bloquent plus, et les tests lancés par le script font foi.
- **Pour un serveur, retenir .NET 10** : c'est la seule version à la fois validée et maintenue.

## Conséquences

- Une montée de version du cadre ne demande aucune modification du code : installer le SDK, reconstruire, tester.
- Le serveur doit porter le runtime ASP.NET Core de la même version majeure que le SDK de construction.
- Un poste qui n'a que le SDK 8 construit encore l'outil, sur un cadre qui n'est plus maintenu : acceptable pour essayer,
  pas pour exposer l'outil sur le réseau.
- Un SDK non validé (11 et suivants) peut révéler une incompatibilité : le script de construction le dit et indique
  comment revenir au SDK 8 ou forcer un cadre.

## Écarté

- **Rester sur `net8.0`** : c'est livrer un outil neuf sur un cadre sans correctif de sécurité.
- **Viser `net10.0` en dur** : la construction échoue ou télécharge sur un poste qui n'a pas le SDK 10.
- **Viser plusieurs cadres à la fois** : double temps de construction et double livraison, sans utilité pour une
  application déployée à un seul endroit.
