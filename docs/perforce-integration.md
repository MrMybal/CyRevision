# Perforce Helix Core integration

CyRevision ships an optional, project-scoped Perforce plugin. It uses the official `p4` command-line client and does not embed a Perforce server or an alternative protocol implementation.

## Safety model

- The plugin is disabled by default and must be enabled separately for each CyRevision project.
- Enabling it does not initialize, convert, reconcile, sync, or otherwise modify the project.
- CyRevision stores only the executable path and the `P4PORT`, `P4USER`, and `P4CLIENT` coordinates in its application configuration.
- Passwords, tickets, and trust material remain managed by the official Perforce tools and are never written to the project or the CyRevision plugin settings.
- Workspace-writing actions are disabled by default. The user must validate the server, login ticket, and workspace mapping, then explicitly enable writes.
- Reconcile and sync offer non-writing previews. Reconcile, sync, revert, and submit require confirmation in the desktop interface.
- Every file operation is restricted to the selected CyRevision project root.

## Available tools

- detect the official `p4` CLI;
- validate server access, authentication, and the selected client workspace;
- list files opened by the current or other workspaces;
- search opened files by path, action, user, workspace, or changelist;
- list pending and recently submitted changelists;
- inspect a selected file's revision history;
- preview and apply `p4 reconcile`;
- open a selected project file for edit;
- revert unchanged files or explicitly revert a selected file;
- preview and apply workspace sync;
- submit the default or a selected numbered changelist.

## First configuration

1. Install the Helix Core Command-Line Client and authenticate from a trusted terminal with `p4 login`.
2. Enable **Perforce Helix Core** from **Extensions & Help > Plugins** for the selected project.
3. Enter or verify `P4PORT`, `P4USER`, and `P4CLIENT` in the Perforce tab.
4. Select **Validate & refresh**. CyRevision verifies that the client root contains the selected project.
5. Only if the result is correct, enable **Enable workspace writes** and save the project configuration.

The plugin also contributes a dedicated **Perforce** operating mode. This mode disables Git features, keeps backups available, and exposes the Perforce, solution explorer, console, and backup tools.
