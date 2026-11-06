---
id: sync-and-peers
title: Smart Sync and authorized peers
description: Run an isolated Syncthing instance and exchange signed Git/LFS data with approved devices.
section: Workflows / Collaboration
order: 26
revision: 2
---

## Isolated syncthing {#isolated-syncthing}
Each CyRevision project uses its own configuration, database, API key, loopback address, and port. CyRevision never searches for, reconfigures, pauses, or stops a personal Syncthing instance.

## Start and stop {#start-and-stop}
Selecting a Syncthing executable does not start it. Start launches the dedicated project instance. Pause, Resume, and Stop affect only the exact child process created by CyRevision.

## Peer admission {#peer-admission}
Create a single-use invitation and send its verification code through a separate channel. Approve the returned join request only after checking the device and role. Revocation removes future Git/LFS authorization.

## Smart lfs policy {#smart-lfs-policy}
Current objects are always prioritized. Historical objects can remain on demand, include a configurable number of recent versions, or replicate everything. Signed inventories advertise availability without forcing a download.

```image
{"id":"sync","src":"assets/sync.en.svg","alt":"Smart Sync and authorized peers","controls":"minimal"}
```

[See also](doc:sync-conflicts)
