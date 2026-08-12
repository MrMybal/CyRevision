# CyRevision plugins and Unreal integration

CyRevision plugins are separate .NET assemblies discovered from the `Plugins` directory beside the application. Their manifests use `cyrevision-plugin.json`, and enabled plugin identifiers are stored in the current user's CyRevision configuration. A packaged plugin may therefore remain present without being loaded.

The release contains two distinct Unreal components:

| Component | Runs in | Required |
| --- | --- | --- |
| `CyRevision.Plugin.Unreal` | CyRevision | No; disabled by default |
| `CyRevisionUnreal` | Unreal Editor | No; autonomous after installation |

## Installing the Unreal Editor plugin

1. Open **Plugins** in CyRevision.
2. Select **Unreal Engine Integration** and choose **Enable**.
3. Select a `.uproject` file.
4. Choose **Install / update in project**.

CyRevision copies the bundled source plugin into `Project/Plugins/CyRevisionUnreal`, adds an enabled plugin entry to the `.uproject` descriptor, and keeps a `.cyrevision.bak` copy of that descriptor. When updating an existing plugin, its previous directory is moved to `Saved/CyRevision/PluginBackups` before the new copy is activated.

Unreal project files must be regenerated and the Editor target compiled when the installed plugin does not already have binaries compatible with the project's Unreal version and toolchain.

## Autonomous Unreal mode

The Unreal plugin does not require the desktop client. **Tools > Revision dashboard** can show repository status and recent revisions, stage all files, commit, fetch, pull, and push by using the configured Git executable. Advisory reservations also remain available locally or through the project's existing shared presence directory.

## Direct connection

**Tools > Swarm over VPN** is autonomous too. On Windows it can save the Coordinator VPN IPv4/DNS alias to the existing Swarm Agent options, create a `.cyrevision.bak` copy first, launch Agent or Coordinator, test TCP 8008/8009, and show a manual recovery guide. The optional desktop plugin adds WireGuard peer discovery, project-scoped firewall/DNS changes and a combined configuration diagnostic.

The CyRevision Unreal extension listens on `127.0.0.1:47832` only while it is enabled and CyRevision is open. Configuring a project creates a random token and writes it to `Saved/CyRevision/bridge.json`. This file is outside normal source-controlled project content and should remain excluded from Git.

Unreal sends the token as a Bearer credential. A successful connection lets it announce Git and advisory changes so CyRevision can refresh the matching project. The bridge advertises Swarm and VPN-file capabilities but does not grant them: no Git, LFS, Sync, backup, VPN, DNS, firewall or file-transfer permission is granted by the bridge token itself.

Disabling the CyRevision plugin stops and unloads the bridge. It does not remove `CyRevisionUnreal` from any project, and the Unreal-side autonomous tools continue to work.
