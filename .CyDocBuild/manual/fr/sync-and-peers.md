---
id: sync-and-peers
title: Smart Sync et pairs autorisés
description: Utiliser une instance Syncthing isolée et échanger des données Git/LFS signées.
section: Parcours / Collaboration
order: 26
sourceRevision: 2
---

## Syncthing isolé {#isolated-syncthing}
Chaque projet CyRevision possède sa configuration, sa base, sa clé API, son adresse loopback et son port. CyRevision ne recherche, ne reconfigure, ne met en pause et n'arrête jamais une instance Syncthing personnelle.

## Démarrer et arrêter {#start-and-stop}
Sélectionner l'exécutable ne le lance pas. Démarrer crée l'instance dédiée au projet. Pause, Reprendre et Arrêter concernent uniquement le processus enfant exact créé par CyRevision.

## Admission d'un pair {#peer-admission}
Créez une invitation à usage unique et transmettez son code par un canal séparé. Vérifiez l'appareil et le rôle avant d'approuver la demande. La révocation interdit les futurs échanges Git/LFS.

## Politique lfs intelligente {#smart-lfs-policy}
Les objets courants sont toujours prioritaires. L'historique peut rester à la demande, inclure un nombre réglable de versions récentes ou être répliqué entièrement. Les inventaires signés annoncent la disponibilité sans forcer le téléchargement.

```image
{"id":"sync","src":"assets/sync.fr.svg","alt":"Smart Sync et pairs autorisés","controls":"minimal"}
```

[Voir aussi](doc:sync-conflicts)
