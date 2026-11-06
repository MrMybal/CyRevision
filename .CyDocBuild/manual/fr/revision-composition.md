---
id: revision-composition
title: Restauration multiple et composition cherry-pick
description: Composer des versions de fichiers ou une suite de commits avec un aperçu de sécurité avant toute modification.
section: Parcours / Git
parent: git-history
order: 11
sourceRevision: 2
---

## Restauration multiple {#multi-restore}
Ouvrez Compose > Multi restore, choisissez un commit et chargez ses fichiers. Cochez uniquement les fichiers à inclure. BeforeCommit choisit l'état précédant immédiatement le commit ; AtCommit choisit sa version exacte. Ajouts, suppressions et renommages deviennent des opérations explicites de restauration ou suppression.

## Aperçu de sécurité {#safety-preview}
Créez l'aperçu avant d'appliquer. Il expire après quinze minutes, vérifie que HEAD n'a pas avancé, détecte les modifications locales et bloque les objets Git LFS absents. Une copie de récupération horodatée est écrite dans les données Git avant toute modification. L'index reste inchangé et aucun commit ou push n'est créé.

## Comparaison de branches {#branch-comparison}
Ouvrez Branch compare & cherry-pick, choisissez une source et une cible locale existante. Les commits source uniquement, cible uniquement et équivalents par patch sont séparés ; un changement déjà intégré reste donc visible même si son hash diffère. Les commits cochés sont appliqués dans l'ordre affiché.

## Modes cherry-pick {#cherry-pick-modes}
KeepCommits conserve chaque commit. CombineIntoOne applique leur contenu puis crée un seul commit avec le message fourni. Les commits de merge sont bloqués tant qu'un parent principal explicite n'est pas disponible.

## Isolation et conflits {#isolation-and-conflicts}
Si la cible n'est pas la branche active, CyRevision utilise un worktree temporaire sans changer la branche affichée. Un conflit annule l'opération et restaure le point de départ de la cible. Aucun push n'est automatique.

## Fenêtres détachables {#detached-windows}
View > Open detached workspace ouvre des fenêtres History, Code, Multi Restore ou Cherry-pick supplémentaires. Plusieurs fenêtres peuvent rester ouvertes sur différents écrans ; leurs données et leur sélection restent liées à la fenêtre principale.

```image
{"id":"git","src":"assets/git.fr.svg","alt":"Restauration multiple et composition cherry-pick","controls":"minimal"}
```

[Voir aussi](doc:merge-conflicts)
