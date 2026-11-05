---
id: lfs-time-machine
title: Time Machine LFS
description: Consulter, demander, exporter et restaurer les versions de fichiers lourds sans téléchargement silencieux.
section: Git LFS
order: 18
sourceRevision: 1
---

## Suivre les assets lourds {#track-large-assets}
Ajoutez des motifs comme *.uasset, *.umap, *.png ou *.fbx à Git LFS avant de committer les gros assets binaires.

## États de stockage {#storage-states}
• Local : l'objet peut être prévisualisé, exporté ou restauré immédiatement.
• Pair : un appareil autorisé a annoncé l'objet dans un inventaire signé.
• Archive : l'objet a été détecté dans l'archive froide configurée.
• Missing : aucun emplacement vérifié n'est connu.

## Demande à la carte {#on-demand-requests}
Sélectionnez une ancienne version absente puis Demander au pair. La demande signée expire après sept jours. Un pair autorisé publie l'objet lors d'un échange ultérieur. Une copie interrompue reprend depuis un fichier partiel vérifié et n'est acceptée qu'après validation SHA-256.

## Restauration sûre {#restore-safety}
Restaurer modifie uniquement le fichier de travail. L'opération n'indexe, ne committe, ne pousse et ne verrouille rien automatiquement.

```image
{"id": "locks", "src": "assets/locks.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:safe-lfs-storage)
