---
id: getting-started
title: Getting started
description: Choose a project mode, register a folder, and understand which tools are optional.
section: Basics
order: 0
revision: 1
---

CyRevision keeps Git, synchronization, backups, and VPN independent. Choose only the modules required by the project.

## Create a repository {#create-a-repository}
Use Create repository for a new Git project, or Open repository for an existing one. Add folder registers a folder for Sync or Backup without requiring Git.

## Project modes {#project-modes}
• Git: local revision control only.
• Git + Sync: local Git plus signed peer exchange.
• Sync: current files synchronized without Git.
• Sync + Versions: file synchronization with Syncthing versioning.
• Backup: deduplicated snapshots without continuous synchronization.

## Requirements {#requirements}
Git and Git LFS are required for Git projects. Syncthing is needed only for a Sync mode. WireGuard is needed only for VPN. CyRevision never starts an optional tool merely because a project was opened.

## Native V2 and Classic {#native-v2-and-classic}
V2 uses a native CyUI interface on Windows. The managed engine owns the same project services; Classic remains available from View > Classic interface and is the cross-platform UI. Both interfaces open the same local CyDoc reader. English is the default and Français is available in Edit > Language or the guide selector.

## First session {#first-session}
1. Open an existing repository, Clone, Create repository or Add folder as appropriate.
2. Choose a mode in Overview > Project.
3. Verify the header's project/branch before an operation.
4. Review changes before committing, and configure a remote only if publishing is needed.
5. Set an independent backup and test recovery before relying on team synchronization.

Screenshots use a sample project and may show the English interface or a different theme. Captions and concept diagrams follow the guide language. Use the related chapters for complete procedures.

```image
{"id": "modes", "src": "assets/modes.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

```image
{"id": "workspace-v2", "src": "assets/workspace-v2.png", "alt": "Native V2 workspace with a demonstration repository", "controls": "minimal"}
```

[See also](doc:navigation)
