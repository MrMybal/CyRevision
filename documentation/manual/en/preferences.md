---
id: preferences
title: Preferences, cache, themes and application options
description: Configure global behavior separately from per-project settings.
section: Basics
order: 3
revision: 1
---

## Open preferences {#open-preferences}
Use Edit > Preferences or Ctrl+,. Review Application/Desktop sections and save changes explicitly. Project mode, remote and automatic project services belong to Overview > Project, not global preferences.

## Cache location {#cache-location}
Select the cache root using the folder control and read the current path/status. The cache is generated data, not the original repository or a backup store. Changing the preferred root is not permission to delete arbitrary old directories. Stop active readers/transfers before a relocation offered by the interface, verify the new location and inspect disk usage afterward.

## Appearance {#appearance}
Choose a theme preset and density. V2 uses shared CyUI theme roles; the Theme editor allows previewing supported colors. Test selected tabs, disabled actions, diffs and warnings for contrast. Font/DPI behavior depends on the platform; a theme change should not be used to hide an operation error.

## Behavior and shortcuts {#behavior-and-shortcuts}
Configure startup/tray, refresh/performance and keyboard options that the installed version exposes. Shortcut conflicts must be resolved before saving. Reset a section only when you intend to replace its custom values. Saved preferences do not automatically publish changes or enable every optional project service.

```image
{"id": "settings", "src": "assets/settings.en.svg", "alt": "Preferences, cache, themes and application options", "controls": "minimal"}
```

[See also](doc:keyboard-shortcuts)
