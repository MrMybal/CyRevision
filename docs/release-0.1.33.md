# CyRevision 0.1.33 Alpha

- Start automatic release checks before loading plugins and project repositories.
- Show update-check progress, availability, up-to-date and retry states in a fixed status-bar area without shifting the workspace.
- Offer newer published releases with release notes, an explicit download-and-install action and a Not now option.
- Add Help menu entries to check for updates and reopen an available update, including when no project is selected.
- Keep update checks independent of repository operations and handle network failures without blocking startup.
- Preserve dismissed offers across project switches and prevent delayed responses from reopening the same prompt.
- Add English and French labels for the update controls.

Windows V2 requires Windows 10/11 x64 and Direct3D 11. Launch
`CyRevision.Native.exe` from the portable ZIP. The **CyRevision (Classic)**
shortcut and `CyRevision.Desktop.exe` remain available; Linux and macOS use
the Avalonia interface.

The V2 interface remains Alpha. Git features are Beta; Sync, Git + Sync, Lore,
Backup and plugin features remain Alpha.
