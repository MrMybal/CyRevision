---
id: tray-and-startup
title: System tray and automatic startup
description: Keep optional background services available after hiding the main window.
section: Basics
order: 44
revision: 1
---

## System tray {#system-tray}
CyRevision exposes a native system tray icon on Windows, macOS, and supported Linux desktops. Click the icon to show or hide the main window. The tray menu shows the selected project and provides Refresh, startup preferences, and an explicit Quit action.

## Close and quit {#close-and-quit}
Close window to tray keeps CyRevision running when the window close button is used. Use File > Exit or Quit CyRevision from the tray to stop the application and dispose its managed services. Disabling the tray also disables close-to-tray and hidden startup.

## Automatic startup {#automatic-startup}
Launch at system login registers CyRevision for the current user only: the Windows Run key, a Linux XDG autostart desktop entry, or a macOS LaunchAgent. Start hidden in tray adds --background so login does not open the main window. The registration always targets the installed executable currently running. Portable builds therefore must stay at the same path.

## Linux note {#linux-note}
Tray icons require desktop support for StatusNotifier/AppIndicator. If a Linux desktop does not display tray icons, disable hidden startup so the main window remains reachable.

```image
{"id": "settings", "src": "assets/settings.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:application-updates)
