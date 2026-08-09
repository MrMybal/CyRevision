# CyRevision

CyRevision is a modular desktop client for Git, Git LFS, peer-to-peer synchronization and configurable backups. Git, synchronization and backups are independent capabilities: a project can enable any useful combination without requiring GitHub or a permanent server.

## Solution layout

- `CyRevision.Desktop`: cross-platform Avalonia desktop application.
- `CyRevision.Core`: project profiles, feature flags and retention rules.
- `CyRevision.Git`: contracts for complete Git and Git LFS management.
- `CyRevision.Sync`: peer synchronization contracts and Syncthing isolation policy.
- `CyRevision.Backup`: snapshot and retention contracts.
- `CyRevision.Security`: project membership, device identity and peer admission.
- `CyRevision.Server`: optional headless Linux peer and administration API.
- `CyRevision.Core.Tests`: domain tests.

## Requirements

- .NET SDK 8.0 or later. The repository currently targets `net8.0` because it is available on the development machine; moving to .NET 10 LTS is planned before the first release.
- JetBrains Rider with the Avalonia plugin is recommended for XAML previewing.
- Git and Git LFS.

## Run

```powershell
dotnet restore CyRevision.sln
dotnet build CyRevision.sln
dotnet run --project src/CyRevision.Desktop/CyRevision.Desktop.csproj
```

Run the optional server:

```powershell
dotnet run --project src/CyRevision.Server/CyRevision.Server.csproj
```

## Safety rule

CyRevision never controls an existing Syncthing installation. When peer synchronization is enabled, it will start a dedicated instance with separate configuration, database, identity, ports and exchange directories. If no project has synchronization enabled, that instance is not started.

See [docs/architecture.md](docs/architecture.md) for the initial architecture decisions.

