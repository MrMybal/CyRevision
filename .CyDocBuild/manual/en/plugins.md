---
id: plugins
title: Plugins and optional features
description: Enable providers explicitly and keep project tools independent.
section: Extensions
order: 36
revision: 2
---

## Locate and enable {#locate-and-enable}
Open Extensions & Help > Plugins. Inspect installed providers, their status and descriptions. Enable only the feature required for your workflow. Most plugins are Alpha; availability does not imply production maturity.

## Configure in context {#configure-in-context}
1. Enable the provider and apply its configuration.
2. Select the intended project and inspect project detection.
3. Open the provider's workspace or plugin operating mode.
4. Set the external executable/account options, test the connection and inspect status.

A disabled plugin should not start its service just because a project opens. An engine integration can be installed without its external engine currently being running. Disabling an integration does not necessarily uninstall a project-side plugin or remove its stored data.

## If a tab is absent {#if-a-tab-is-absent}
Check Overview > Visible tabs, the active project mode and file type (.uproject, Unity/Godot metadata or provider-specific project). A plugin-supplied mode can move tabs under its own category. Return to a supported project before assuming the provider failed.

Install only trusted packages. Review compatibility before updating an integration used by a team; close the owning editor when installation requires it. Use the developer API chapter for extension authoring, not application preference files.

```image
{"id":"integrations","src":"assets/integrations.en.svg","alt":"Plugins and optional features","controls":"minimal"}
```

[See also](doc:plugin-api)
