# CyRevision V2 Alpha

The Windows frontend uses C++17, Dear ImGui docking, CyUI and Direct3D 11. It
starts the managed desktop engine with `--native-backend` and communicates using
newline-delimited JSON over inherited pipes. One engine is shared by the main
window and detached views; detached workspaces follow the active project.

Build from the repository root with `./scripts/build-native-v2.ps1`. Use
`-CyUiPath` to test an alternative CyUI checkout; release builds use the included
snapshot in `vendor/CyUI`. The pinned nlohmann/json header is downloaded into the
ignored `.tools` directory and verified before compilation.

CyUI and Dear ImGui are included unchanged from the UI reference used by this
port. Their MIT notices are in `vendor/CyUI/LICENSE` and
`vendor/CyUI/third_party/imgui/LICENSE.txt`; the JSON notice is in `Licenses`.
The installer and portable package include these notices.

Run CTest through the build script for headless input regression coverage.
`python scripts/test_native_backend.py --capture-native artifacts/native-v2/CyRevision.Native.exe`
tests the real frontend/engine pair using temporary repositories and profiles,
including detached-window visibility while the main window is minimized.

The native frontend currently targets Windows x64. Classic Avalonia is retained
on all supported platforms; native tray/start-hidden integration is not yet
implemented.
