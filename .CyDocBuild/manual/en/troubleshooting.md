---
id: troubleshooting
title: Troubleshooting and safety
description: Diagnose missing tools, failed exchanges, partial transfers, and common repository problems.
section: Help and recovery
order: 47
revision: 2
---

## Tools not detected {#tools-not-detected}
Confirm that git --version and git lfs version work in a terminal. Restart CyRevision after changing PATH. Syncthing and WireGuard remain optional until their modules are enabled.

## Push rejected {#push-rejected}
Fetch first and inspect the incoming branch. Never use a force push unless replacing remote history is deliberate and understood.

## Pair exchange not moving {#pair-exchange-not-moving}
Check that the dedicated Sync instance is running, the peer is connected, and its membership certificate still has a Git-writing role. A file appearing in the exchange folder is not imported unless an authorized signed inventory announces it.

## Partial lfs files {#partial-lfs-files}
Files ending in .cyrevision-partial are resumable local staging files. Do not rename them into the LFS object store manually. The next exchange verifies the existing prefix and completes or safely restarts the copy.

## Safe recovery {#safe-recovery}
Keep the working tree, active .git directory, backup store, and cold archive as separate locations. Before deleting any data, create a snapshot and verify a restore into a different folder.

```image
{"id":"settings","src":"assets/settings.en.svg","alt":"Troubleshooting and safety","controls":"minimal"}
```

[See also](doc:diagnostics)
