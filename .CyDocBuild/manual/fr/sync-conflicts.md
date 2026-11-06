---
id: sync-conflicts
title: Conflits Sync et récupération de versions
description: Résoudre les éditions concurrentes sans les confondre avec Git.
section: Parcours / Collaboration
parent: sync-and-peers
order: 27
sourceRevision: 2
---

## Identifier un conflit Sync {#identify-a-sync-conflict}
Ouvrez Sync > Sync conflicts si ces outils sont actifs. Des éditions concurrentes peuvent produire des copies de conflit ou conflits de commits Sync. Ce ne sont pas des marqueurs de fusion Git ; ne lancez pas aveuglément une fusion Git pour les résoudre.

## Récupérer volontairement {#recover-deliberately}
1. Suspendez l'édition du fichier sur les appareils.
2. Comparez versions, dates et participants.
3. Gardez une copie en cas de doute, surtout pour un binaire.
4. Choisissez la résolution proposée adaptée.
5. Vérifiez le fichier puis laissez les pairs se réconcilier avant de reprendre.

Comparez le texte ; pour un asset binaire ouvrez chaque candidat dans son application. Une date plus récente ne garantit pas une meilleure version. Sync + Versions peut conserver des versions anciennes mais ce n'est pas l'historique de branches Git.

## Nettoyage {#cleanup}
La rétention et le nettoyage des copies expirées retirent des possibilités de récupération. Examinez périmètre et confirmation. Testez une sauvegarde indépendante : la synchronisation propage aussi erreurs et suppressions.

```image
{"id":"sync","src":"assets/sync.fr.svg","alt":"Conflits Sync et récupération de versions","controls":"minimal"}
```

[Voir aussi](doc:sync-and-peers)
