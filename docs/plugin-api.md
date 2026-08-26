# CyRevision plugin and app API

CyRevision extensions are packaged separately and enabled for each project. Enabling a package for one project does not expose its tabs, file providers, project modes, or services in another project.

## Extension contracts

Reference `CyRevision.Plugin.Abstractions` and implement `ICyRevisionPlugin`. Optional interfaces add narrowly-scoped capabilities:

- `IFilePresentationProvider` adds previews and diffs everywhere CyRevision displays a file;
- `IProjectModeProvider` contributes a complete operating mode and its workspace tabs;
- `IProjectScopedPlugin` receives activation and deactivation for one project;
- `IWorkItemIntegrationPlugin` searches a project-scoped issue tracker and returns stable references for commit and pull-request drafts without exposing credentials to Git;
- engine, AI, Lore, and Perforce interfaces expose their dedicated integration surfaces.

The package contains `cyrevision-plugin.json`, an entry assembly, and an entry type. UI-independent records keep plugins decoupled from Avalonia.

The bundled Jira, ClickUp, and CyTask connectors are reference implementations of `IWorkItemIntegrationPlugin`. They store only project-scoped server/scope settings; tokens remain in memory or are read from a named environment variable.

## Project file sandbox

Project-scoped extensions should access files through `IPluginProjectFileSandbox`. The host policy grants only explicit operations and relative roots. The broker rejects absolute paths, `..` traversal, disallowed roots, oversized reads/writes, and symbolic links escaping the project. Writes use a temporary file followed by an atomic replacement.

An in-process .NET DLL is trusted code and is not an operating-system security boundary: it could call `System.IO`, networking, or process APIs directly. Plugins needing hard isolation must run as an external app/worker process. The future app host will map declared network and process capabilities to an isolated process; CyRevision never presents the in-process broker as a complete OS sandbox.

## Safety rules

- default to read-only permissions;
- ask separately for write, network, and process capabilities;
- store project configuration under CyRevision data directories, not in Git;
- never persist passwords, API keys, session tokens, or WireGuard private keys in a plugin manifest;
- disable every project surface immediately when its plugin is disabled;
- return a preview before performing a destructive or remote operation.
