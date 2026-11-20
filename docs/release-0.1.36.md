# CyRevision 0.1.36 Alpha

[Français](https://github.com/MrMybal/CyRevision/blob/v0.1.36/docs/release-0.1.36.fr.md) · [Release and downloads](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.36)

## Changes

- Update the source-built CyMCPSDK integration to 0.7.1, pinned at `34d4bcae126bc83d1a2925b5047dac36e3d09d92`.
- Make **Free** the default shared assistant/CLI/MCP access profile. Local HTTP clients can use the displayed `http://127.0.0.1:PORT/mcp` URL without a token or additional operation confirmation.
- Keep **Local controlled**, **Token only** and **Protected** available. Migrate settings without a choice to Free and preserve explicit project choices.
- Separate HTTP authentication from operation grants. Keep argument schemas, project boundaries, stale-target checks, plugin/mode availability and emergency blocking in every profile.
- Block old sessions after a profile change until restarting the SDK or starting local MCP applies the choice. Retained resources follow the same project/profile revocation.
- Allow the attached CLI to connect with a loopback URL or an instance descriptor, and report missing/invalid credentials for token profiles correctly.
- Update the English and French bundled manual with profile comparisons, migration behavior and CLI/MCP connection examples.

## Packages and requirements

This release provides **Windows x64** installer and portable ZIP packages, with `SHA256SUMS.txt`. Both Native V2 and Classic, the CLI companion and the complete offline manual are included. The packages include the .NET runtime; Git, Git LFS and optional external providers/services retain their own requirements.

Linux and macOS source support remains available. Their last published packages are [0.1.34](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34); they have not been rebuilt or tested for this release.

## Documented limitations

- Free deliberately lets local processes call every exposed operation using the loopback URL alone. Choose Protected for both token authentication and exact operation approval, or Local controlled for operation approval without a token.
- Provider-native shell/system permissions and account/executable configuration remain separate from the application registry. Free does not grant arbitrary operating-system access to a provider.
- Changing the active profile requires **Restart SDK runtime** or **Start local MCP**; reconnect clients to the new endpoint afterwards.
- Native V2, Sync, Git + Sync, Lore, backups and plugins remain Alpha; Git workflows remain Beta. Keep independent backups and test recovery before production use.
- Windows packages are unsigned. Native V2 requires Windows 10/11 x64 and Direct3D 11.
- Provider accounts, production CI and operating-system VPN/firewall changes still require environment-specific validation. PR/CI and model integration scenarios use local fixtures; cancellation cannot undo an external request already accepted by a service.
- Online documentation is deployed separately. The updated reader is included in this application's `Documentation` directory and Help/F1.

[Installation](https://github.com/MrMybal/CyRevision/blob/v0.1.36/README.md#installation) · [Issue tracker](https://github.com/MrMybal/CyRevision/issues)
