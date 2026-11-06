---
id: safe-lfs-storage
title: Stockage et nettoyage LFS sûr
description: Déplacer le cache LFS et récupérer de l'espace sans supprimer la dernière copie d'un objet.
section: Parcours / Git
parent: lfs-time-machine
order: 19
sourceRevision: 2
---

## Classification {#classification}
Ouvrez Git LFS > Storage & safe cleanup puis lancez Analyze. Les objets référencés par une branche locale, un stash, un index ou un worktree sont protégés, y compris les commits non publiés. Un objet devient éligible uniquement s'il est orphelin, hors délai de grâce et dispose du nombre demandé de copies vérifiées. Sinon il reste bloqué.

## Preuves de conservation {#retention-evidence}
Une copie remote est acceptée après le dry-run Git LFS avec vérification du remote et des objets inaccessibles. Une copie pair exige un inventaire signé récent et un objet réellement publié. Une archive utilise le manifeste CyRevision et son SHA-256 est revérifié avant suppression. Sans remote, conservez au moins une copie sur un pair ou dans une archive.

## Pr et branches supprimées {#deleted-prs-and-branches}
La suppression d'une branche sur une forge ne retire pas sa référence locale. Ses objets restent protégés jusqu'à la suppression volontaire de cette branche locale. Une PR uniquement locale ou une branche non poussée est toujours conservée.

## Stockage externe {#external-storage}
Choisissez un dossier vide dédié. CyRevision copie et vérifie tous les objets avant de configurer lfs.storage localement. Un marqueur empêche le partage dangereux entre plusieurs dépôts. Gardez l'ancien cache lors du premier essai.

## Nettoyage {#cleanup}
Archive candidates crée les copies sans supprimer. Relancez Analyze puis confirmez la purge. Le plan expire après quinze minutes, les références sont relues et un audit est écrit sous .git/cyrevision/lfs-cleanup.

```image
{"id":"backups","src":"assets/backups.fr.svg","alt":"Stockage et nettoyage LFS sûr","controls":"minimal"}
```

[Voir aussi](doc:lfs-time-machine)
