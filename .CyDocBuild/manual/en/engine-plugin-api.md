---
id: engine-plugin-api
title: Editor companion connection API
description: Detect engine projects and connect the correct Unity or Godot companion.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Provider contract {#provider-contract}

`IGameEngineIntegrationPlugin` extends `ICyRevisionPlugin`. It exposes `Engine`, `BridgeStatus` and the `ProjectChanged` event. A change event identifies the engine, project root and action; it is not a Git commit.

Inspect the project before installing its engine-specific companion. Installation and connection configuration can change project-side configuration, so use only the explicitly selected root.

## Bridge scope {#bridge-scope}

The companion uses a private loopback bridge for authorized projects. Detection, installation and change notifications do not imply native asset rendering or semantic differences. File presentation remains an independent contract.

`GameEngineIntegrationPluginBase` is an optional host-side base class; this reference documents its public contract rather than its implementation.

[Engine integrations](doc:engine-integrations)
