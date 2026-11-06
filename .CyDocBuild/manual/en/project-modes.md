---
id: project-modes
title: Project modes and service startup
description: Choose independent features for each project.
section: Get started
parent: getting-started
order: 2
revision: 2
---

## Location {#location}
Open Overview > Project > Operating mode. This is a per-project setting.

## Modes {#modes}
- Git (Beta): Git and LFS; a remote is optional.
- Git + Sync (Alpha): revision tools plus authorized peer exchange. Synchronization does not replace commits.
- Sync (Alpha): current-file exchange through a dedicated Syncthing instance, without Git.
- Sync + Versions (Alpha): synchronization with file versioning, not Git branches.
- Backup (Alpha): snapshots and retention without continuous synchronization.
- Plugin modes appear when their provider is enabled. Lore and plugins remain Alpha.

## Change safely {#change-safely}
1. Finish or cancel any operation modifying files.
2. Select the mode and read its summary.
3. Apply selected mode and confirm.
4. Check available tabs and service states.

Existing data is preserved. Active services are stopped/reconfigured; changing mode does not erase stored Git history or backups. Automatic Sync/VPN startup is a separate explicit project setting. Selecting an executable does not start its service.

If an expected tab is missing, inspect enabled plugins, detected project type and Overview > Visible tabs. Hidden/unavailable is different from loading.

```image
{"id":"modes","src":"assets/modes.en.svg","alt":"Project modes and service startup","controls":"minimal"}
```

[See also](doc:getting-started)
