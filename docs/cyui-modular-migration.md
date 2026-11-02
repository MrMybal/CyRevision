# Native CyUI modular integration

## Dependency

The native frontend vendors CyUI 1.2 from commit
`ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3`, tree
`3727ad9da51a79367be2bfb0857f1dc7b8e52659`.
This is the selected committed local snapshot; the GitHub default branch was
still at `d4c7fe07f1a10dbbf5e5aceea973db9c64234177` when it was imported.
No change to the CyUI repository is needed to build this application.

`native/cyui.lock.json` records the original repository, exact commit/tree,
archive digest, exported paths and every vendored file's SHA-256 digest.
The build uses the checked-in snapshot without a network fetch. Vendored files
retain their original bytes across Windows/Linux checkouts. Do not edit them.
For an intentional update, review the new upstream revision and run:

```powershell
./scripts/update-cyui-snapshot.ps1 -SourceRepository <CyUI-checkout> -Revision <full-commit>
python scripts/test_cyui_modules.py
./scripts/build-native-v2.ps1 -BuildDirectory artifacts/native-v2-modular-build
```

The updater exports committed objects, not uncommitted files or build output.
`-CyUiPath` remains an explicit development override. It bypasses snapshot
verification and must provide the same module APIs; release builds use the
default verified snapshot.

## Selected modules

| Target | Migrated responsibility |
| --- | --- |
| Core | Existing themes/fonts/widgets/layout/notifications; per-context English/French controls and quick filtering |
| Code | Source documents, shared 101-language catalogue, syntax colors, source rendering and search |
| Diff | Git patch parsing, unified/split rendering, aligned rows, scroll synchronization and inline changes |
| CodeEditor | Selectable read-only source previews and editable conflict-result drafts, native Undo/Redo/find |
| Inspector | Shared field descriptors, password/read-only/choice controls and begin/update/commit/cancel drafts |
| DocumentViews | PR descriptions/comments, AI and team transcripts, summaries, documentation and release notes |
| Images | Common texture fit/zoom/pan/inspection for asset, diff and detached image previews |
| Canvas (automatic dependency) | Image navigation and co-change/Unreal relation-graph navigation/clipping |
| Gallery | The pre-existing live palette/theme editor only; the demo is disabled |

`CYUI_MODULES` is configured before `add_subdirectory`. Application targets link
explicit module targets, never the compatibility umbrella. Audio, standalone demo,
Web/CEF and unused plot/node/timeline/viewport adapters are not built. Gallery is
explicitly retained because removing it would remove the existing live theme editor.

One host-owned `cyrevision_imgui` target builds Dear ImGui 1.92+ and is supplied
through `CYUI_IMGUI_TARGET`. Neither a second ImGui instance nor imgui_demo.cpp is
compiled. The frontend requires C++20 because it uses CodeEditor; Core remains
C++17-compatible. The Windows `pascal` macro is isolated around Code includes
in the application, without changing the shared syntax catalogue.

## Application boundaries

The old `TextDocument` parser/lexer and custom `TextViewer` drawing implementation
were removed. `TextViewer` is now a small adapter around `CodeDocument`,
`DiffDocument`, `codeViewer` and `diffViewer`. It retains repository metadata,
copy/detach controls, bounded-preview notices and the application's previous
case-insensitive Enter/Shift+Enter/previous-match/hunk navigation. CyUI's built-in
Find-next button is case-sensitive; the host navigation supplements that public
API without copying the renderer.

CyUI 1.2's Diff renderer has a fixed 88dp gutter without a width option. The host
uses the supported scoped-font API to fit the old/new number format inside that
gutter, preventing overlap in comfortable density and at higher DPI. A future
shared gutter-width option would remove this small typography adaptation.

`SourceEditor` delegates editing to CodeEditor and translates UTF-8 glyph
positions back to the managed API's UTF-16 offsets, retaining CRLF and surrogate
pairs. A new document identity resets local Undo even for identical text.
Polling/echoing an unchanged document does not reset Undo.

CyRevision still owns all Git/review/conflict actions, authorization, file IO,
texture decoding/lifetimes, project services, history, validation and persistence.
Inspector previews never invoke a setter. A committed proposal invokes exactly
one setter; cancellation invokes none. External revisions cancel stale drafts.
Conflict text remains a draft until applied or used by an action. A subsequent
conflict action carries its draft in the same validated engine request, avoiding
an action on stale server text. Writing/staging still requires confirmation.

Documents return links to the host. Only HTTP/HTTPS navigation without embedded
credentials is accepted, and it requires confirmation before opening the browser.
Source, messages, paths, logs and identifiers are not translated. The language
choice is stored with native window/theme preferences; existing Classic/backend
labels outside the migrated controls retain their current localization coverage.

Each detached view owns its module view state. States are released on close and
reset when the active project changes. Message transcripts retain server paging
(50 messages per page, bounded message transfer) and raw/detail/attachment views.

## Deliberately retained special-purpose views

- The CI log table retains server paging, complete log access, separate Source
  and line-number columns, movable/resizable columns, and Error/Warning filters.
  Diagnostics' bounded generic log list does not supply that contract; replacing
  it would truncate logs or lose those interactions. No Diagnostics dependency
  is selected for this table.
- The project file tree/table retains paged filesystem queries, multiselection,
  Git/LFS/Sync history actions and domain-specific columns. Assets' in-memory
  browser does not provide this paged column/tree contract. No second asset
  registry or UI library is copied into the application.
- Commit-lane layout, authors/references, co-change layout and graph-to-history
  actions remain repository-specific. Relation-graph navigation now uses Canvas;
  no editable node/curve/timeline model is introduced for a read-only Git graph.
- Settings with custom browse/apply/cache-purge/shortcut capture behavior remain
  host forms using Core. Generic model field descriptors and the advanced
  Inspector share the Inspector adapter.
- No browser is embedded in these views. The native Markdown subset is not
  full HTML/CommonMark. No CEF runtime is added. The Unreal plugin uses its
  existing engine UI, not CyUI; this migration does not rebuild its DLLs or
  introduce an Unreal CyUI module.

## Verification

```powershell
./scripts/build-native-v2.ps1 -BuildDirectory artifacts/native-v2-modular-build
python scripts/test_cyui_modules.py --build-dir artifacts/native-v2-modular-build
dotnet test tests/CyRevision.Core.Tests/CyRevision.Core.Tests.csproj -c Release
python scripts/test_native_text_preview.py --kind json --themes mid-grey
python scripts/test_native_text_preview.py --kind ini --side-by-side
python scripts/test_native_text_preview.py --kind ini --themes light-grey --comfortable --locale fr
python scripts/test_native_backend.py --capture-native artifacts/native-v2/CyRevision.Native.exe
python scripts/test_native_conflict_editor.py
```

Native tests cover project/navigation/detached-window behavior, fixed loading
layout, shared diff models, bounded long lines, case-insensitive host search,
Unicode selection, editor transactions/Undo/Redo, Inspector cancellation and
single commit, language changes during editing, document rendering in five
themes and both densities, and DPI adaptation. Real captures use isolated
repositories/profiles rather than the user's projects. The native renderer is
Windows/D3D11-only, as before. Classic Avalonia Windows/Linux/macOS code paths
are preserved; Linux/macOS execution is not claimed by a Windows test run.

Packages install the CyUI, Dear ImGui, ImGuiColorTextEdit, Lexilla, Scintilla and
SciTE-property licenses plus the shared third-party notice. No CEF, audio runtime
or new engine binary is distributed by this integration.
