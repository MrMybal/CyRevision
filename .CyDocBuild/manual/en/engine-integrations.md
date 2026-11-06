---
id: engine-integrations
title: Unity, Godot and Lore integrations
description: Use the correct engine adapter without assuming universal compatibility.
section: Extensions
parent: plugins
order: 38
revision: 2
---

## Enable the matching provider {#enable-the-matching-provider}
Open Plugins and enable Unity, Godot or Lore only when needed. Select the project root with the expected engine metadata. Its workspace is shown when the integration is enabled and detection succeeds.

## Configure and verify {#configure-and-verify}
1. Read the detected project/version information.
2. Choose the external executable or project file requested by that adapter.
3. Apply configuration and inspect its status before launch/install actions.
4. Open the owning engine to verify the result.

Each adapter exposes its actual supported controls; this is not a promise of identical features across engines. Do not use an Unreal package installer for a Unity/Godot project. Plugin-side installation and application-side activation are different steps.

## Lore mode {#lore-mode}
Lore is Alpha and may provide its own operating mode, branch/history or push controls. Treat an Apply/Switch/Push operation as a real project mutation and read its confirmation. Do not infer that Lore history is a Git repository unless the project explicitly uses Git too.

## Troubleshoot detection {#troubleshoot-detection}
Check the selected root, metadata file, enabled provider and visible tabs. Nested engine projects may require registering the actual project folder rather than its parent. Reopen/refresh after external metadata changes. If a version is unsupported, retain the existing project integration rather than forcing a mismatched package.

```image
{"id":"integrations","src":"assets/integrations.en.svg","alt":"Unity, Godot and Lore integrations","controls":"minimal"}
```

[See also](doc:unreal-plugin)
