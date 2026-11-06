---
id: project-mode-api
title: Plugin-owned project modes
description: Declare feature switches, retention and supported tabs without coupling to the desktop UI.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Declare modes {#declare-modes}

Implement `IProjectModeProvider` and expose `ProjectModes`. Each `PluginProjectModeDescriptor` has an ID, name, description, feature switches, retention, workspace tab IDs and a category label. Mode IDs are unique within the provider.

`PluginProjectModeFeatures` declares Git, LFS, peer synchronization, backup and standard-remote switches. `PluginProjectModeRetention` declares current-state, timeline or permanent retention and optional version/age/storage limits.

## Evaluate compatibility {#compatibility}

The host passes the selected project's identity, name, root and enabled plugin IDs to `EvaluateProjectMode`. Return an availability flag and a useful explanation. This check does not switch modes or mutate the project. The provider must be enabled for that project before its modes and registered workspaces are offered.

[Choose a mode](doc:project-modes)
