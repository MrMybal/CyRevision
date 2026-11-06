---
id: sync-conflicts
title: Sync conflicts and version recovery
description: Resolve concurrent file edits without confusing them with Git merges.
section: Workflows / Collaboration
parent: sync-and-peers
order: 27
revision: 2
---

## Identify a Sync conflict {#identify-a-sync-conflict}
Open Sync > Sync conflicts when the project's synchronization tools are enabled. Concurrent edits can create conflict copies or pending Sync commit conflicts. These are not Git merge markers and should not be resolved blindly through a Git merge command.

## Recover deliberately {#recover-deliberately}
1. Pause editing of the affected file across devices.
2. Inspect both versions and their timestamps/participants.
3. Preserve a copy if uncertain, especially for binary assets.
4. Choose the intended resolution using the offered operation.
5. Verify the working file and let peers reconcile before resuming edits.

For text, compare content; for binary assets open each candidate in the owning application. A newer timestamp does not guarantee a better version. Sync + Versions may provide older file versions, but its retention is not Git branch history.

## Cleanup {#cleanup}
Conflict backup retention and expired-copy cleanup can remove recovery material. Inspect the scope and confirmation first. Test an independent backup restore before relying on synchronization alone: deletions and mistakes can propagate as well as useful changes.

```image
{"id":"sync","src":"assets/sync.en.svg","alt":"Sync conflicts and version recovery","controls":"minimal"}
```

[See also](doc:sync-and-peers)
