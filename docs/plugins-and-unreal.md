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

CyRevision detects the project's Unreal version and whether it is a C++ or Blueprint-only project before enabling installation. It displays the complete compatibility list and blocks a mismatched or unavailable package.

| Project type | Installation mode | Compatibility |
| --- | --- | --- |
| C++ | Portable source plugin, compiled by the project's Editor target | Unreal 4.27 and 5.0 through 5.8 |
| Blueprint-only | Exact precompiled plugin for the engine minor version and OS | Win64 variants currently bundled for Unreal 5.2 through 5.8 |

CyRevision copies the selected plugin into `Project/Plugins/CyRevisionUnreal`, adds an enabled plugin entry to the `.uproject` descriptor, and keeps a `.cyrevision.bak` copy of that descriptor. When updating an existing plugin, its previous directory is moved to `Saved/CyRevision/PluginBackups` before the new copy is activated.

C++ projects must regenerate project files and compile the Editor target. Blueprint-only projects do not compile the plugin, so CyRevision requires the exact precompiled variant and refuses installation when it is absent. Unreal 4.27, 5.0, and 5.1 are source-compatible targets, but their Blueprint-only Win64 variants are not yet bundled.

## Autonomous Unreal mode

The Unreal plugin does not require the desktop client. **Tools > Revision dashboard** can show repository status and recent revisions, stage all files, commit, fetch, pull, and push by using the configured Git executable. Advisory reservations also remain available locally or through the project's existing shared presence directory.

## Direct connection

**Tools > Swarm over VPN** is autonomous too. On Windows it can save the Coordinator VPN IPv4/DNS alias to the existing Swarm Agent options, create a `.cyrevision.bak` copy first, launch Agent or Coordinator, test TCP 8008/8009, and show a manual recovery guide. The optional desktop plugin adds WireGuard peer discovery, project-scoped firewall/DNS changes and a combined configuration diagnostic.

The CyRevision Unreal extension listens on `127.0.0.1:47832` only while it is enabled and CyRevision is open. Configuring a project creates a random token and writes it to `Saved/CyRevision/bridge.json`. This file is outside normal source-controlled project content and should remain excluded from Git.

Unreal sends the token as a Bearer credential. A successful connection lets it announce Git and advisory changes so CyRevision can refresh the matching project. The bridge advertises Swarm and VPN-file capabilities but does not grant them: no Git, LFS, Sync, backup, VPN, DNS, firewall or file-transfer permission is granted by the bridge token itself.

Disabling the CyRevision plugin stops and unloads the bridge. It does not remove `CyRevisionUnreal` from any project, and the Unreal-side autonomous tools continue to work.

## Local Unreal build lab

When the optional Unreal integration is enabled, **Extensions & Help > Unreal builds** discovers:

- Unreal installations registered with Epic Launcher or `Unreal Engine\Builds`;
- additional roots listed in `CYREVISION_UNREAL_ENGINE_ROOTS`;
- project targets declared by `*.Target.cs`;
- every project plugin containing a `.uplugin` descriptor.

The build lab can compile one engine or a selected engine-version range. It runs Unreal Build Tool for compile checks, Automation Tool `BuildPlugin` for plugins, and `BuildCookRun` when **Cook & package** is selected. Standard output and error are streamed into the project task and log views. Exit code `0` is success; every other code is reported as a failed row with its retained log path.

Discovery is cached below `Project/.cyrevision/cache/unreal` and invalidated when the `.uproject`, targets, plugins or toolchain environment changes. **Discover** forces a new scan when an installation was added manually. Named build presets retain the target, platform, configuration and toolchain choices per project.

The live log is a bounded, virtualized list rather than one continuously rebuilt text block. Compiler and Automation Tool output is also parsed into structured diagnostics with severity, file, line and error code. Version matrices remain sequential by default; an explicit parallel limit from 2 to 4 is available for machines with enough CPU, RAM and disk bandwidth.

Windows, Linux cross-compilation, and Android targets are available. CyRevision reads each engine's `Build.version`, recommends the matching Epic Linux toolchain/Clang generation, inspects `LINUX_MULTIARCH_ROOT`, `LINUX_ROOT`, AutoSDK, Android SDK/NDK and `JAVA_HOME`, and lets the user override those paths per project. Profiles remain local to CyRevision and build output defaults to `Saved/CyRevision/Builds`.
