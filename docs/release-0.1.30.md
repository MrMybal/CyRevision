# CyRevision 0.1.30 Alpha

- Introduced the Windows V2 interface built with Dear ImGui/CyUI, with a project-only sidebar, project-scoped workspaces, status indicators and orange active tabs.
- Added detachable workspaces, lists, file previews and diffs in independent windows, with dock/return actions and saved layouts. Detached views follow the active project and share its engine.
- Fixed project switching during background scans, history and comparison selection, stale file results and overlapping refresh requests.
- Added visible loading indicators, queued-action feedback and cancellation while retaining guards against duplicate operations.
- Ported Git/LFS, pull requests, CI logs, branch inspection, code search, asset previews, project services, plugins and preferences to the V2 workspace shell.
- Improved Git process cancellation and export handling, workspace invalidation and searches over changing directories.
- Fixed runtime restoration for self-contained desktop and agent packages after solution builds, including macOS Intel and Apple Silicon.

Windows installer shortcuts open the V2 interface; **CyRevision (Classic)** remains available for the previous Avalonia interface. In the portable ZIP, run `CyRevision.Native.exe` for V2 or `CyRevision.Desktop.exe` for Classic.

Linux and macOS continue to use the Avalonia interface. V2 requires Windows 10/11 x64 and Direct3D 11; native tray/start-hidden integration is not yet available.

The V2 interface remains Alpha. Git features are Beta; Sync, Git + Sync, Lore, Backup and plugin features remain Alpha.
