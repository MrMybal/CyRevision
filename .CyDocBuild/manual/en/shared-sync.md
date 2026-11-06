---
id: shared-sync
title: Shared folders outside the project
description: Share only explicit folders with authorized peers.
section: Workflows / Collaboration
parent: sync-and-peers
order: 28
revision: 2
---

## Create a share {#create-a-share}
Open Team & Network > Shared Sync folders. Choose a specific existing folder, a clear display name and the intended folder policy. Do not share a broad user profile, whole disk or unrelated project just to exchange a few files.

## Admit participants {#admit-participants}
1. Check the selected project and authorized peer identity.
2. Configure the folder's intended participants and policy.
3. Start the dedicated synchronization service explicitly.
4. Verify folder/device state and pending changes on each side.

A VPN connection does not grant synchronized-folder access. Adding a device and sharing a folder are distinct permissions. Validate invitations and role changes through the membership workflow.

## Resolve and stop {#resolve-and-stop}
Inspect conflicts in the Sync conflict view before deleting alternate copies. Pause/Stop affects the project-managed instance, not a personal Syncthing installation. Removing a share configuration is different from deleting its local folder; read confirmations and preserve required content.

Shared current files are not a backup. Use dedicated snapshots for recovery and avoid sharing generated caches that repeatedly trigger rescans.

```image
{"id":"sync","src":"assets/sync.en.svg","alt":"Shared folders outside the project","controls":"minimal"}
```

[See also](doc:members-security)
