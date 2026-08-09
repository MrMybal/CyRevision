# Synchronisation intelligente et archive froide

## Plan de synchronisation

Le planificateur sépare les contenus au lieu de traiter le projet comme un bloc unique :

1. métadonnées, révisions et références Git ;
2. état de travail courant ;
3. objets LFS nécessaires à `HEAD` ;
4. versions LFS historiques ;
5. snapshots de sauvegarde.

Les objets actuels et manquants ont la priorité. L'historique LFS peut rester **On demand**, conserver un nombre réglable de versions récentes, ou être répliqué intégralement. Les backups peuvent rester dans leur stockage dédié ou être destinés à un pair de sauvegarde. Construire ou modifier ce plan ne démarre jamais Syncthing : seul le bouton **Start** lance l'instance isolée appartenant à CyRevision.

## Archive froide

Une archive froide peut pointer vers un disque secondaire, un dossier NAS ou tout stockage monté par le système. CyRevision sélectionne les snapshots plus vieux que l'âge configuré tout en préservant au moins les cinq plus récents dans le stockage actif.

L'archivage copie les manifests et les objets de contenu manquants. Le stockage cible conserve la même structure dédupliquée et peut donc être ouvert par le moteur de backup pour restaurer un snapshot. L'opération est volontairement non destructive : elle ne supprime aucun snapshot ni objet du stockage actif. Une politique de rétention peut être appliquée séparément après vérification de l'archive.
