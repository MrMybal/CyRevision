---
id: branches
title: Branches et worktrees
description: Examiner avant de changer, fusionner ou supprimer une branche.
section: Parcours / Git
parent: git-workflow
order: 9
sourceRevision: 2
---

## Inspecter ou changer {#inspect-versus-switch}
Ouvrez Git > Branches et actualisez. Distinguez branches locales et références distantes. Sélectionner une ligne l'inspecte ; changer la branche de travail est une action distincte.

## Commencer un travail {#start-work}
1. Récupérez les références avec Fetch.
2. Créez une branche au nom explicite depuis la bonne révision.
3. Basculez puis vérifiez le nom dans l'en-tête.
4. Faites des commits ciblés et poussez vers le bon remote.

Préservez les fichiers non commitées avant de changer. L'inspecteur parcourt une branche sans modifier le checkout. Un worktree historique crée un autre dossier de travail et consomme du disque. Fermez les éditeurs qui l'utilisent avant son retrait.

## Intégrer et ranger {#integrate-and-clean-up}
Comparez avant fusion ou cherry-pick. Appliquez les commits dépendants dans l'ordre. Résolvez ou abandonnez un conflit avant une autre intégration.

Ne supprimez qu'une branche inutile, jamais celle actuellement utilisée. Une PR fusionnée ne prouve pas que tous les commits locaux non poussés peuvent être perdus. PR > Actions propose une suppression locale confirmée lorsque possible : vérifiez la cible. Suppression locale et distante sont distinctes. Ne forcez pas simplement parce que la suppression normale est refusée.

```image
{"id":"git","src":"assets/git.fr.svg","alt":"Branches et worktrees","controls":"minimal"}
```

[Voir aussi](doc:revision-composition)
