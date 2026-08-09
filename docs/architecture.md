# Architecture CyRevision

## Modules

| Projet | Responsabilité |
| --- | --- |
| `CyRevision.Desktop` | Client Avalonia et orchestration locale |
| `CyRevision.Core` | Catalogue, profils, options et rétention |
| `CyRevision.Git` | Git CLI, LFS et transactions P2P signées |
| `CyRevision.Sync` | Processus Syncthing isolé, profils et API REST |
| `CyRevision.Backup` | Objets dédupliqués, manifestes et restauration |
| `CyRevision.Security` | Identités ECDSA, invitations, certificats et révocation |
| `CyRevision.Diff` | Comparaisons d’assets sans moteur |
| `CyRevision.Vpn` | Profils WireGuard, clés, configuration, tunnel possédé et admission VPN |
| `CyRevision.Server` | Pair Linux, API, ordonnanceurs et dashboard web |
| `CyRevisionUnreal` | Menus Content Browser, fenêtre de présence et pont vers le client |

Les modules sont activés par des drapeaux indépendants. Git ne dépend pas de Sync ; Sync ne dépend pas de Git ; Backup peut être utilisé seul.

Le VPN est lui aussi indépendant des modes projet. Il transporte du trafic IP générique et ne donne par lui-même aucun droit Git ou Syncthing. Le MVP adopte une étoile autour du nœud invitant, adaptée à un serveur, une CI ou un coordinateur Swarm permanent.

## Git + Sync

Synchroniser un `.git` vivant avec un outil de fichiers est dangereux : plusieurs machines peuvent modifier simultanément les références, l’index ou les packs. CyRevision utilise donc une zone d’échange distincte.

1. Git crée un bundle contenant les branches locales.
2. Le manifeste contient le projet, l’auteur, les références et le SHA-256 du bundle.
3. L’appareil signe ce manifeste avec son identité ECDSA.
4. Les objets Git LFS sont copiés dans un magasin immuable adressé par leur SHA-256.
5. Syncthing transfère la zone d’échange, jamais `.git`.
6. Le destinataire vérifie l’adhésion, le rôle, la signature, le hash et `git bundle verify`.
7. Les branches sont importées sous `refs/remotes/cyrevision/<appareil>/...`.
8. L’utilisateur décide ensuite de créer une branche locale ou de faire un merge.

Les rôles `ReadOnly`, `Backup` et `EncryptedArchive` ne sont pas autorisés à publier des transactions Git. Les transactions déjà reçues restent disponibles après une révocation ; les nouvelles ne sont plus acceptées.

## Sync sans Git

Le dossier de travail est directement géré par Syncthing. Si Backup est actif, CyRevision crée en parallèle des snapshots externes et configure la conservation simple de Syncthing. Pour les données sensibles, la sécurité d'accès au système d'exploitation et au réseau reste indispensable.

## Réservations souples

Une réservation souple est un marqueur de présence, jamais un verrou. Le plugin écrit un fichier JSON indépendant par couple utilisateur/asset dans `presence/reservations`. Cette granularité évite le fichier central concurrent et permet à plusieurs personnes de signaler le même asset.

Le marqueur contient le projet, le package Unreal, le chemin relatif, l'utilisateur, la machine et trois dates UTC. Unreal renouvelle les marqueurs de l'utilisateur chaque minute. Après expiration ils restent visibles comme obsolètes jusqu'au nettoyage, sans bloquer le travail. En Git + Sync, `presence` réside dans la zone d'échange hors dépôt ; en Sync sans Git, il réside dans `.cyrevision/presence` au sein du dossier partagé.

## Instance Syncthing isolée

Chaque profil utilise :

- des dossiers `config` et `data` privés et non chevauchants ;
- une identité Syncthing propre ;
- une API HTTP sur loopback avec clé aléatoire ;
- un port local mémorisé par projet ;
- `--no-browser`, `--no-restart` et `--no-upgrade` ;
- une référence directe au processus enfant possédé.

Le client suit les options officielles `--config`, `--data` et `--gui-address`. Syncthing expose son contrôle par API sur le port de son interface locale ; CyRevision utilise la clé dédiée dans l’en-tête `X-API-Key`.

Références : [commande Syncthing](https://docs.syncthing.net/users/syncthing.html), [API REST](https://docs.syncthing.net/dev/rest.html) et [versioning](https://docs.syncthing.net/users/versioning.html).

## Backup

Un snapshot contient un manifeste JSON trié et des références vers des objets SHA-256. Un fichier identique à une version déjà stockée n’est pas recopié. Le store peut être un disque local, un partage NAS, un volume monté par `rclone` ou un stockage objet exposé comme système de fichiers.

`.git` et les objets LFS sont inclus ; les caches de compilation (`Intermediate`, `Saved`, `DerivedDataCache`, `bin`, `obj`, etc.) sont exclus par défaut. La restauration d’un snapshot complet refuse d’écraser les fichiers existants.

## Limites assumées

- un graphe Blueprint parfaitement fidèle nécessite le plugin Unreal et les sérialiseurs du moteur ; le client externe fournit une analyse structurale simplifiée ;
- le plugin Unreal fournit le pont externe et la présence non bloquante ; un fournisseur Source Control natif complet reste un module séparé ;
- la direction `ReadOnly` du mode Sync sans Git repose aussi sur la configuration du client Syncthing distant ; Git, lui, vérifie le rôle cryptographiquement lors de l’import.
