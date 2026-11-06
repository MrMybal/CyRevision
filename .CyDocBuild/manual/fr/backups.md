---
id: backups
title: Backups, rétention et archive froide
description: Créer des snapshots dédupliqués et copier les anciens contenus vers un stockage moins cher.
section: Parcours / Sauvegardes
order: 29
sourceRevision: 2
---

## Snapshots {#snapshots}
Choisissez un emplacement puis créez un snapshot. Les fichiers sont adressés par SHA-256 : un contenu identique n'est stocké qu'une fois. Les caches Unreal et sorties de compilation sont exclus par défaut.

## Rétention {#retention}
Configurez une chronologie, un nombre maximal de versions, un âge maximal ou un budget de stockage. La rétention est appliquée au stockage actif après la réussite du snapshot.

## Restauration {#restore}
Restaurez de préférence dans un dossier vide. CyRevision refuse d'écraser les fichiers existants sauf lorsqu'une opération l'autorise explicitement.

## Archive froide {#cold-archive}
Sélectionnez un disque secondaire, un dossier NAS ou un stockage objet monté, puis un âge d'archivage. Les manifests admissibles et objets manquants sont copiés tout en conservant les snapshots les plus récents dans le stockage actif. L'archivage ne supprime jamais automatiquement la copie active.

```image
{"id":"backups","src":"assets/backups.fr.svg","alt":"Backups, rétention et archive froide","controls":"minimal"}
```

[Voir aussi](doc:sync-and-peers)
