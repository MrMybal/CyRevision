---
id: merge-conflicts
title: Resolve Git conflicts
description: Complete or abort an integration with deliberate resolutions.
section: Workflows / Git
parent: pull-request-manager
order: 12
revision: 2
---

## Recognize the operation {#recognize-the-operation}
Pull, merge and cherry-pick can stop when Git cannot combine changes. This is not a Syncthing conflict copy. Save editor buffers and inspect the conflicted paths.

## Resolve a text file {#resolve-a-text-file}
1. Open a conflicted path in the conflict editor.
2. Compare base/local/incoming versions where available.
3. Compose the intended result, not merely remove markers.
4. Save/apply, inspect and test the result.
5. Mark resolved/stage using the offered action, then finish the original Git operation.

Keeping one side is a decision, not automatic correctness. If uncertain, use the supported Abort control, preserving needed work first. Do not start another integration over an unresolved operation.

## Binary and LFS assets {#binary-and-lfs-assets}
Text merge cannot combine arbitrary binary assets. Choose a valid version or resolve in the owning application. Obtain the LFS payload if necessary; do not edit its pointer as if it were the asset.

## PR conflicts {#pr-conflicts}
PR > Conflicts reports mergeability and paths when the provider exposes them. Some providers only supply a flag, not exact files. Refresh after pushing a fix. An empty list while mergeability is being calculated is not proof of a clean merge.

```image
{"id":"conflicts","src":"assets/conflicts.en.svg","alt":"Resolve Git conflicts","controls":"minimal"}
```

[See also](doc:pull-request-manager)
