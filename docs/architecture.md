# Architecture CyRevision

## Modules

| Projet | Responsabilité |
| --- | --- |
| `CyRevision.Desktop` | Client Avalonia et orchestration locale |
| `CyRevision.Core` | Catalogue, profils, options et rétention |
| `CyRevision.Git` | Git CLI, LFS et transactions P2P signées |
| `CyRevision.RemoteBuild` | Snapshots de source, client distant, recettes autorisées, jobs et artefacts |
| `CyRevision.Build.Agent` | Agent headless Windows/Linux/macOS pour les compilations sur VPN |
| `CyRevision.Sync` | Processus Syncthing isolé, profils et API REST |
| `CyRevision.Backup` | Objets dédupliqués, manifestes et restauration |
| `CyRevision.Security` | Identités ECDSA, invitations, certificats et révocation |
| `CyRevision.Diff` | Comparaisons d’assets sans moteur |
| `CyRevision.Vpn` | Profils WireGuard, clés, configuration, tunnel possédé et admission VPN |
| `CyRevision.Discord` | Webhooks de salon, surveillance Git, messages groupés et points de contrôle anti-doublon |
| `CyRevision.Discord.Control` | Client de contrôle authentifié pour un agent Discord local ou distant |
| `CyRevision.Discord.Agent` | Processus autonome multi-projet exécutable à côté du client desktop |
| `CyRevision.Plugin.Abstractions` | Contrats stables et sans interface pour les extensions chargeables |
| `CyRevision.Plugin.Unreal` | Extension Unreal optionnelle : inspection, installateur et pont loopback authentifié |
| `CyRevision.Plugin.Perforce` | Extension Perforce optionnelle et isolée par projet : orchestration sûre du CLI officiel `p4` sans stockage d'identifiants |
| `CyRevision.Plugin.Jira` | Extension Jira Cloud optionnelle et isolée par projet : recherche de tâches en lecture seule et références dans les brouillons Git |
| `CyRevision.Plugin.ClickUp` | Extension ClickUp optionnelle et isolée par projet : recherche de tâches Workspace et références dans les brouillons Git |
| `CyRevision.Server` | Pair Linux, API, ordonnanceurs et dashboard web |
| `CyRevisionUnreal` | Plugin Editor autonome : révisions Git, présence et connexion facultative au client |

Les modules sont activés par des drapeaux indépendants. Git ne dépend pas de Sync ; Sync ne dépend pas de Git ; Backup peut être utilisé seul.

Le VPN est lui aussi indépendant des modes projet. Il transporte du trafic IP générique et ne donne par lui-même aucun droit Git ou Syncthing. Chaque profil choisit l'installation WireGuard du système ou le runtime officiel livré avec CyRevision. Le MVP adopte une étoile autour du nœud invitant, adaptée à un serveur, une CI ou un coordinateur Swarm permanent.

L'assistant réseau sépare le rôle client du rôle hôte. Un client ne crée aucune règle entrante. Un hôte ouvre uniquement son port UDP WireGuard ; Swarm et l'API de contrôle restent limités au sous-réseau VPN et ne sont jamais publiés sur le modem. Les règles portent des noms déterministes par projet, ce qui permet de les retirer sans toucher aux règles étrangères. macOS utilise le pare-feu applicatif officiel et reste guidé manuellement ; Windows Defender, UFW et firewalld peuvent appliquer le plan après confirmation et élévation explicites.

Le profil Swarm est distinct du profil WireGuard, mais référence obligatoirement une adresse du même sous-réseau. La configuration XML est conservatrice : un fichier existant est requis, une sauvegarde horodatée est créée et aucun nœud inconnu n'est inventé. L'alias DNS Windows occupe un bloc `hosts` déterministe, remplaçable et supprimable sans toucher aux autres lignes.

Le transfert de fichiers VPN est un protocole applicatif borné, pas un serveur de fichiers générique. Le listener s'attache à une seule adresse WireGuard, filtre l'adresse source selon le CIDR, puis exige un secret de projet. Les uploads vont dans une inbox non destructive ; les downloads ne peuvent lire que sous une racine explicite après validation canonique du chemin et refus des reparse points. Chaque fichier est validé par taille et SHA-256 avant publication atomique.

Le gestionnaire LFS s'appuie sur `lfs.storage` local au dépôt. Une relocalisation copie et vérifie chaque objet avant de changer la configuration ; un marqueur de propriété interdit le partage accidentel d'un même store entre plusieurs dépôts. L'analyse protège toutes les références locales, index, stashes et worktrees. Un objet orphelin ne devient supprimable qu'avec le nombre configuré de preuves remote, pair signé ou archive SHA-256.

L'agent de build est un processus facultatif distinct. Sa configuration locale est l'autorité : le contrôleur choisit uniquement un identifiant de recette et ne transmet jamais une commande. Le mode workspace utilise un projet déjà synchronisé ; le mode snapshot extrait une archive sans `.git` dans un dossier de job isolé. Seuls les motifs d'artefacts autorisés par la recette sont retournés.

Le dossier d'échange Sync peut transporter des invitations et réponses VPN. Ces objets sont déjà signés par les identités des appareils. Une enveloppe supplémentaire vérifie le SHA-256 et la cohérence des métadonnées ; les champs ressemblant à une clé privée, un token, un webhook ou un autre secret sont refusés. Charger un message ne rejoint jamais automatiquement le VPN : l'acceptation reste une action distincte.

L'agent Discord peut vivre dans l'application desktop ou dans un processus autonome. Le module de contrôle ne renvoie jamais le webhook au client : il transmet les changements au sidecar par une API Bearer liée au loopback par défaut. Une exposition distante exige HTTPS, ou une autorisation explicite pour une adresse privée transportée par un VPN WireGuard de confiance.

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
- le plugin Editor Unreal fournit des outils Git autonomes, la présence non bloquante et un pont externe ; un fournisseur Source Control natif complet reste un module séparé ;
- la direction `ReadOnly` du mode Sync sans Git repose aussi sur la configuration du client Syncthing distant ; Git, lui, vérifie le rôle cryptographiquement lors de l’import.
