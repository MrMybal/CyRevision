# CyUI optional modules

CyUI 1.2 provides a common Dear ImGui foundation and independently selectable
interface modules. All CyUI code is MIT licensed. Applications own their models,
engines, files, services, validation and Undo history.

## Localization in every integration

Core includes English (default), French and extensible translation catalogs.
Use stable translated labels when migrating application interfaces. C++, Python,
Web and Unreal usage, font requirements and host responsibilities are described
in [LOCALIZATION.md](LOCALIZATION.md).

## Targets and dependencies

`CyUI::Core` contains themes, localization, quick filters, fonts, icons, ordinary widgets, layout and
notifications. Modules link Core automatically. The following targets are implemented:

Menu/panel filtering is opt-in through Core's `TextFilter`, `filterBox`,
`filterMenu` and the filtered panel header. Inspector accepts an optional filter
without adding a dependency. See [FILTERING.md](FILTERING.md).

| CMake target | Public header | Interface | Additional CyUI dependency |
| --- | --- | --- | --- |
| `CyUI::Audio` | `modules/Audio.h` | Knobs, faders, peak/RMS meters | Core |
| `CyUI::Canvas` | `modules/Canvas.h` | Double coordinates, grid, clipping, pan/zoom | Core |
| `CyUI::Plots` | `modules/Plots.h` | Linear/log axes, lines, steps, points, bars, histograms, heatmaps | Canvas |
| `CyUI::Curves` | `modules/Curves.h` | Keyframes, Bezier handles, selection, snap, copy/paste | Plots |
| `CyUI::Nodes` | `modules/Nodes.h` | Cards, pins, links, layout, move/connect/remove requests | Canvas |
| `CyUI::Timeline` | `modules/Timeline.h` | Lanes, clips, markers, ruler, move/trim, range selection | Canvas |
| `CyUI::AudioViews` | `modules/AudioViews.h` | Waveforms, spectrum, oscilloscope, stereo scope | Plots |
| `CyUI::Code` | `modules/Code.h` | Source viewer, 101 syntax modes, extensible registry, semantic overlays, annotations, search | Core + selected Lexilla engines + system threads |
| `CyUI::Diff` | `modules/Diff.h` | Unified/split diff, line numbers, hunk navigation, inline ranges | Code |
| `CyUI::CodeEditor` | `modules/CodeEditor.h` | Native editable source, Undo/Redo, multiple cursors, completion hooks | Code + MIT TextEditor, C++20 |
| `CyUI::Inspector` | `modules/Inspector.h` | Typed properties, exact int64, validation, draft edits | Core |
| `CyUI::Assets` | `modules/Assets.h` | Hierarchical folder navigation, grid/list, search, thumbnails, drag/drop | Core |
| `CyUI::Images` | `modules/Images.h` | Texture viewport, checkerboard, crop, gradient stops | Canvas |
| `CyUI::Viewport` | `modules/Viewport.h` | Host-rendered scene surface, orbit camera, 2D transform overlay | Images |
| `CyUI::Commands` | `modules/Commands.h` | Searchable keyboard command palette | Core |
| `CyUI::Diagnostics` | `modules/Diagnostics.h` | Bounded thread-safe logs, filters, diagnostic navigation | Core + system threads |
| `CyUI::DocumentViews` | `modules/DocumentViews.h` | Markdown subset, fenced code, links, tables, cached transcripts | Code |
| `CyUI::Web` | `modules/Web.h` | Browser panel, texture/input contract, origin policy, messages, theme bridge | Core |
| `CyUI::Gallery` | `Gallery.h` | Examples and theme editor | Enabled modules only |

Header paths are relative to `include/cyui/`. `CyUI::Code::Diff` is an alias for
`CyUI::Diff`. `CyUI::CyUI` is the compatibility umbrella linking all modules
selected in this build. A module is a static library, not a dynamically loaded plugin.

Optional external adapters:

| Target | Enablement | Dependency |
| --- | --- | --- |
| `CyUI::ImGuizmo` | Set `CYUI_IMGUIZMO_TARGET` to an existing host target; select Viewport | Host's MIT ImGuizmo; `adapters/ImGuizmo.h` |
| `CyUI::WebCEF` | `CYUI_BUILD_WEB_CEF=ON`; select Web | Host CEF SDK/runtime; `adapters/WebCEF.h`, C++20 |

Neither adapter is downloaded or enabled by `CYUI_MODULES=All`. The Web module
is usable with a host backend; the supplied browser implementation is CEF.
See [WEB.md](WEB.md) for platform support, process lifecycle and deployment.
WebCEF hands the host a texture `release` callback, reports `external` requests
with `userGesture`/`mainFrame`/`sourceOrigin` (schemes limited by
`WebPolicy::externalSchemes`) and counts discarded events in `dropped` events.
The supplied bridge and helper run CEF unsandboxed on Windows (see WEB.md).

The optional [Unreal Editor module](UNREAL.md) packages selected CyUI modules for
embedding in an existing plugin's Source/CyUI folder or sharing through one provider
plugin. It provides a Slate renderer/input backend without menus or demo windows.
Prepare it separately with `tools/package_unreal.py --layout module` (default);
it is not a CMake target or a dependency of Core. A redistributed plugin embeds it
under a unique name with `--module-name`, or depends on the shared provider.

## Select only what an application needs

```cmake
set(CYUI_IMGUI_TARGET app_imgui)
set(CYUI_BUILD_DEMO OFF CACHE BOOL "" FORCE)
set(CYUI_BUILD_AUDIO OFF CACHE BOOL "" FORCE)
set(CYUI_BUILD_GALLERY OFF CACHE BOOL "" FORCE)
set(CYUI_MODULES "Code;Diff;Inspector" CACHE STRING "" FORCE)
add_subdirectory(external/CyUI)
target_link_libraries(app PRIVATE CyUI::Diff CyUI::Inspector)
```

```cpp
#include <cyui/Core.h>
#include <cyui/modules/Diff.h>
#include <cyui/modules/Inspector.h>
```

Dependencies are selected automatically: Diff enables Code; it does not enable
CodeEditor, audio, plots or a browser. Link explicit targets to keep public feature
macros and dependencies restricted to the consumer. `CYUI_HAS_CODE`,
`CYUI_HAS_DIFF`, etc. are public definitions of their respective targets.

`CYUI_MODULES` accepts a semicolon list, `Core`, an empty list, or `All`.
The standalone full demo defaults to All. Embedded builds and standalone builds
with the demo disabled default to no new optional modules. Audio and Gallery
retain their original ON defaults; disable those explicitly when not used.
`CyUI::Core` never links them. The old Widgets.h/CyUI.h declarations remain source compatible.

Without CMake, compile the three Core sources and only the selected
`src/modules/<Name>.cpp` files plus their dependency closure. Define the matching
`CYUI_HAS_<NAME>` macros for Gallery/CyUI.h. CodeEditor also needs TextEditor.cpp,
its include directory and C++20. Code and its dependents additionally compile
`src/modules/code/*.cpp`, the bundled `third_party/lexilla/lexlib/*.cxx` and
`lexers/*.cxx`, with Lexilla include/lexlib and Scintilla include directories.
Define `CYUI_CODE_LANGUAGE_CATALOGUE=1` for the extended C++ catalogue API. `build_demo.bat` is the complete Windows demo build.

## Shared data and edit contract

- Give every object a stable, nonzero uint64 ID: results that return a bare ID
  (command palette, log/diagnostic rows, plot series, annotations) use 0 for none.
  Gestures track their targets with flags, so a 0 ID still drags and selects. Keep
  one state object per view.
- Wheel zoom and pan keep ranges usable: a step that would overflow a log axis or
  shrink a span below what its magnitude resolves (|value| x 1e-12) is ignored.
  Selection ranges are reported ordered (min <= max).
- ArrayView and callbacks borrow data for the call; keep it alive while drawing.
- Curve points are finite and sorted by time; timeline durations are positive.
  Units, snapping policy, graph types and interpolation belong to the host.
- Editors return `Edit<T>` proposals with `begin`, `update`, `commit`, `cancel`.
  Atomic actions may return commit directly. Preview state never mutates the host.
  Validate and apply a committed proposal as one Undo transaction. Increment the
  supplied revision when replacing host data during a gesture to cancel stale edits.
- Ordinary rendering and model preparation run on the host UI thread. LogBuffer
  append/clear/snapshot are synchronized and may be called from producer threads.
- Colors use palette roles; UI geometry uses dp/style. Gradient colors, plot
  values, textures and scene matrices are application data.
- No module reads projects, executes Git actions, starts a terminal process,
  decodes audio/video, computes FFTs or runs a language server. Clipboard actions
  are local UI requests. Web explicitly creates a browser only when selected.

Prepare documents/envelopes when data changes. Draw visible rows using cached
models, and supply visible/bounded datasets for graphs and timelines. Plots
accept at most 65,536 points per series by default and report oversized IDs in
`skippedSeries`; call reducePlot or query a host index before drawing. Heatmaps
bound the displayed cells to 256 per axis. Diff uses patience anchors and bounded
LCS (4 million cells by default), exposing `approximate()` when an unmatched
region is displayed as a full replacement. Both source sides are retained; CRLF and
LF lines compare equal and a notice row reports differing line endings.
Source/Markdown inputs are limited to 32 MiB; large files need host paging.
Logs default to 10,000 rows / 8 MiB. Waveforms consume prepared min/max envelopes.

## Python

```sh
python -m pip install -e ".[ui]"
```

`cyui` remains the existing theme/widget API. `cyui_modules` contains optional
`audio`, `canvas`, `plots`, `curves`, `nodes`, `timeline`, `audioviews`, `code`,
`diff`, `inspector`, `assets`, `images`, `viewport`, `commands`, `diagnostics`,
`documents` (`documentviews` alias), `web` and `cef`. Import models without loading
imgui-bundle; drawing imports it lazily. No NumPy, media decoder or 3D engine is required.

Python uses the same IDs, ranges, palette roles, edit phases, origin policy and
bounded diff algorithm. Its CodeEditor uses ImGui multiline editing with native
Undo and a live syntax preview. The richer C++ TextEditor backend has its own API;
Python does not pretend to expose its multiple cursors/completion hooks.
`cyui_modules.cef.CEFRuntime` loads the optional C ABI library/helper explicitly;
CEF binaries are not included in the Python package. See [WEB.md](WEB.md).

## Application survey, 2026-11-02

The survey covers first-party interface source trees and their build dependencies,
with focused inspection of the components below. Generated output, installed
runtimes and bundled dependencies are excluded from the feature inventory.
This is a source survey, not execution or validation of those applications.

Paths in this table are relative to each named application's repository.

| Application | Relevant source examples | Reusable presentation | Keep in the application |
| --- | --- | --- | --- |
| CyDAW | `src/v2/CurveEditor.*`, `DeviceVisuals.*`, `GraphEditor.*`, `GraphLayout.*`, `ArrangementView.cpp`, `AudioEditorView.cpp` | Editable automation curves, graph cards and links, timelines, waveform and device displays | JUCE bindings, audio taps, FFT/DSP, effect catalogue, automation interpolation and project Undo |
| Shaderixel | `src/ui/NodeEditor.cpp`, `CodeEdit.*`, `GradientEditor.cpp`, `Analysis.cpp`, `Canvas.cpp` | Node canvas, annotated shader code, gradient stops, histograms, image scopes, zoom and overlays | Shader compilation, effect graph evaluation, document model, GPU device and image-processing algorithms |
| ShaderixFrame | `src/ui/Timeline.cpp`, `CurveEditor.cpp`, `LayerCanvas.*`, `DspCanvas.*`, `CommandPalette.cpp`, `src/core/CanvasView.h`, `src/adapters/CyDawWidgets.*` | Clip lanes, ruler, markers, keyframes, graph navigation and command search | Video decoding, rendering, frame rate/timecode policy, adapters, project history and DSP graph |
| CyGameSDK | `app/native/GraphView.cpp`, `Content.cpp`, `Viewport.cpp`, `Gizmo.cpp`, `Editor.cpp` | Typed pin presentation, asset cards/tree, property forms, viewport surface and gizmo adapter | Graph interpreter/types, asset registry, scene model, validation, engine adapters and game formats |
| CyCode / CyCodeReader | `app/src/EditorTheme.cpp`, `EditorLanguage.cpp`, `Syntax.cpp`, `HighlightUI.cpp`, `TerminalUI.cpp`, `VcsUI.cpp`, `apps/CyCodeReader/Reader.cpp` | Code editor/viewer, token colors, diagnostics, gutters, search, diff presentation and terminal surface | Document revisions, LSP/DAP clients, language servers, compilers, process execution, VCS and terminal process |
| CyTools | AudioLibrary `gui/waveform_view.py`; AudioStudio `gui/timeline.py`, `effect_ui.py`, `knobs.py`; MeshStudio `gui/widgets.py`; AIChat `gui/chat_panel.py`; Storyboard `gui/layout_editor.py`; ComfyFlow `result_viewer.py` | Peak envelope view, audio plots, timelines, schema forms, result cards, image/video surfaces, transcript and viewport presentation | Audio decoding/cache storage, inference, providers, model installation, jobs, permissions, media decoding and offscreen rendering |
| CyInsights | `App/InsightsViews.cpp`, `CounterViews.cpp`, `AnalysisViews.cpp`, `OvrHeatmapView.hpp`, `ActivityViews.cpp`, `ComparisonViews.cpp` | Timing tracks, overview/range selection, sampled counter plots, histograms, heatmaps and comparison tables | Trace readers/indexes, aggregation, capture, counter precision/availability, GPU measurements and background jobs |
| CyRevision | `native/CyRevision.Native/TextDocument.*`, `TextViewer.cpp`, `GitViews.cpp`, `GitConflictViews.cpp`, `NativeWidgets.cpp` | Unified/split diffs, synchronized scrolling, hunk navigation, lazy syntax spans, commit lanes and descriptor forms | Git/review/merge actions, server state, network/authentication and repository history queries |

CyTools also includes Manager, MiniMaxH3, PersonaRP, Subtitle, VideoToGaussian and
VideoToStereo3D. Their common forms, progress, previews, logs and update panels
reinforce the same families. Product-specific model and installation behavior
does not belong in CyUI. The PersonaRP Colab tree is a packaging variant.

ShaderixFrame already has explicit CyDAW widget and Shaderixel graph adaptations.
Those are evidence of repeated UI work, but also of coupling: the complete editor
classes cannot be moved into CyUI with their current engine/document dependencies.
CyRevision adds an optional Diff component to the Code family. Code reading,
editing and comparison should share token roles and line/document views while
remaining selectable independently.

## Adoption map

| Application | Suggested selected modules |
| --- | --- |
| CyDAW / AudioStudio | Audio, AudioViews, Curves, Nodes, Timeline, Inspector, Diagnostics |
| Shaderixel / ShaderixFrame | Nodes, Curves, Timeline, Code, CodeEditor, Images, Viewport, Plots, Commands |
| CyGameSDK | Nodes, Inspector, Assets, Viewport, optional ImGuizmo adapter |
| CyCode / CyCodeReader | Code, optional CodeEditor, Diff, Diagnostics, Commands |
| CyRevision | Code, Diff, Inspector, DocumentViews |
| CyInsights | Timeline, Plots, Inspector, Diagnostics |
| CyTools | Choose audio/media/scene/document modules per tool; Python ports are selectable |
| CyMD / CyAnnota / CyTask / CyAIOrchestrator | Web + optional WebCEF for web panels; DocumentViews for the native Markdown subset |
| CyImgView | Images, Viewport, Inspector; Web only for an embedded web tool |

The additional web survey found CyMD's versioned iframe protocol in
`src/platform/embedded.ts`, CyAnnota's React web build plus Electron desktop
bridge, CyTask's web app and plugin frontends, and CyAIOrchestrator's web assets
and optional pywebview host. CyImgView is a native C++ application. Browser UIs
can be hosted by CEF; Electron IPC and server dependencies need host adapters.
No source application is changed by this library update, and no application
GPL/AGPL implementation is copied into these modules.

## Validation

```sh
cmake -S . -B build/check -DCYUI_BUILD_DEMO=OFF -DCYUI_MODULES=All -DCYUI_BUILD_TESTS=ON
cmake --build build/check --config Release
ctest --test-dir build/check -C Release --output-on-failure
python -B tests/test_python_modules.py
node --test tests/web_bridge.test.mjs
python -B tests/module_matrix.py
```

module_matrix.py supports `--generator`, `--make-program`, `--cmake` and an
optional prebuilt host `--imgui-library` to avoid recompiling ImGui in each variant.
It builds, links and runs each module separately and checks the dependency closure.
The feature tests include real ImGui frames and gestures, UTF-8/CRLF/tab round trips,
revision cancellation, exact int64 properties, bounded concurrent logs and diff
reconstruction properties. `tests/capture_gallery.py` covers tabs 0–21 across all
five themes and both densities with the Windows demo.

GitHub Actions is configured for Windows, Linux and macOS native builds. A workflow
file is not evidence that remote jobs have run. The local build/render validation
for this revision uses Windows/MSVC; platform-specific CEF packaging still needs
validation on each target OS. See [WEB.md](WEB.md) and [LICENSING.md](LICENSING.md).

See [CODE.md](CODE.md) for the C++ syntax catalogue, plugin extension contract,
UTF-8 span rules, caching and reproducible lexer updates.
