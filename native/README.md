# CyRevision V2 Alpha

The Windows frontend uses C++20, Dear ImGui docking, modular CyUI and Direct3D 11. It
starts the managed desktop engine with `--native-backend` and communicates using
newline-delimited JSON over inherited pipes. One engine is shared by the main
window and detached views; detached workspaces follow the active project.

Build from the repository root with `./scripts/build-native-v2.ps1`. Use
`-CyUiPath` to test an alternative CyUI checkout; release builds use the included
snapshot in `vendor/CyUI`. The pinned nlohmann/json header is downloaded into the
ignored `.tools` directory and verified before compilation.

CyUI 1.2 is pinned at `ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3` in
`cyui.lock.json`. Source, diff, editing, property, document and image views use
the selected shared modules. One host ImGui target is supplied to CyUI. Audio,
CEF and the demo are disabled; Gallery supplies the existing live theme editor.
The build verifies the dependency snapshot and installs all selected modules'
third-party notices. See [the migration guide](../docs/cyui-modular-migration.md)
for the adoption map, boundaries, dependency update and verification commands.

CyUI is available at [MrMybal/CyUI](https://github.com/MrMybal/CyUI) (currently
private) under the [MIT License](https://github.com/MrMybal/CyUI/blob/main/LICENSE).
Windows packages include the full license in `Licenses/CyUI.txt` and its source
reference in `Licenses/CyUI-Source.md`.

Run CTest through the build script for headless input regression coverage.
`python scripts/test_native_backend.py --capture-native artifacts/native-v2/CyRevision.Native.exe`
tests the real frontend/engine pair using temporary repositories and profiles,
including detached-window visibility while the main window is minimized.

The native frontend currently targets Windows x64. Closing its window hides the
main and auxiliary views in the system tray without stopping the engine or its
services. The tray menu restores the UI or explicitly quits through the normal
confirmation flow. Desktop preferences control tray residence and hidden startup;
Classic Avalonia remains available on all supported platforms.

Release checks start automatically before plugin and project loading. A fixed
status-bar slot shows checking, up-to-date, available and retry states without
moving the workspace. A newer published stable release opens an offer with notes,
an explicit install action and a Not now option. Declining it keeps the application
running and does not repeat the offer when switching projects. Use Help > Check
for updates to check again, or Help > View available update to reopen the offer.
Checks also work without a project and during repository operations; network
failures do not prevent startup. The release request times out after 20 seconds.
Downloading and closing the application still require explicit confirmation.

## Shared assistant, CLI and MCP runtime

CyMCPSDK 0.7.1 is pinned at `34d4bcae126bc83d1a2925b5047dac36e3d09d92`
in `cymcp.lock.json`. The application builds Core, Platform, Http, MCP and AI
from the vendored source snapshot, not an SDK download or precompiled bridge.
To refresh that snapshot from a local SDK checkout, use
`./scripts/vendor-cymcp.ps1 -Source ../CyMCPSDK -Revision FULL_COMMIT -Update`.
The script refuses modified vendored files; if the checkout is unavailable it
uses the authenticated Git remote. `scripts/verify-cymcp-snapshot.py` verifies
the pinned files before building with `scripts/build-cymcp.ps1` (Windows) or
`scripts/build-cymcp.sh` (Linux/macOS on their target platform).

Free is the default local MCP profile; the running UI displays its
`http://127.0.0.1:PORT/mcp` address. Free accepts that URL alone and uses the SDK's
`LocalAccessProfile::free`, tokenless loopback HTTP and `Policy::allow_all()`
registry. Argument validation and project/mode checks are unchanged.
Local controlled, Token only and Protected remain selectable. Choices are
persisted per project. A profile change blocks the old registry until SDK
restart or Start local MCP applies it. The CLI also accepts `--instance URL`
and `--access-profile free|local-controlled|token-only|protected`; token profiles
require a private descriptor or `--token` when attaching by URL.

Run `python scripts/test_cymcp_access_profiles.py` against the development
output in `artifacts/cymcp-access` for real desktop read/write, authentication,
CLI and local-fixture chat checks. No GitHub workflow is needed.
