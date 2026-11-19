# Creating a CyRevision release

CyRevision releases are self-contained: end users do not need to install the .NET runtime. Git and Git LFS are installed separately; official Syncthing runtimes are included for the optional Sync modes. WireGuard may use either a system installation or a separately packaged, checksum-verified integrated runtime.

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
- Visual Studio C++ desktop tools and CMake for the Windows V2 interface;
- Inno Setup 6 (`choco install innosetup -y`);
- PowerShell 7 or Windows PowerShell 5.1.

Run from the repository root:

```powershell
./scripts/build-release.cmd 0.1.0
```

The output is written to `artifacts/release/0.1.0/`.

Windows packages contain both `CyRevision.Native.exe` (V2 Alpha) and
`CyRevision.Desktop.exe` (Classic and the managed engine). The default installer
shortcut starts V2; a separate Classic shortcut remains available. The native
build verifies the checked-in CyUI snapshot, selects explicit module targets and
installs their license notices (including editor and syntax dependencies). See
[CyUI modular integration](cyui-modular-migration.md) for update and verification commands.
Linux and macOS retain the Avalonia interface.

Release builders also compile the CyMCPSDK source revision pinned in `native/cymcp.lock.json` and include the platform native bridge, notices and `Cli/CyRevision.Cli` companion. This requires CMake 3.24+, C++20 and, on Linux/macOS, libcurl development files. For a direct development build, prepare the native SDK first with `scripts/build-cymcp.ps1` or `scripts/build-cymcp.sh`; supplying `CyMcpNativeRuntimeDirectory` requires the correct target architecture.

## Local publication without GitHub Actions

Build packages on the available target operating system. A Windows-only release must identify that scope in both READMEs and release notes; retain older Linux/macOS links rather than relabeling their existing archives.

Generate the bilingual manual with `node .CyDocBuild/build.mjs`, run local tests and commit the selected release sources. To intentionally avoid push-triggered Actions for that publication, include `[skip ci]` in the release commit message and do not dispatch workflows. Create the version tag on that same commit. Publish only newly built packages using the GitHub Releases API or CLI, with `SHA256SUMS.txt` covering the exact uploaded assets. Do not overwrite an existing release or draft.

The local package builder does not publish the Pages site. Its `pages.yml` workflow is a separate deployment; skipping Actions leaves the existing online reader in place while the new manual is bundled in application packages.

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

If a runner image changes after a tag is created, start the current workflow manually with that tag in **source_ref**, its version number, and **Publish release** enabled. Every job checks out the same resolved source SHA; publication requires it to match the existing version tag. This rebuilds the tagged code without moving tags or replacing an existing release. macOS builds use macos-15-intel for Intel packages and macos-15 for Apple Silicon.

## Code signing and trust warnings

Packages are unsigned by default:

- Windows SmartScreen can display an unknown-publisher warning;
- macOS receives an ad-hoc signature for bundle integrity, but Gatekeeper can still warn because the app is not notarized;
- Linux package integrity is covered by the published SHA-256 hashes, not by a distribution repository signature.

Production signing should use protected CI secrets or hardware-backed credentials. Never commit a certificate, private key, Apple ID password, or notarization token to this repository.
