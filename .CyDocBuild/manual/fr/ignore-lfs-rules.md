---
id: ignore-lfs-rules
title: Exclusions et motifs de suivi
description: Distinguer fichiers générés, préparation et suivi des gros fichiers.
section: Parcours / Git
parent: git-workflow
order: 17
sourceRevision: 2
---

## Deux politiques {#two-policies}
Git > Ignore rules exclut des chemins non suivis. Git > Git LFS gère les motifs .gitattributes pour du contenu à versionner. Exclusion et suivi LFS ne sont pas interchangeables.

## Exclure les fichiers générés {#ignore-generated-files}
1. Identifiez caches moteur, temporaires et sorties de build.
2. Ajoutez des motifs relatifs, de préférence par dossier ciblé.
3. Examinez les chemins, enregistrez et lisez le diff .gitignore.
4. Commitez la politique partagée uniquement si souhaité.

Ignorer un fichier suivi ne le retire pas de l'index. Retirer des fichiers générés suivis est un changement visible par l'équipe ; vérifiez le périmètre avant commit.

## Motifs LFS {#lfs-patterns}
Ajoutez-les avant de commiter de gros assets et vérifiez Git LFS. Relisez .gitattributes. Ajouter un motif ne migre pas les anciens contenus ni l'historique : cette migration exige une maintenance coordonnée distincte.

Keep/local-only dans Changes peut écarter un élément du commit préparé sans le supprimer. Ce n'est pas automatiquement une règle d'exclusion partagée. Vérifiez ensemble inclus et index avant Commit.

```image
{"id":"git","src":"assets/git.fr.svg","alt":"Exclusions et motifs de suivi","controls":"minimal"}
```

[Voir aussi](doc:safe-lfs-storage)
