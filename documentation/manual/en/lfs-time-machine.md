---
id: lfs-time-machine
title: LFS Time Machine
description: Inspect, request, export, and restore large-file versions without silently downloading history.
section: Git LFS
order: 18
revision: 1
---

## Track large assets {#track-large-assets}
Add patterns such as *.uasset, *.umap, *.png, or *.fbx to Git LFS before committing large binary assets.

## Storage states {#storage-states}
• Local: the object can be previewed, exported, or restored immediately.
• Peer: an authorized device announced the object in a signed inventory.
• Archive: the object was detected in the configured cold archive.
• Missing: no verified location is currently known.

## On-demand requests {#on-demand-requests}
Select a missing historical version and choose Request from peer. The signed request expires after seven days. An authorized peer publishes the object during a later exchange. Interrupted copies resume from a verified partial file and are accepted only after SHA-256 validation.

## Restore safety {#restore-safety}
Restore changes only the working file. It does not stage, commit, push, or lock the asset automatically.

```image
{"id": "locks", "src": "assets/locks.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:safe-lfs-storage)
