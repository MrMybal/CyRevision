# CyRevision 0.1.35 Alpha

[Français](https://github.com/MrMybal/CyRevision/blob/v0.1.35/docs/release-0.1.35.fr.md) · [Release and downloads](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.35)

## Changes

- Add the CyMCPSDK assistant: provider/model discovery, streaming replies, cancellation, attachments and saved conversation checkpoints.
- Share real project operations between the assistant, `Cli/CyRevision.Cli` and authenticated local MCP clients.
- Connect 264 reviewed desktop actions covering Git/LFS, PR/CI, Sync/VPN/Swarm, backups, tasks, plugins, builds and preferences.
- Add independent grants for navigation, publication, destructive operations, service control, administration and builds. Review exact targets and arguments in an application-wide confirmation window.
- Reject stale selections/drafts, propagate business failures and preserve host-controlled credentials and executable configuration.
- Add task-provider search and task links in PR/commit drafts, plus Git checkout, push/pull, merge and conflict recovery.
- Update the bundled CyDoc manual to 87 pages per language, in English and French, including assistant setup, CLI/MCP examples and operational limits.

## Packages and requirements

This release provides **Windows x64** packages: installer, portable ZIP and SHA-256 sums. Both Native V2 (`CyRevision.Native.exe`) and Classic (`CyRevision.Desktop.exe`) are included, with the CLI companion under `Cli`. Packages are self-contained; Git/Git LFS and optional provider or service tools retain their own requirements.

Linux and macOS source support is retained, but their last published packages are [0.1.34](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34). They have not been rebuilt or tested for this release.

The updated HTML reader is bundled at `Documentation/Documentation.html`, with its complete `DocData` folder. Open Help > User guide or F1. Online documentation is deployed independently of application packages.

## Documented limitations

- Native V2, Sync, Git + Sync, Lore, backups and plugins remain Alpha; Git workflows remain Beta. Keep independent backups and validate recovery before production use.
- Native V2 requires Windows 10/11 x64 and Direct3D 11. Windows packages are unsigned and may show operating-system trust warnings.
- Live `app.*` operations require an open project and the host's grants/confirmations. Headless Git/LFS/file/backup operations do not expose every desktop service.
- Account credentials, identity enrollment, assistant self-permissions, arbitrary shell execution and executable installation remain host-owned workflows.
- Provider accounts, real CI services and operating-system VPN/firewall changes require configuration and environment-specific validation. Automated PR/CI/task scenarios use local API fixtures.
- Cancellation cannot undo a request already accepted by an external service.

[Installation](https://github.com/MrMybal/CyRevision/blob/v0.1.35/README.md#installation) · [Issue tracker](https://github.com/MrMybal/CyRevision/issues)
