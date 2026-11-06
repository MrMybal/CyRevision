---
id: git-workflow
title: Utilisation quotidienne de Git
description: Examiner les modifications, créer des commits, gérer les branches et consulter l'historique.
section: Parcours / Git
order: 7
sourceRevision: 2
---

## Actualiser et vérifier {#refresh-and-review}
Actualiser lit l'état du dépôt. Sélectionnez un fichier modifié pour examiner son diff avant de l'indexer. Les fichiers binaires et LFS sont identifiés explicitement.

## Créer une révision {#create-a-revision}
1. Vérifiez les fichiers modifiés.
2. Indexez les modifications souhaitées.
3. Saisissez un message de commit clair.
4. Créez la révision.

## Branches et remotes {#branches-and-remotes}
Créez ou sélectionnez les branches locales dans le panneau Branches. Fetch télécharge les références distantes sans modifier le dossier de travail. Pull intègre la branche distante configurée. Push publie les commits locaux.

## Explorateur d'historique {#history-explorer}
Recherchez les commits par message, auteur ou hash. Sélectionnez deux révisions pour une comparaison A ↔ B. Exporter un ancien fichier écrit une copie séparée sans modifier le projet.

```image
{"id":"git","src":"assets/git.fr.svg","alt":"Utilisation quotidienne de Git","controls":"minimal"}
```

[Voir aussi](doc:git-history)
