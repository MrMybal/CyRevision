---
id: plugin-contracts-api
title: Plugin lifecycle and project activation
description: Initialize a plugin, receive a project context and release resources explicitly.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Lifecycle {#lifecycle}

Reference the `CyRevision.Plugin.Abstractions` assembly and import `CyRevision.Plugin.Abstractions`. Implement `ICyRevisionPlugin`, expose its `Descriptor`, implement `InitializeAsync`, and release owned resources through `IAsyncDisposable.DisposeAsync`.

The host supplies `CyRevisionPluginContext`: `ApplicationDirectory`, `PackageDirectory`, `ConfigurationDirectory`, `DataDirectory` and `ApplicationVersion`. Keep configuration and generated data in their intended directories; an application directory is not permission to scan a user's files.

## Project context {#project-context}

Implement `IProjectScopedPlugin` only when the extension needs a selected project. `ActivateProjectAsync` receives a `PluginProjectSandboxPolicy` and a cooperative file broker. `DeactivateProjectAsync` receives the project ID. Drop project-specific state when that context ends.

A package contains `cyrevision-plugin.json`, its entry assembly and entry type. Installing a package and enabling it for a project are separate actions. Do not start every optional service just because a project is selected.

## Using contracts {#source}

The reference below lists public signatures, parameter meanings and return values. It does not expose implementation files. These contracts are UI-neutral: a plugin does not need an Avalonia or Dear ImGui reference.

[File access](doc:plugin-file-broker-api) · [Project modes](doc:project-mode-api)
