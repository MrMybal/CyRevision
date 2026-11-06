---
id: backups
title: Backups, retention, and cold archive
description: Create deduplicated snapshots and copy older content to cheaper mounted storage.
section: Workflows / Backups
order: 29
revision: 2
---

## Snapshots {#snapshots}
Choose a backup location and create a snapshot. Files are addressed by SHA-256, so identical content is stored only once. Unreal caches and build outputs are excluded by default.

## Retention {#retention}
Configure a timeline, maximum version count, maximum age, or storage budget. Retention applies to the active backup store after a snapshot succeeds.

## Restore {#restore}
Restore into an empty destination whenever possible. CyRevision refuses to overwrite existing files unless an operation explicitly permits it.

## Cold archive {#cold-archive}
Select a secondary disk, NAS folder, or mounted object storage and choose an archive age. Eligible manifests and missing objects are copied while at least the newest snapshots remain active. Cold archiving is non-destructive and never deletes the active copy automatically.

```image
{"id":"backups","src":"assets/backups.en.svg","alt":"Backups, retention, and cold archive","controls":"minimal"}
```

[See also](doc:sync-and-peers)
