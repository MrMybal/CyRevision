# Creating a CyRevision release

CyRevision releases are self-contained: end users do not need to install the .NET runtime. Git, Git LFS, and Syncthing remain external tools because every project can enable or disable those modules independently. WireGuard may use either a system installation or a separately packaged, checksum-verified integrated runtime.

The autonomous Discord agent is published inside the `Agent` directory of every desktop package. Windows and macOS can launch it from the Discord tab. Debian packages also install the `cyrevision-discord-agent` command and a user-level systemd unit.

The optional `VpnRuntime/<RID>` directory is copied into releases. A production integrated-VPN package must provide the platform files and `runtime.json` checksums described in `src/CyRevision.Desktop/VpnRuntime/README.md`; release builds must never substitute an unofficial cryptographic implementation.

## Packages

The `Build multiplatform release` workflow creates:

| Platform | Architectures | Installer | Portable package |
| --- | --- | --- | --- |
| Windows 10/11 | x64 | `CyRevision-Setup-<version>-win-x64.exe` | `.zip` |
| Debian/Ubuntu Linux | x64, ARM64 | `.deb` | `.tar.gz` |
| macOS 11 or newer | Intel x64, Apple Silicon ARM64 | `.dmg` | `.zip` containing `CyRevision.app` |

Every build also produces a SHA-256 checksum file. The GitHub Release combines them into `SHA256SUMS.txt`.

## Build locally on Windows

Requirements:

- .NET SDK 8 or newer;
- Inno Setup 6 (`choco install innosetup -y`);
- PowerShell 7 or Windows PowerShell 5.1.

Run from the repository root:

```powershell
./scripts/build-release.cmd 0.1.0
```

The output is written to `artifacts/release/0.1.0/`.

To test a current-user installation without prompts:

```powershell
./artifacts/release/0.1.0/CyRevision-Setup-0.1.0-win-x64.exe `
  /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /LOG
```

## Build locally on Linux

Requirements are the .NET 8 SDK, `dpkg-deb`, `tar`, and standard Unix utilities.

```bash
bash scripts/build-linux-release.sh 0.1.0 linux-x64
# Or build the ARM64 package:
bash scripts/build-linux-release.sh 0.1.0 linux-arm64
```

Install the Debian package with:

```bash
sudo apt install ./artifacts/release/0.1.0/CyRevision-0.1.0-linux-x64.deb
```

## Build locally on macOS

Requirements are the .NET 8 SDK and the macOS command-line tools (`sips`, `iconutil`, `codesign`, `hdiutil`, and `ditto`).

```bash
bash scripts/build-macos-release.sh 0.1.0 osx-arm64
# Use osx-x64 for an Intel Mac package.
```

The DMG presents `CyRevision.app` and an `Applications` shortcut for drag-and-drop installation.

## Build and publish with GitHub Actions

The workflow can be started manually with **Publish release** disabled. This builds all packages and keeps them as downloadable workflow artifacts without creating a public GitHub Release.

Pushing a semantic version tag builds every platform and publishes the GitHub Release automatically:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The validation job builds the complete solution and runs the test suite before any installer is published. Each native package is then created on a matching GitHub-hosted operating system.

## Code signing and trust warnings

Packages are unsigned by default:

- Windows SmartScreen can display an unknown-publisher warning;
- macOS receives an ad-hoc signature for bundle integrity, but Gatekeeper can still warn because the app is not notarized;
- Linux package integrity is covered by the published SHA-256 hashes, not by a distribution repository signature.

Production signing should use protected CI secrets or hardware-backed credentials. Never commit a certificate, private key, Apple ID password, or notarization token to this repository.
