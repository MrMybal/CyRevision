# Modèle de sécurité

## Menaces traitées

- ajout accidentel ou non autorisé d’un appareil ;
- réutilisation d’une invitation ;
- altération d’un bundle Git ou d’un objet LFS ;
- transaction publiée par un rôle en lecture seule ;
- prise de contrôle de l’installation Syncthing personnelle ;
- arrêt ou modification accidentelle d'un tunnel WireGuard personnel ;
- traversée de chemin lors d’une restauration ou d’un import.

## Contrôles

- jetons d’invitation aléatoires de 256 bits, hashés dans le store ;
- durée maximale de sept jours et consommation unique ;
- code à six chiffres transmis séparément ;
- identité ECDSA P-256 par appareil, clé privée protégée par les permissions du compte ;
- certificat d’adhésion signé par l’administrateur ;
- comparaison en temps constant des secrets ;
- SHA-256 des manifestes, bundles, blobs de backup et objets LFS ;
- `git bundle verify` avant import ;
- API Syncthing sur loopback et clé distincte par projet ;
- arrêt limité à l’objet `Process` lancé par CyRevision ;
- token Bearer pour l’API Linux.
- interface WireGuard dédiée `cyrev-*`, marqueur de propriété obligatoire avant arrêt et refus des collisions ;
- invitations VPN ECDSA limitées dans le temps, adresse et capacités signées, acceptation explicite par le propriétaire ;
- aucune route Internet complète créée par défaut et clé privée masquée dans l'interface ;

## Responsabilités d'exploitation

- utilisez TLS devant le serveur web dès qu’il n’est plus strictement local ;
- protégez `/var/lib/cyrevision`, les clés et le token serveur par les permissions du système ;
- ne transmettez pas l’invitation et son code sur le même canal ;
- révoquez rapidement une machine perdue ;
- considérez qu’un ancien membre conserve les données déjà reçues ;
- chiffrez les volumes de backup sensibles au repos.

Les réservations souples sont volontairement informatives. Elles ne servent jamais de contrôle d'accès et n'autorisent ou n'interdisent aucune écriture. Leur transport bénéficie de l'admission des pairs Syncthing, mais un membre disposant de l'écriture sur la zone d'échange peut modifier un marqueur ou son nom affiché. Les décisions de sécurité restent fondées sur les certificats, rôles et signatures des transactions Git, pas sur la présence Unreal.

Le rôle limite l’acceptation des transactions Git. En Sync de fichiers sans Git, un client modifié peut ignorer sa configuration locale `receiveonly`; utilisez un propriétaire en `sendonly`, des backups et une segmentation réseau lorsqu’une garantie plus forte est nécessaire.
