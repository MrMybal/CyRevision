---
id: code-explorer
title: Explorateur, aperçus et édition texte
description: Parcourir les fichiers et comprendre les limites des aperçus.
section: Parcours / Code et ressources
order: 20
sourceRevision: 2
---

## Explorer {#explore}
Ouvrez Code & Assets > Solution Explorer. Cherchez un nom/chemin, choisissez Tree/List et affichez les fichiers masqués si nécessaire. Actualisez au besoin ; l'indexation exclut .git, les dépendances et les sorties générées courantes. Un résultat filtré n'est pas l'inventaire complet du disque.

## Prévisualiser {#preview-a-file}
Sélectionnez une ligne. JSON et INI s'ouvrent comme texte, avec coloration selon les formats supportés. Les gros fichiers ont un aperçu borné signalant la troncature ; l'extrait visible n'est pas le fichier complet. Les binaires affichent métadonnées ou aperçu adapté, pas des octets décodés arbitrairement.

## Modifier volontairement {#edit-deliberately}
Utilisez l'éditeur proposé, examinez le brouillon et appliquez/enregistrez explicitement. Changer d'onglet ou de sélection n'est pas un commit. Contrôlez Git > Changes puis lancez formatage/compilation dans l'outil concerné. Ne supposez pas un Undo multi-fichiers ou une version enregistrée si l'éditeur ne le propose pas.

## Actions sur fichiers {#file-actions}
Clic droit sur le fichier : historique Git / LFS si Git est actif, ou historique Sync dans un projet compatible. Les historiques d'un dossier et d'un fichier ont des périmètres différents. L'aperçu d'asset n'est pas son historique d'auteurs ; celui-ci s'ouvre séparément. Si l'action demande un fichier, resélectionnez sa ligne, pas un titre de dossier.

```image
{"id":"split-diff-v2","src":"assets/split-diff-v2.png","alt":"Explorateur, aperçus et édition texte","controls":"minimal"}
```

[Voir aussi](doc:code-and-ai)
