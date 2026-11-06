---
id: merge-conflicts
title: Résoudre les conflits Git
description: Terminer ou abandonner une intégration en choisissant les résolutions.
section: Parcours / Git
parent: pull-request-manager
order: 12
sourceRevision: 2
---

## Identifier l'opération {#recognize-the-operation}
Pull, merge et cherry-pick peuvent s'arrêter si Git ne sait pas combiner les changements. Ce n'est pas une copie de conflit Syncthing. Enregistrez les buffers d'éditeur et consultez les chemins concernés.

## Résoudre un texte {#resolve-a-text-file}
1. Ouvrez le chemin dans l'éditeur de conflits.
2. Comparez les versions base/locale/entrante disponibles.
3. Composez le résultat voulu, sans simplement retirer les marqueurs.
4. Enregistrez/appliquez, examinez puis testez.
5. Marquez résolu/indexez avec l'action proposée et terminez l'opération Git initiale.

Garder un côté est un choix, pas une garantie. En cas de doute, utilisez Abort en préservant d'abord le travail nécessaire. Ne démarrez pas d'autre intégration sur une opération non résolue.

## Binaires et LFS {#binary-and-lfs-assets}
Une fusion texte ne combine pas arbitrairement les assets binaires. Choisissez une version valide ou résolvez dans l'application propriétaire. Récupérez le contenu LFS si nécessaire ; son pointeur n'est pas l'asset.

## Conflits de PR {#pr-conflicts}
PR > Conflicts rapporte la fusion possible et les chemins fournis par l'hébergeur. Certains ne donnent qu'un indicateur, pas les fichiers précis. Actualisez après avoir poussé le correctif. Une liste vide pendant le calcul de fusion ne prouve pas l'absence de conflit.

```image
{"id":"conflicts","src":"assets/conflicts.fr.svg","alt":"Résoudre les conflits Git","controls":"minimal"}
```

[Voir aussi](doc:pull-request-manager)
