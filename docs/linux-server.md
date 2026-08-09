# Serveur Linux optionnel

Le serveur apporte un pair disponible en continu, un magasin de backup et un dashboard web. Aucun client n’en dépend pour travailler localement.

## Docker Compose

```bash
cd deploy
cp .env.example .env
# Remplacer le token dans .env
docker compose up -d --build
```

Le dashboard écoute sur `http://serveur:8080`. Placez Caddy, Traefik ou Nginx avec HTTPS devant ce port sur un réseau non fiable.

Les données résident dans le volume `/var/lib/cyrevision`. L’image contient Git, Git LFS, Syncthing et les outils WireGuard. Dans l’API ou le client, le chemin Syncthing du conteneur est `/usr/bin/syncthing`.

Le VPN reste facultatif. Le compose principal n'accorde aucun privilège réseau. Pour transformer le serveur en pair WireGuard, utilisez aussi `docker-compose.vpn.yml`, qui ajoute uniquement `NET_ADMIN` :

```bash
docker compose -f docker-compose.yml -f docker-compose.vpn.yml up -d --build
```

## systemd

1. publiez le serveur avec `scripts/publish.sh` puis copiez le résultat dans `/opt/cyrevision` ;
2. créez l’utilisateur `cyrevision` et `/var/lib/cyrevision` ;
3. installez Git, Git LFS et Syncthing ;
4. placez `deploy/cyrevision-server.service` dans `/etc/systemd/system` ;
5. placez `CYREVISION_SERVER_TOKEN=...` dans `/etc/cyrevision/server.env` avec les permissions `0600` ;
6. exécutez `systemctl enable --now cyrevision-server`.

Pour le VPN seulement, installez `wireguard-tools` et copiez `deploy/cyrevision-server-vpn.conf` comme drop-in `/etc/systemd/system/cyrevision-server.service.d/vpn.conf`, puis lancez `systemctl daemon-reload`. Ce droit n'est pas requis pour Git, Sync ou Backup.

Le service d’exemple écoute seulement sur `127.0.0.1:8080`, prévu pour un reverse proxy local.

## Stockage économique

Le chemin de backup peut viser :

- un disque USB ou SATA dédié ;
- un partage NFS/SMB ;
- un volume `rclone mount` vers un stockage objet ;
- un volume chiffré puis répliqué par `restic`.

Les snapshots incluent `.git` et `.git/lfs`, donc les anciens commits et assets LFS restent restaurables. La déduplication évite de payer plusieurs fois les fichiers inchangés. Utilisez une rétention locale courte et une rétention longue sur le volume économique.

## Token

Si `CYREVISION_SERVER_TOKEN` n’est pas défini, le serveur génère un token dans `config/server-token.txt` sous son dossier de données. Ce fichier est limité à l’utilisateur du service sur Linux.
