# CyRevision Collaboration for Unreal Engine

`CyRevisionUnreal` is an optional **Editor plugin** that also works on its own. Its revision dashboard runs Git directly from Unreal Editor, so CyRevision does not have to be open.

## Autonomous features

- **Revision Control > Connect to Revision Control** now lists **CyRevision** as a native provider;
- the native provider detects the project Git repository, branch and optional remote, and reports file states without mandatory checkout or permission changes;
- **Tools > CyRevision** groups revision, collaboration, connection, and Swarm commands in a dedicated category;
- the configurable main-toolbar button uses the CyRevision icon and can show or hide its name, or be hidden completely;
- **Tools > CyRevision > Revision dashboard** shows the branch, working-tree changes, and recent commits;
- stage all files, commit, fetch, pull, and push without leaving Unreal Editor;
- dedicated **All Git LFS locks**, **My Git LFS locks**, and **Work in progress** views keep real locks separate from non-blocking presence;
- advisory asset reservations from the Content Browser remain non-blocking;
- two people may report the same asset: a warning is shown, but nobody is prevented from editing;
- no Unreal checkout, permission change, or Git LFS lock is created;
- active markers are renewed every minute and expire after 30 minutes by default.
- the Content Browser right-click menu contains a **CyRevision** submenu for normal LFS lock/unlock, work-in-progress reporting, the lock lists, the dashboard, and the desktop client;
- **Tools > CyRevision > Swarm over VPN** configures `CoordinatorRemotingHost`, launches Agent/Coordinator, tests TCP 8008/8009, and includes a complete repair checklist without requiring the desktop client.

## Swarm over VPN

The standalone Swarm window works on Windows and uses the matching Engine `Binaries/DotNET` tools. It stores the chosen coordinator host in Unreal's per-project user settings, backs up `SwarmAgent.Options.xml` before changing its existing `CoordinatorRemotingHost` field, and never changes the modem/router or public network exposure.

The optional CyRevision desktop integration adds project-owned Windows Firewall and local DNS/hosts entries, WireGuard peer/handshake checks, configurable Agent groups/cache paths, and an actionable test report. TCP 8008/8009 must remain restricted to the WireGuard project subnet and must never be forwarded publicly.

## Connected features

When the optional **Unreal Engine Integration** plugin is enabled in CyRevision, it installs or updates this Editor plugin from the CyRevision interface and writes a private connection file under `Saved/CyRevision/bridge.json`.

The bridge:

- listens only on `127.0.0.1`;
- uses a random per-project bearer token;
- never stores the token in Git;
- lets Unreal notify CyRevision after revision or advisory changes;
- exposes the extended Git, LFS, Sync, backup, asset-diff, Swarm setup, and VPN file-exchange capabilities available in CyRevision.

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

## Engine and project compatibility

- the source plugin targets Unreal Engine **4.27 and 5.0 through 5.8**;
- C++ projects install the portable source plugin and compile it with their matching Unreal toolchain;
- Blueprint-only projects require an exact precompiled plugin for their Unreal minor version and operating system;
- the CyRevision interface detects the engine association and project type before installation, displays the full supported range, and blocks an unsafe or mismatched installation;
- the current Windows package contains locally compiled Win64 variants for Unreal Engine **5.2 through 5.8**;
- Unreal 4.27, 5.0, and 5.1 remain source-compatible targets, but their Blueprint-only Win64 variants are not yet bundled.

The precompiled package must match the exact Unreal Engine minor version. A C++ project remains the most portable option because its Editor target builds the plugin from source using the project's own engine and compiler.
