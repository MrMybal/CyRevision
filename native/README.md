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
