# CyRevision

<p align="center">
  <img src="src/CyRevision.Desktop/Assets/Branding/cyrevision-logo-concept.png" alt="CyRevision logo" width="420">
</p>

**English** · [Français](README.fr.md)

CyRevision is a revision control, synchronization, and backup client designed for large projects, especially Unreal Engine productions. It does not require GitHub: Git and Git LFS remain local, while optional peer-to-peer synchronization transports verified transactions between authorized devices.

> CyRevision is currently under active development. The core workflows are functional, but production hardening and large-scale multi-device testing are still in progress.

## Current capabilities

- Native Avalonia desktop application for Windows, Linux, and macOS.
- Five project modes: **Git**, **Git + Sync**, **Sync**, **Sync + Versions**, and **Backup**.
- Local Git workflows: status, staging, commits, branches, merges, remotes, Git LFS, an interactive history explorer, A ↔ B comparisons, and per-file history.
- LFS Time Machine with object history, local/peer/archive availability, signed on-demand requests, resumable transfers, texture previews, export, and confirmed restore operations.
- Optional Git visualizations for commit history, co-change relations, team activity, and simplified engine-independent Unreal dependencies.
- Intelligent peer-to-peer Git exchange using signed bundles and inventories, configurable LFS priorities, resumable transfers, and SHA-256 verification without synchronizing the active `.git` directory.
- Optional Syncthing integration with a dedicated profile, identity, database, loopback API, and port for every CyRevision project.
- Secure peer admission with single-use invitations, out-of-band verification codes, ECDSA certificates, roles, and revocation.
- SHA-256 deduplicated snapshots with restore, retention policies, and non-destructive cold-archive copies.
- Configurable smart synchronization planning without implicitly starting Syncthing.
- Engine-independent comparisons for text, textures and heatmaps, OBJ geometry, binary files, and simplified `.uasset`/`.umap` inspection.
- Optional Linux server with an API, scheduled backups, scheduled Git exchange, and a protected web dashboard.
- Optional Unreal Editor plugin for opening CyRevision and publishing non-blocking advisory asset reservations.
- Optional WireGuard integration with guided setup, isolated tunnels, VPN-only peers, and Unreal Swarm/CI/service profiles.
- Extensible localization: English is the default, French is included, and additional JSON catalogs can be added to the desktop client and web dashboard.
- Searchable offline documentation bundled with the desktop application in English and French.
- Built-in stable-release updater with platform package selection and mandatory SHA-256 verification; commits, drafts, and prereleases are ignored.

## Open in Rider

Open `CyRevision.sln` in JetBrains Rider. The solution currently targets **.NET 8**. A future migration to .NET 10 LTS can be considered once all supported environments provide it.

### Requirements

- .NET SDK 8 or newer.
- Git.
- Git LFS.
- Syncthing only when a Sync mode is enabled.
- WireGuard only when the VPN module is enabled.
- The Avalonia plugin for Rider is recommended for XAML previews but is not required.

## Build and run

```powershell
dotnet restore CyRevision.sln
dotnet build CyRevision.sln
dotnet test CyRevision.sln
dotnet run --project src/CyRevision.Desktop/CyRevision.Desktop.csproj
```

Publish on Windows:

```powershell
./scripts/publish.ps1
```

Build a self-contained Windows installer and portable release locally:

```powershell
./scripts/build-release.cmd 0.1.0
```

Native Linux (`.deb`) and macOS (`.dmg`) packages are built by the multiplatform GitHub Actions release workflow. They can also be built on their native operating system with `scripts/build-linux-release.sh` and `scripts/build-macos-release.sh`. See [Creating a release](docs/releasing.md).

Publish on Linux:

```bash
./scripts/publish.sh
```

## Syncthing safety rule

CyRevision never discovers, configures, or stops an existing personal Syncthing installation. It starts a process only after Sync has been enabled and a Syncthing executable has been selected explicitly. CyRevision can stop only the exact child process instance it created.

In **Git + Sync** mode, the shared directory contains signed Git bundles, membership certificates, signed LFS inventories, and immutable content-addressed LFS objects. The working tree and active `.git` directory remain local. In **Sync without Git** mode, Syncthing shares the selected project directory directly.

## Documentation

Detailed documentation is currently available in French while its English translation is being prepared:

- [User guide](docs/user-guide.md)
- [Architecture](docs/architecture.md)
- [Security](docs/security.md)
- [Optional Linux server](docs/linux-server.md)
- [Engine-independent asset diff](docs/asset-diff.md)
- [Server API](docs/server-api.md)
- [WireGuard VPN](docs/wireguard-vpn.md)
- [Git visualizations](docs/git-visualizations.md)
- [Git explorer and LFS Time Machine](docs/git-explorer-lfs-time-machine.md)
- [Smart synchronization and cold archive](docs/smart-sync-and-cold-archive.md)
- [Localization](docs/localization.md)
- [Creating a multiplatform release](docs/releasing.md)
- [Unreal Editor bridge](plugins/CyRevisionUnreal/README.md)

## License

CyRevision is licensed under the [GNU Affero General Public License v3.0](LICENSE).
