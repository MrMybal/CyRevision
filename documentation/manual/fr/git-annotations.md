---
id: git-annotations
title: Annotations des lignes et auteurs
description: Retrouver le commit d'une ligne et examiner le changement.
section: Git
order: 10
sourceRevision: 1
---

## Ouvrir les annotations {#open-annotations}
Git > Annotations avec un chemin texte suivi. Chargez le contexte/révision proposé. Les lignes associent texte, commit, auteur et date.

## Examiner {#investigate}
1. Repérez une ligne ou cherchez fichier/chemin.
2. Lisez le commit et son message.
3. Ouvrez-le dans History pour comprendre fichiers liés et intention.
4. Comparez la révision précédente pour le changement réel.

Blame désigne la dernière révision responsable d'une ligne ; formatage, déplacements et fusions affectent l'attribution. Ce n'est pas la preuve de l'auteur initial d'une fonction. Les binaires n'ont pas d'annotation pertinente : utilisez Git / LFS file history.

Si vide, vérifiez Git actif, chemin relatif présent dans la révision et fichier commité. Les nouvelles lignes non commitées peuvent ne pas avoir d'auteur de commit. Un renommage peut nécessiter un autre chemin historique. Actualisez après changement de branche.

```image
{"id": "history-v2", "src": "assets/history-v2.png", "alt": "Annotations des lignes et auteurs", "controls": "minimal"}
```

[Voir aussi](doc:git-history)
