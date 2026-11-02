# CyRevision 0.1.32 Alpha

- Migrated Windows V2 code, diff, conflict editing, property forms, documents, conversations and image navigation to the modular CyUI 1.2 architecture.
- Improved JSON, INI and source-code previews with syntax highlighting, aligned comparisons, line-number gutters and in-file search.
- Added bounded previews for large untracked text files and support for Unicode configuration files, with explicit truncation notices.
- Preserved editor Undo/Redo across engine refreshes and applied conflict-result drafts together with subsequent validated actions.
- Added persistent English/French controls for the migrated views.
- Separated project navigation from action toolbars, stabilized loading indicators and improved status colours across native themes.
- Included CyUI source and license references alongside the bundled third-party notices.

Windows V2 requires Windows 10/11 x64 and Direct3D 11. In the portable ZIP, launch
`CyRevision.Native.exe`; `CyRevision.Desktop.exe` and the **CyRevision (Classic)**
installer shortcut remain available for the Avalonia interface. Linux and macOS
continue to use Avalonia. CyUI's repository currently requires access permission;
the complete MIT license is included in the Windows package's `Licenses` folder.

The V2 interface remains Alpha. Git features are Beta; Sync, Git + Sync, Lore,
Backup and plugin features remain Alpha.
