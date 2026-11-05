---
id: perforce
title: Perforce workspace
description: Inspect changelists and reconcile with explicit server permissions.
section: Extensions
order: 40
revision: 1
---

## Connect deliberately {#connect-deliberately}
Enable the Perforce integration, select the project and configure the server/client/user settings exposed by the workspace. Confirm the client mapping points at the intended local files. Perforce and Git are different version-control systems; a Git remote is not a Perforce connection.

## Inspect before action {#inspect-before-action}
Refresh opened files, pending changelists and file history. Select a path to inspect its current depot/workspace context. Authentication, client mapping and server protections determine available operations.

## Sync, reconcile and submit {#sync-reconcile-and-submit}
1. Preserve unsaved editor buffers and local work.
2. Preview the intended sync or reconcile scope.
3. Confirm only the expected add/edit/delete paths.
4. Review the changelist and description before Submit.
5. Verify the server result and refresh the workspace.

Reconcile and Submit are not read-only diagnostics. Avoid applying a broad reconcile to build caches or generated assets. A submit changes the depot and may trigger automation. Resolve server-reported conflicts using the appropriate Perforce workflow rather than Git's conflict controls.

If a command fails, inspect client root, connection/authentication, installed p4 executable and server response. Do not change unrelated filesystem permissions as a substitute for a valid client mapping.

```image
{"id": "integrations", "src": "assets/integrations.en.svg", "alt": "Perforce workspace", "controls": "minimal"}
```

[See also](doc:plugins)
