---
id: plugin-api
title: Plugin and app API
description: Create project-scoped extensions with explicit file permissions and honest isolation boundaries.
section: Extensions
order: 43
revision: 1
---

## Project activation {#project-activation}
A plugin is enabled independently for every project. Its tabs, project modes, preview providers, and services disappear as soon as it is disabled for the selected project.

## Contracts {#contracts}
Reference CyRevision.Plugin.Abstractions and implement ICyRevisionPlugin. Optional interfaces add file presentation, project modes, AI, game-engine, Lore, Perforce, or project lifecycle capabilities without referencing Avalonia.

## File broker {#file-broker}
IPluginProjectFileSandbox grants explicit enumerate, read, and write operations for selected relative roots. It rejects absolute paths, traversal, disallowed roots, oversized content, and symbolic links escaping the project. Writes are atomic.

## Security boundary {#security-boundary}
An in-process .NET plugin is trusted code and cannot be an operating-system sandbox. Use the file broker for cooperative extensions. Extensions requiring hard network, process, or filesystem isolation must run as an external app or worker process. CyRevision never describes an in-process DLL as fully isolated.

## Defaults {#defaults}
Start read-only, request write/network/process permissions separately, keep secrets out of manifests, and preview destructive or remote actions before execution.

```image
{"id": "integrations", "src": "assets/integrations.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:plugins)
