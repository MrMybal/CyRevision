---
id: unreal-plugin
title: Optional CyRevision plugin and autonomous Unreal plugin
description: Install CyRevisionUnreal from the app, manage revisions inside Unreal, and connect both sides when desired.
section: Unreal Engine
order: 37
revision: 1
---

## Two separate plugins {#two-separate-plugins}
Unreal Engine Integration is an optional CyRevision plugin and is disabled by default. CyRevisionUnreal is an autonomous Editor plugin included in every desktop package.

## Compatibility {#compatibility}
The source plugin supports Unreal 4.27 and 5.0 through 5.8 for C++ projects. The Windows package also bundles exact Win64 binaries for Blueprint-only projects on Unreal 5.2 through 5.8. CyRevision detects the engine and project type, displays both compatibility lists, and blocks an unsafe or unavailable installation.

## Install from cyrevision {#install-from-cyrevision}
Open Plugins, enable Unreal Engine Integration, select a .uproject file, then choose Install / update in project. Existing plugin files are moved to Saved/CyRevision/PluginBackups before an update. C++ projects compile the source plugin with their Editor target; Blueprint-only projects receive an exact precompiled variant and do not need C++ project conversion.

## Autonomous revision tools {#autonomous-revision-tools}
Tools > Revision dashboard works without CyRevision. It shows Git status and recent commits and can stage, commit, fetch, pull, and push.

## Direct connection {#direct-connection}
CyRevision creates a random project token under Saved/CyRevision/bridge.json and listens on loopback only. Unreal can notify the matching CyRevision project after Git or advisory changes. Disabling the CyRevision plugin stops the bridge but does not remove the Editor plugin.

## Advisory reservations {#advisory-reservations}
Content Browser markers remain informational only: no checkout, permission change, or Git LFS lock is created.

```image
{"id": "integrations", "src": "assets/integrations.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:unreal-builds)
