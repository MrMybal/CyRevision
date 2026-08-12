# CyRevision Collaboration for Unreal Engine

`CyRevisionUnreal` is an optional **Editor plugin** that also works on its own. Its revision dashboard runs Git directly from Unreal Editor, so CyRevision does not have to be open.

## Autonomous features

- **Tools > Revision dashboard** shows the branch, working-tree changes, and recent commits;
- stage all files, commit, fetch, pull, and push without leaving Unreal Editor;
- advisory asset reservations from the Content Browser remain non-blocking;
- two people may report the same asset: a warning is shown, but nobody is prevented from editing;
- no Unreal checkout, permission change, or Git LFS lock is created;
- active markers are renewed every minute and expire after 30 minutes by default.

## Connected features

When the optional **Unreal Engine Integration** plugin is enabled in CyRevision, it installs or updates this Editor plugin from the CyRevision interface and writes a private connection file under `Saved/CyRevision/bridge.json`.

The bridge:

- listens only on `127.0.0.1`;
- uses a random per-project bearer token;
- never stores the token in Git;
- lets Unreal notify CyRevision after revision or advisory changes;
- exposes the extended Git, LFS, Sync, backup, and asset-diff capabilities available in CyRevision.

If CyRevision is closed or its plugin is disabled, the Unreal revision dashboard and local advisory reservations continue to work.

## Installation

Preferred installation:

1. Open **Plugins** in CyRevision.
2. Enable **Unreal Engine Integration**.
3. Select the `.uproject` file.
4. Choose **Install / update in project**.

Manual installation remains supported:

1. Copy `CyRevisionUnreal` into the Unreal project's `Plugins` directory.
2. Regenerate project files and compile the Editor target.
3. Optionally configure `ExecutablePath`, `GitExecutable`, `BridgeUrl`, and `BridgeToken` under `[CyRevision]` in the Editor per-project user settings.

The source targets the common Unreal Engine 5.3–5.6 Editor APIs. Precompiled binaries are intentionally not shipped because a project or engine build may require a matching toolchain.
