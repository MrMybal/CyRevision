# CyRevision 0.1.31 Alpha

- Restored the Windows V2 system tray: closing the main window hides the main and detached views while the engine and background services continue running.
- Added tray commands to reopen CyRevision or quit completely through the existing confirmation dialog.
- Kept engine requests responsive while the interface is hidden and paused rendering until the windows are restored.
- Enabled native tray and hidden-start preferences, and corrected Windows login startup to launch the native frontend rather than the engine alone.
- Restored the tray icon after Windows Explorer restarts, and kept the window accessible if an icon cannot be registered.
- Prevented isolated test profiles from changing system startup registration and avoided duplicate tray icons when saving preferences.

Windows V2 requires Windows 10/11 x64 and Direct3D 11. In the portable ZIP, launch `CyRevision.Native.exe`; `CyRevision.Desktop.exe` and the **CyRevision (Classic)** installer shortcut remain available for the Avalonia interface. Linux and macOS continue to use Avalonia.

The V2 interface remains Alpha. Git features are Beta; Sync, Git + Sync, Lore, Backup and plugin features remain Alpha.
