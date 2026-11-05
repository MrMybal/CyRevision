---
id: file-locks
title: Verrous et réservations de travail
description: Les verrous serveur sont distincts des avis informatifs.
section: Git LFS
order: 16
sourceRevision: 1
---

## Verrous LFS {#lfs-locks}
Git > File locks : actualisez le serveur et lisez chemin, propriétaire, date et appartenance. Une entrée en cache ne prouve pas que le serveur est actuellement joignable.

## Coordonner une modification {#coordinate-an-edit}
1. Vérifiez le verrou avant d'éditer un binaire partagé.
2. Verrouillez le chemin exact si le workflow le demande.
3. Modifiez/testez puis commitez et poussez normalement.
4. Déverrouillez quand l'équipe peut reprendre sans risque.

Libérer le fichier d'un autre ou tous les verrous exige les droits adaptés. Coordonnez-vous et examinez la confirmation. Un commit ne libère pas automatiquement tous les verrous distants. L'application des règles dépend de l'hébergeur et des outils de l'équipe.

## Réservations informatives {#advisory-reservations}
Team & Network > Work in progress et les marqueurs du Content Browser Unreal annoncent un travail sur un chemin. Ils ne changent pas les permissions, ne créent pas de verrou LFS et n'empêchent pas l'écriture. Ce sont des outils de coordination, pas une exclusivité garantie.

Actualisez un propriétaire périmé avant de forcer. Si l'accès est refusé, corrigez les droits serveur/compte au lieu de supprimer des métadonnées locales sans rapport.

```image
{"id": "locks", "src": "assets/locks.fr.svg", "alt": "Verrous et réservations de travail", "controls": "minimal"}
```

[Voir aussi](doc:lfs-time-machine)
