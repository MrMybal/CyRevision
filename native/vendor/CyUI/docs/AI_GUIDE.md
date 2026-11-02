# CyUI — guide for AI agents

You are restyling, or building, a Dear ImGui desktop interface. Use **CyUI**
(for example, in `external/CyUI`). Read this whole file first. The code of
every example is in `src/Gallery.cpp`.

CyUI provides a common visual language and optional modules: one semantic
palette, consistent sizes, fonts, controls and reusable specialized views.
It sits **on top of** Dear ImGui. It does not replace it, so
stock ImGui calls keep working and pick up the theme.

---

## 1. Integrate (once per app)

**Localization is required for interface text:** keep English as the default and
French as the second built-in language. Read [LOCALIZATION.md](LOCALIZATION.md).
Use `cyui::trLabel("English label")` for controls/tabs/windows and
`cyui::tr("English text")` for display text. Register the application's messages
and provide a language choice; do not translate user content, paths or IDs.
Core includes localization automatically, without an extra module dependency.

**Requirements:** Dear ImGui **1.92 or newer** (the docking branch is
recommended) and C++17 (C++20 when CodeEditor or the optional CEF adapter is selected).

### CMake

```cmake
set(CYUI_IMGUI_TARGET <the app's imgui target>)   # e.g. cydaw_imgui
add_subdirectory(external/CyUI ${CMAKE_BINARY_DIR}/cyui)
target_link_libraries(<app> PRIVATE CyUI::CyUI)
```

`CyUI::CyUI` is the compatibility umbrella. For selective integration use
`<cyui/Core.h>` and `CyUI::Core`. Audio controls live in
`<cyui/modules/Audio.h>` / `CyUI::Audio`; the gallery and theme editor live in
`<cyui/Gallery.h>` / `CyUI::Gallery`. Audio and Gallery default to enabled;
disable `CYUI_BUILD_AUDIO` / `CYUI_BUILD_GALLERY` when not needed, and disable
`CYUI_BUILD_DEMO` for a selective build. Core never links those modules.
See [MODULES.md](MODULES.md) for complete examples, the application survey and
the implemented optional targets and their exact dependency boundaries.

For example, a source-reading application selects only Code and Diff:

```cmake
set(CYUI_BUILD_DEMO OFF CACHE BOOL "" FORCE)
set(CYUI_BUILD_AUDIO OFF CACHE BOOL "" FORCE)
set(CYUI_BUILD_GALLERY OFF CACHE BOOL "" FORCE)
set(CYUI_MODULES "Code;Diff" CACHE STRING "" FORCE)
# add_subdirectory(...) after these options
target_link_libraries(app PRIVATE CyUI::Diff)
```

### Without CMake

Add `include/` to the include path. Compile `src/Theme.cpp`, `src/Widgets.cpp`,
`src/Layout.cpp`, `src/Filter.cpp` alongside the app's ImGui sources for Core. Add
`src/modules/Audio.cpp` for audio controls, and `src/Gallery.cpp` for the gallery.
Define `CYUI_HAS_AUDIO=1` when compiling Gallery with audio examples.

### Startup

Do this after `ImGui::CreateContext()`, before the first `NewFrame`, and
**instead of** the app's old theme function (`ImGui::StyleColorsDark`,
`ApplyTheme`, a hand-written `style.Colors[...]` block):

```cpp
#include <cyui/CyUI.h>

cyui::Config config;
config.themeKey = "mid-grey";            // see the theme table below
config.density  = cyui::Density::compact; // or comfortable
config.dpiScale = dpiScale;              // 1.0 at 96 dpi
cyui::apply(config);
cyui::loadFonts();                       // Segoe UI + Semibold + Consolas + MDL2 icons
```

When the DPI changes (`WM_DPICHANGED`), call `cyui::applyDpi(newScale)`. It
does nothing when the scale is unchanged. `apply` and a real DPI change rebuild
the whole `ImGuiStyle`, so make your own style tweaks after them. At the end of
every frame, after everything else, call `cyui::renderNotifications()`. It
draws the toasts.

`Config` also has `squareWindows`: square, opaque windows for apps whose ImGui
windows are OS windows (multi-viewports, plug-in editors). `FontConfig` has
`size` (0: the density's font size), `fallbackFolder` (a folder of Inter,
Roboto, JetBrains Mono or Cousine `.ttf` files, tried when the Windows fonts
are missing) and `icons` (merge Segoe MDL2 Assets; on by default).

**Several ImGui contexts** (plug-in windows, editor panels): everything CyUI
keeps is per context and released when the context is destroyed. That covers
the palette, theme key, density, DPI, fonts, typed entry and toasts. Call
`apply` and `loadFonts` with each context current. Theme calls change the
current context only. With **no** context current, `setPalette`, `setTheme`
and `setDensity` change every live context, and the defaults new contexts
start from. Make such calls while no other thread draws with those contexts.
`apply` and `applyDpi` with no context current change the defaults only.

| themeKey      | look                                       |
|---------------|--------------------------------------------|
| `mid-grey`    | Mid greys, orange accent (default)          |
| `light-grey`  | Light greys, dark text, orange accent       |
| `amber-night` | Deep blue-greys, amber accent               |
| `violet`      | Dark slate, violet accent, cyan info        |
| `navy`        | Deep navy, blue accent, flat                |

Use `compact` density for dense pro tools (DAW, editors). Use `comfortable`
for launchers, hubs and settings-heavy apps.

---

## 2. The rules (what makes it look right)

1. **Never hard-code a colour.** Take every colour from `cyui::palette()`,
   by its role:
   - `bg0` is the deepest ground: canvases, timelines, wells, the status bar.
   - `bg1` is the panel ground.
   - `bg2` is for headers, cards and popups.
   - `bg3` is for fields and buttons at rest; `bg4` when hovered; `bg5` when
     pressed or active.
   - `line` and `lineStrong` are for lines.
   - `text`, `textDim` and `textFaint` are the three text levels.
   - `accent`, `accentHover`, `accentDim`, `accentText` and `selection`: there
     is **one** accent, used only for what is active, selected or the main
     action.
   - `success`, `warning`, `error` and `info` carry meaning.

   For a variant, use `cyui::alpha / mix / brighter / darker`. For series of
   things (tracks, layers, tags), use `cyui::categorical(i)`.
2. **Never hard-code a size in pixels.** Wrap it: `cyui::dp(12.0f)`,
   `cyui::dp(8, 4)`. Prefer sizes derived from the style:
   `ImGui::GetFrameHeight()`, `ImGui::GetFontSize()`, `ImGui::GetStyle()...`.
3. **Text hierarchy.**
   - `cyui::heading(t, 1|2|3)` for titles.
   - `cyui::sectionTitle` for groups in a panel.
   - `cyui::overline("UPPERCASE")` for compact uppercase labels.
   - `cyui::textDim` for labels and secondary info.
   - `cyui::textFaint` for hints.

   Do not make titles with `ImGui::Text` plus a colour push.
4. **One primary button per view.** Use `cyui::primaryButton` for the main
   action, `cyui::button` (secondary) for the rest, `cyui::ghostButton` or
   `cyui::iconButton` in toolbars, and `cyui::dangerButton` for destructive
   actions.
5. **Toolbars are icon buttons.** Use `cyui::iconButton(icon::x, "Tooltip
   (Shortcut)", active)`. Separate groups with `cyui::toolbarSeparator()`.
   Every icon button **must** have a tooltip that names the action and its
   shortcut.
6. **Settings and inspectors are property tables**
   (`beginProperties` / `property` / `endProperties`), inside collapsible
   sections (`beginSection`). Do not use free-floating
   `ImGui::Text("Label:"); ImGui::SameLine(); ImGui::SliderFloat(...)` lines.
7. **Group related content in cards** (`beginCard` / `endCard`). Use toned
   cards (`Tone::accent / warning / error`) only to draw attention.
8. **Empty panels say why.** Use `cyui::emptyState("No layers yet", "Drop an
   image or press Ctrl+N", icon::photo)`. Never leave a blank panel.
9. **Feedback is visible.**
   - `cyui::notify("Saved", Tone::success)` for events.
   - `cyui::callout(Tone::warning, ...)` for standing conditions.
   - `progressBar` / `spinner` for work in progress.
   - `badge` and `statusDot` for state.
10. **Tooltips are delayed and wrapped.** Use `cyui::tooltip(text)` after an
    item. Use `cyui::helpMarker(text)` to explain an option.
11. Keep stock ImGui wherever it already reads well: `Checkbox`, `InputText`,
    `ColorEdit`, tables, trees, menus, tab bars, `DragFloat2`. The theme
    styles them. Do not wrap or reinvent them.
12. Prefer the RAII helpers `ScopedColor`, `ScopedVar`, `ScopedFont`,
    `ScopedID` and `ScopedDisabled` over manual Push/Pop pairs.

---

## 3. Migrating an existing ImGui interface

Work panel by panel. Change the look, not the behaviour. Keep IDs, labels
and logic as they are.

| Stock / old code | CyUI |
|---|---|
| `ImGui::StyleColorsDark(); style.Colors[...] = ...` (theme function) | delete it; `cyui::apply(config); cyui::loadFonts();` |
| `IM_COL32(40,42,50,255)` or `ImVec4(...)` literals in drawing code | `cyui::palette().bg3` (pick by role) |
| `10.0f` pixel literals in custom drawing | `cyui::dp(10.0f)` |
| `ImGui::Button("OK")` main action of a dialog/panel | `cyui::primaryButton("OK")` |
| `ImGui::Button("Delete")` | `cyui::dangerButton("Delete")` |
| `ImGui::Button("X")` / `ImGui::SmallButton` in toolbars | `cyui::iconButton(cyui::icon::close, "Close")` |
| `ImGui::Button(icon)` + `if (selected) PushStyleColor(...)` tool buttons | `cyui::iconButton(icon, tip, selected)` |
| `ImGui::Checkbox` for an on/off **setting** | `cyui::toggleSwitch("Label", &v)` (keep `Checkbox` in lists/tables) |
| row of `RadioButton`s / buttons for a mode | `cyui::segmented("##mode", labels, n, &mode)` |
| `ImGui::Text("Section"); ImGui::Separator();` | `cyui::sectionTitle("Section")` or `cyui::beginSection("Section")` |
| `ImGui::TextColored(gray, ...)` | `cyui::textDim(...)` / `cyui::textFaint(...)` |
| `ImGui::Text("Name:"); SameLine(); InputText(...)` stacks | `beginProperties` + `property("Name")` + the widget |
| `ImGui::SliderFloat` in an inspector | `cyui::sliderBar(id, &v, min, max, "%.2f", -1)` (or keep SliderFloat) |
| `ImGui::InputText` used as a filter | `cyui::searchField("##filter", buf, size, "Search")` |
| `ImGui::BeginChild(..., true)` used as a box | `cyui::beginCard(id, "Title")` ... `cyui::endCard()` |
| `ImGui::ProgressBar` | `cyui::progressBar(f, size, overlay, tone)` |
| `ImGui::OpenPopup` + `BeginPopupModal` confirm dialog | `cyui::beginModal` + `cyui::modalButtons` |
| status messages printed in a panel | `cyui::notify(...)` |
| custom main-window bars with `SetNextWindowPos` hacks | `cyui::beginTopBar` / `beginStatusBar` / `beginSidebar` |
| audio knobs / faders / meters drawn by hand | `cyui::knob` / `cyui::fader` / `cyui::meter` |

Custom-drawn views (canvases, node editors, timelines, curves) keep their
drawing code. Swap their colour constants for palette roles:
- grounds: `bg0`
- grid: `grid` / `gridStrong`
- selection: `selection` plus an `accent` outline
- text: `text` / `textDim`
- handles: `bg5`, or `accent` when active

Check your work: open `cyui::showGallery()` from a debug menu, switch themes
and densities, and look at the app in **all five themes**. Nothing may stay
dark-on-dark or light-on-light.

---

## 4. API reference (short)

All functions are in `namespace cyui`. The headers carry the full comments.

**Units.** Widget `size` and `width` parameters are screen pixels: wrap design
values in `dp()`, as in `cyui::button("OK", cyui::dp(80, 0))`. 0 means
automatic, and a negative width fills the row minus that much. These are
design units, scaled inside:
- `beginCard` height
- `beginProperties` label width
- `beginSidebar` and `beginModal` widths
- `space` height, `divider` spacing, `spinner` radius
- `KnobStyle::diameter`

`beginTopBar`'s height is in pixels.

**Disabled and edited.** Inside `ScopedDisabled`, CyUI's hand-drawn widgets
fade and stop reacting, like stock ImGui. Every CyUI control that changes a
value marks it edited, so `ImGui::IsItemEdited()` is true after it on that
frame. Clicks and drags also give `ImGui::IsItemDeactivatedAfterEdit()` on
release: one undo step per gesture. Wheel nudges and typed values are one-shot
edits that only set `IsItemEdited()`. `markEdited(id)` does the same for your
own controls.

### Theme (`Theme.h`)

- **Setup:** `apply(Config)`, `applyDpi(s)`, `dpi()` (the current scale),
  `loadFonts(FontConfig)`.
- **Palette:** `palette()`, `setPalette(p)`, `setTheme("key")`,
  `builtInThemes()`, `currentThemeKey()`. `paletteFields()` lists each role
  with its key, label, group and member pointer, for editors and theme files.
- **Density and metrics:** `setDensity(d)`, `density()`, `metrics()`. The
  metrics give the font size, paddings, roundings, `cardPadding` and
  `toolbarHeight`.
- **Fonts:** `uiFont()`, `boldFont()`, `monoFont()`, `iconsAvailable()`. The
  fonts are null until `loadFonts` filled the context's atlas.
  `ScopedFont(nullptr, factor)` then keeps the current font.
- **Colour helpers:** `rgb(0xRRGGBB, a)`, `alpha`, `mix`, `brighter`,
  `darker`, `vec`, `categorical(i)`. `categorical` gives 12 hues derived from
  the palette, toned for light or dark grounds.
- **Sizes:** `dp(v)` / `dp(x, y)`.
- **Theme files:** `savePalette(path, p)`, `loadPalette(path, p)`,
  `paletteToText`, `paletteFromText`. The `.cytheme` format is
  `key = #RRGGBB[AA]`; CyDAW's JSON `.cytheme` files also load.

### Icons (`Icons.h`)

`cyui::icon::play, pause, stop, record, add, remove, trash, settings, search,
folder, save, undo, redo, edit, close, check, chevronDown, more, refresh,
zoomIn, lock, filter, loop, library, star, copy, paste, info, warning, home,
globe, people, mail, calendar, clock, cloud, upload, download, code, photo,
document, ...` (104 icons). They are UTF-8 strings of Segoe MDL2 Assets, so
they work in any label: `std::string(icon::save) + "  Save"`. They draw only
on Windows. Without the icon font, the CyUI widgets fall back:
- `iconButton` shows the first letter of its tooltip when the current font
  lacks the glyph (a host's own merged icon font still works).
- `iconTextButton` shows the label alone.
- `sidebarItem` and `emptyState` leave the icon out.
- `helpMarker` shows `(?)`.

Guard your own icon labels with `iconsAvailable()`.

### Text (`CoreWidgets.h`)

- `heading(t, level)`, `sectionTitle(t)`, `overline(T)`
- `textDim(fmt, ...)`, `textFaint(fmt, ...)`, `textColoured(c, fmt, ...)`,
  `textMono(fmt, ...)`
- `kbd("Ctrl+S")`, `helpMarker(t)`, `tooltip(t)`
- `clippedText(list, pos, w, c, t)`, `centredText(list, min, max, c, t)`

### Buttons

- `toneColour(Tone)`: the palette colour of a tone (neutral is `textDim`).
- `button(label, Variant, size)`. The variants are `secondary`, `primary`,
  `ghost`, `outline`, `danger` and `success`.
- Shortcuts: `primaryButton`, `dangerButton`, `ghostButton`.
- `iconTextButton(icon, label, variant)`
- `iconButton(icon, tooltip, active, activeColour, size)`
- `toggleButton(label, &b, onColour, size, tooltip)`: compact pills such as
  M, S and R.
- `toggleSwitch(label, &b)`
- `segmented(id, labels, n, &current, width)`. A width of `-1` fills the row.
- `linkText(label)`

### Values

- `knob(id, &v, min, max, default, KnobStyle{diameter, colour, label, format,
  unit})`. Set `KnobStyle.flags` to `knobBipolar`, `knobLogarithmic`,
  `knobInteger` or `knobNoLabel`. `.steps` and `.modulation` are also
  available.
- `fader(id, &dB, minDb, maxDb, size, colour)`
- `meter(id, size, peakL, peakR, rmsL, rmsR, vertical)`
- `dragValue(id, &v, speed, min, max, fmt, width)`. `speed` is per pixel
  dragged. Once the field has the focus (clicked or navigated to), a wheel
  notch moves 10 × `speed`; before that, the wheel scrolls the panel.
  `min >= max` is unbounded.
- `sliderBar(id, &v, min, max, fmt, width, colour)`. A click or a drag sets the
  value. A wheel notch moves 1 % of the range.
- `valueEntry(...)`: the typed number field these controls open.
  `valueEntryOpen()` says whether one is open (to hold back shortcuts while
  the user types).
- `itemWheel()`: the wheel for your own control, claimed in time so the panel
  under it does not scroll. Also `takeWheel()`, `wheelTaken()` and
  `valueSpeed()`.

The value controls share these interactions:
- drag to change; Ctrl is fine (× 0.15), Shift is fast (× 4)
- the mouse wheel nudges the value and does not scroll the panel under it
  (a `dragValue` only once it has the focus)
- a double click types the value (`dragValue`, `sliderBar`) or resets it (a
  knob)
- a click on a knob's value types it

### Fields

- `searchField(id, buf, size, hint, width)`
- `textField(id, buf, size, hint, width)`
- `combo(id, &i, items, n, width)`

### Quick filtering (`Filter.h`, included in Core)

Provide a quick filter in menus and panels where users need to find actions or
properties. Keep a `TextFilter` in each view's state. `filterBox(id, state)` draws
the query field; `panelHeader(title, subtitle, &state)` includes it in the heading.
Use `state.matches(englishLabel, keywordsAndSectionPath)` before drawing a row.
Search uses all words, ignoring ASCII/Latin case and accents, and checks both the
English key and the current translation. Hidden ImGui IDs are excluded.

`beginFilteredSection(label, state, hasMatchingChildren, defaultOpen)` reveals a
matching section without changing its saved collapse preference; pair with
`endSection()` when true. `filterMenu(id, state, entries)` draws a searchable menu
inside an already open ImGui menu/popup. Its `FilterMenuItem` tree retains action
IDs, disabled/checked states and shortcuts. Searching lists matching descendants
with breadcrumbs. The host handles the returned action ID.

`InspectorOptions{&state, sectionTitle}` enables filtering in `inspector()`;
`propertyMatches(state, property, sectionTitle)` helps choose visible sections.
Call Inspector for a section's full property model so hidden active edits can be
cancelled; when omitting an entire panel, cancel its host previews explicitly.
Read [FILTERING.md](FILTERING.md) for complete C++ and Python examples and limits.
These helpers do not intercept raw ImGui calls or patch Dear ImGui.

### Containers

- `beginCard(id, title, height, tone)` / `endCard()`. **Always** call
  `endCard`, whatever `beginCard` returned.
- `beginSection(label, defaultOpen)` / `endSection()`. Call `endSection`
  **only when** `beginSection` returned true.
- `beginProperties(id, labelWidth)` / `property(label, help)` /
  `endProperties()`. Call `endProperties` only when `beginProperties`
  returned true. `property()` sets up full width for the next widget.
- `toolbarSeparator()`, `divider(spacing)`, `space(h)`

### Status

- `badge(t, Tone)`, `statusDot(c, tip)`
- `progressBar(f, size, overlay, Tone)`. A fraction `f < 0` gives an
  indeterminate bar.
- `spinner(id, r, c)`
- `emptyState(title, msg, icon)`
- `callout(Tone, text)`
- `colourChip(id, c, size)`
- `notify(text, Tone, seconds)` shows a toast in the current ImGui context.
  With no context current, it goes to the one context CyUI knows. When there
  are several or none, it asserts in debug builds and drops the toast.
  `notify(context, text, Tone, seconds)` posts to a given context, from any
  thread. Call `renderNotifications()` once per frame. At most six toasts
  show at a time.

### Layout (`Layout.h`)

The application shell. Call these in the frame in this order: menu bar, top
bar, status bar, sidebar, then dock space.

- `beginTopBar(id, h)` / `endTopBar()`
- `beginStatusBar(id)` / `endStatusBar()`, with `statusText(Tone, fmt, ...)`
- `beginSidebar(id, w)` / `endSidebar()`, with `sidebarHeader(title, badge)`,
  `sidebarGroup(LABEL)` and `sidebarItem(icon, label, active, badge)`
- `dockSpace(id, flags)` (docking branch)
- `panelHeader(title, subtitle)`
- `panelHeader(title, subtitle, &filter)` to add a quick filter
- `beginToolbar(id)` / `endToolbar()`
- `beginModal(title, &open, width)` / `endModal()`. Call `endModal` only
  when `beginModal` returned true. `modalButtons(confirm, cancel, danger)`
  returns 1 for confirm and 2 for cancel. Enter confirms and Escape cancels.
  Modals can nest: each `modalButtons` closes its own modal.
- `alignRight(width)`, `buttonWidth(label)`

All `begin*Bar` / `beginSidebar` / `beginToolbar` must be paired with their
`end*`, whatever they returned. Popups and tooltips opened from a bar keep the
theme's style.

### Gallery (`Gallery.h`)

- `showGallery(&open, selectTab)`: every widget, a theme switcher and a
  density switcher. `selectTab` brings a page to the front once:
  - 0 text, 1 buttons, 2 controls, 3 containers
  - 4–19 the optional modules
  - 20 layout (the application shell), 21 filtering (menus and panels)
- `showGallery(&open, selectTab, GalleryResources)`: the same, with host
  resources for the module pages: `texture`, `width` and `height` (Images,
  Viewport), `web` (a live Web backend) and `previewCommands`.
- `showThemeEditor(&open)`: live palette editing and `.cytheme`
  save and load.

---

## 5. Recipes

**Inspector panel**
```cpp
if (ImGui::Begin("Inspector")) {
    cyui::panelHeader("Layer", layer.name.c_str());
    cyui::divider();
    if (cyui::beginSection("Transform")) {
        if (cyui::beginProperties("##transform")) {
            cyui::property("Position");  ImGui::DragFloat2("##pos", &layer.x);
            cyui::property("Opacity");   cyui::sliderBar("##opacity", &layer.opacity, 0, 1, "%.2f", -1);
            cyui::property("Visible");   cyui::toggleSwitch("##visible", &layer.visible);
            cyui::property("Blend", "How the layer mixes with the ones under it.");
            cyui::combo("##blend", &layer.blend, blendNames, blendCount);
            cyui::endProperties();
        }
        cyui::endSection();
    }
}
ImGui::End();
```

**Tool palette**
```cpp
for (int i = 0; i < toolCount; ++i) {
    if (cyui::iconButton(tools[i].icon, tools[i].tooltip, current == i))
        current = i;
    ImGui::SameLine(0, cyui::dp(2));
}
cyui::toolbarSeparator();
```

**Confirm dialog**
```cpp
if (cyui::beginModal("Discard changes?", &askDiscard)) {
    ImGui::TextWrapped("The project has unsaved changes.");
    if (cyui::modalButtons("Discard", "Cancel", true) == 1)
        discard();
    cyui::endModal();
}
```

**Custom canvas colours**
```cpp
const auto& p = cyui::palette();
draw->AddRectFilled(min, max, p.bg0);
for (...) draw->AddLine(a, b, major ? p.gridStrong : p.grid);
draw->AddRectFilled(selMin, selMax, p.selection);
draw->AddRect(selMin, selMax, p.accent, 0, 0, cyui::dp(1.5f));
```

---

## 6. Do not

- Do not edit `third_party/imgui`. It is a copy used only by the demo; apps
  use their own ImGui.
- Do not add a second accent colour, gradients or drop shadows. The look is
  flat, with one accent.
- Do not call `ImGui::StyleColors*` or write `style.Colors[...]` after
  `cyui::apply`; that undoes the theme. To change colours, use `setPalette`.
- Do not use `ImGui::SetWindowFontScale`. Use
  `cyui::ScopedFont(font, factor)`, which calls
  `PushFont(font, FontSizeBase * factor)`.
- Do not put a CyUI widget's ID in its visible label when the label changes.
  Use `"Label###stableId"`.

## 7. Build and check

- `build_demo.bat` builds `build/cyui_demo.exe` with MSVC 2022 (no CMake
  needed).
- `cyui_demo.exe --capture out.bmp --theme navy --tab 2 [--comfortable]`
  renders 40 frames, writes the window to a BMP and quits. Use it to check a
  theme visually without a person at the screen. `--tab` takes a gallery
  page, 0 to 21.
- Add `--gallery-only` to show the gallery across the window instead of the
  full demo's docking layout. This also works interactively and does not change
  the library's rendering backend.
- `--gallery-only --tab 7 --image-graph` selects the image-processing node
  example, whose previews are generated by the demo host. The Assets and Inspector
  pages (13 and 12) show a content browser and grouped editor details panel.
- `python tools/capture_readme.py` regenerates the README's native screenshots
  from the built demo (requires Pillow). It captures synthetic fixtures for code,
  diff, audio monitors, a multi-node processing graph, frequency response,
  automation curves and a timeline, plus theme previews.
- Screenshots are in `docs/screenshots/`.


## 8. Optional module API and recipes

All headers below use the `cyui/` prefix. Link the corresponding target described
in [MODULES.md](MODULES.md). The examples for every widget are in Gallery.cpp,
tabs 4–19; the optional 3D adapter example appears in Viewport when enabled.
Use the same palette/dp/style rules as Core for host callbacks and overlays.

### IDs, state and edits

`Interaction.h` defines `ItemId` (uint64), `Point` (double x/y), `Range` (min/max),
`Rect` (screen bounds), borrowed `ArrayView<T>` and `Edit<T>`. Zero is reserved for
no object. `ItemScope` includes all 64 ID bits in the ImGui stack. Own one state
per panel, not one static gesture shared by every document. `snapValue` rounds
symmetrically; `zoomRange` keeps the pointer anchor fixed.

Interactive model editors return begin/update/commit/cancel proposals. Apply a
commit to the application model and record one Undo transaction. A revision
change cancels a stale active edit; advance it when the external model is replaced.
Atomic actions can commit immediately. Do not overwrite the external model with
all preview updates unless the application deliberately owns that preview layer.

```cpp
cyui::CurveState state;                   // persist with the panel
std::vector<cyui::CurvePoint> points;      // sorted, finite, stable nonzero IDs
cyui::CurveOptions options;
options.revision = document.revision;
options.snap = 0.25;
auto edit = cyui::curveEditor("##automation", state, points, cyui::dp(0, 260), options);
if (edit.committed()) document.commitCurve(edit.value);
```

The diagram/model types contain no engine pointers, JUCE strings or mandatory
JSON types. Keep arrays and callback captures alive for the duration of a draw.

### Canvas and numeric plots

| API | Use |
| --- | --- |
| `CanvasView::world/screen/zoomAt/fit` | Double world coordinates and anchored transforms |
| `CanvasScope(id, state, size, options)` / `frame()` | Reserve a canvas, draw its background/grid and scope clipping; `frame().wheel` when navigation or `claimWheel` takes the wheel |
| `drawCanvasGrid(rect, view, step)` | Grid overlay in the current window draw list |
| `Axis::fraction/value` | Inverse linear/log mapping; logarithmic values require positive bounds |
| `plot(id, PlotState&, ArrayView<PlotSeries>, size, options)` | Line/step/points/bars, ticks, legend, pan/zoom, crosshair and nearest sample ID/index |
| `reducePlot(points, budget)` | Prepare min/max buckets, retaining spikes and conservative gaps |
| `histogram(values, range, bins)` | uint64 counts, inclusive endpoints; ignores NaN/outside values |
| `heatmap(id, values, columns, rows, valueRange, size)` | Bounded cells, missing-data color and hovered row/column |

Canvas: middle drag or Space+left pans, wheel zooms at the pointer. Plot: middle
drag pans, wheel zooms X, Ctrl-wheel also zooms Y. `PlotOptions` controls grid,
axes, legend, crosshair/navigation, maxPoints and optional formatX/formatY callbacks
for host units. Ticks and legend are drawn inside the plot bounds. Reported
`skippedSeries` must prompt host reduction/querying; oversized data is not silently
reduced every frame. Prepare/cache a bounded visible dataset outside draw.

```cpp
cyui::PlotState state{{{20, 20000}, true}, {{-90, 0}, false}};
cyui::PlotSeries line{1, "Spectrum", preparedPoints, cyui::PlotStyle::line, 1};
cyui::PlotOptions options;
options.formatX = [](double hz) { return formatFrequency(hz); };
auto hover = cyui::plot("##response", state, {&line, 1}, cyui::dp(0, 220), options);
```

### Curves, graphs and timeline

| API | Use / result |
| --- | --- |
| `evaluateCurve(points, time, fallback)` | Linear, step, smooth or Bezier evaluation of sorted keys |
| `curveEditor(id, CurveState&, points, size, options)` | Editable points/handles and vector edit proposals |
| `arrangeNodes(nodes, links, spacing)` | Deterministic layered positions; returns proposals, tolerates cycles |
| `nodeEditor(id, NodeState&, nodes, links, size, options)` | Move vectors, connectFrom/connectTo, removeNodes/removeLink requests |
| `timeline(id, TimelineState&, lanes, clips, markers, size, options)` | Clip move/trim edits, seek, range selection and remove IDs |

Curve: double-click adds/removes keys; drag a point/selected Bezier handle; Ctrl
extends selection, box-select blank space; Ctrl+A/C/V, Delete, Escape are scoped
to the focused panel. Ctrl controls host/application shortcuts through ImGui's
focus state. CurveOptions.evaluate evaluates the preview with host interpolation;
snapTime can override the default snapping grid.

Nodes: drag cards or pins; Ctrl/Shift multi-select; blank drag box-selects;
Home frames the graph; Delete proposes removing selected nodes; right-click a
link in empty space proposes unlinking. `canConnect(from,to)` validates host pin
types and connection rules before emitting a connect request. `drawBody` draws
host details inside a card; it does not run the graph. The host removes dangling
links when deleting nodes and allocates IDs for accepted new links.

The gallery's image graph uses `NodeOptions::drawBody` and a taller `row` to draw
an image below a card's pin labels. Texture IDs come from the host through
`GalleryResources::imagePreviews` (four optional previews); the host must keep
them alive while the gallery draws. Assets uses the same textures as thumbnails,
and Inspector uses one as an example object preview. These resources are optional:
other hosts and tests show placeholders when no image is supplied. The demo's
generated image pixels are sample data, independent of UI palette colors.

Timeline: wheel zooms at the pointer, Shift-wheel pans, Ctrl-wheel scrolls lanes;
drag clips or their edges to move/trim. Locked clips/readOnly are respected.
Blank drag selects a time interval; the ruler requests seeking. TimelineOptions
supplies playhead, snap/minDuration, revision, formatTime, snapTime and drawClip.
Time units and frame/sample rounding remain the application's responsibility.

### Audio controls and prepared signal views

`modules/Audio.h` retains knob/fader/meter and meterPosition. A fader spreads its
own minimumDb..maximumDb over its travel (size 0 or negative follows ImGui sizing).
The separate AudioViews target offers:

| API | Input |
| --- | --- |
| `makePeaks(samples, columns)` | Min/max envelope preparation, preserving missing-only buckets as NaN |
| `waveform(id, WaveformState&, WaveformData, playhead, size)` | Coverage interval plus prepared per-channel peak arrays |
| `spectrum(id, PlotState&, series, size[, PlotOptions])` | Frequency/value series, logarithmic X (invalid X resets to 20..20000) |
| `oscilloscope(id, PlotState&, series, size[, PlotOptions])` | Prepared time/amplitude series |
| `stereoScope(id, PlotState&, series, size[, PlotOptions])` | Square X/Y scope; both axes share one range symmetric around 0 |

Waveform emits seek/range-selection state, supports wheel zoom, Shift-wheel pan
and middle drag. Decode audio, query envelope caches and compute FFT/DSP on the
host side. Keep raw float audio ownership and thread synchronization out of draw.

### Code, editing and Git-style diff presentation

`CodeDocument::setText(text, language, revision)` preserves original UTF-8 bytes,
tabs, CRLF and final-newline state. Display lines expand tabs to four columns and
carry lexical spans. Languages include plain, C++, C#, Python, JavaScript,
TypeScript, Rust, GLSL, HLSL, JSON, Lua, SQL, INI and Markdown. Lexical coloring is a
presentation heuristic; supply semantic parser/LSP results as overlays when needed.

- `setOverlays(SourceSpan[], revision)` accepts zero-based lines and raw UTF-8
  **byte** offsets. Stale revisions and offsets inside a code point are rejected.
- `tokenColor(role, background)` maps token roles through the current palette,
  adjusting toward the text role for contrast. `paintCodeLine` draws prepared spans.
- `codeViewer(id, CodeViewState&, document, size, annotations)` provides line
  numbers, search/next, selected/focused line, annotation hover and navigation IDs.
- `CodeEditor::setText/text/setLanguage/setReadOnly/draw` wraps the optional MIT
  native TextEditor. draw returns document-changed. `native()` exposes advanced
  TextEditor hooks for completion, annotations, cursors, wrapping and Undo/Redo;
  include TextEditor.h and handle document storage/revisions in the host.
- `DiffDocument::setTexts(before, after, language, workBudget)` prepares a bounded
  comparison; `setUnified(text, language)` reads a unified patch. `rows`, `pairs`,
  `hunks`, `columns`, `revision`, `approximate` expose prepared presentation data.
- `diffViewer(id, DiffState&, document, size)` offers unified/side-by-side modes,
  synchronized vertical scrolling, hunk/search navigation, inline changed ranges
  and old/new line navigation results. Final-newline-only changes are represented;
  CRLF and LF lines compare equal, with a notice row when line endings differ.
- `expandTabs(text, tabSize)` widens tabs to the next stop as displayed;
  `displayOffset(raw, byteOffset, tabSize)` maps a raw byte offset to that display text.

```cpp
cyui::DiffDocument comparison;
comparison.setTexts(beforeText, afterText, cyui::CodeLanguage::cpp);
cyui::DiffState view;
auto clicked = cyui::diffViewer("##comparison", view, comparison, cyui::dp(0, 400));
if (clicked.newLine > 0) host.openNewDocumentAt(clicked.newLine);
```

Do not call setTexts on unchanged large documents every frame. Code and Diff do
not depend on Git, LSP, a compiler or CodeEditor. Host repository/authentication,
review comments, merge resolution and actions stay outside the renderer.

### Inspector and assets

`Property` supports bool, int64, double, strings, choices, readOnly/password/
multiline flags and optional bounds. Set integerMinimum/integerMaximum for exact
integer bounds; do not convert int64 through double. `inspector(id, state,
properties, revision, validator)` returns PropertyEdit entries. The validator
returns an error string; invalid drafts remain visible without committing.

`assetBrowser(id, AssetState&, items, size)` uses AssetItem{id,parent,label,type,
thumbnail,folder,favorite}. State owns query, folder, selected IDs and grid/list
mode. It returns open/favorite/rename/remove requests. It performs no filesystem
operation. `CYUI_ASSET` carries the stable numeric ID for host drag/drop handling.
Folder parents and thumbnails are host data; querying/scanning/loading stays outside.

### Images, viewport and optional 3D gizmo

| API | Use |
| --- | --- |
| `imageViewport(id, ImageState&, ImageData, size, ImageTool)` | Texture/UV crop, checkerboard, fit/zoom/pan, pixel hover and crop edit proposal |
| `sampleGradient(stops, position, linearLight)` | Sample unordered stops using sRGB or explicit linear-light interpolation |
| `gradientEditor(id, GradientState&, stops, size, linearLight, revision)` | Select/drag/add/remove stops; returns edits |
| `cameraPosition/cameraViewMatrix(OrbitCamera)` | Arbitrary up axis, column-major right-handed camera |
| `viewport(id, ViewportState&, ImageData, size, navigation)` | Aspect-preserving scene texture, UV picking, camera interaction |
| `transformGizmo(id, TransformState&, Transform2D, options)` | 2D move/scale/rotation overlay, projection/unprojection callbacks |
| `gizmo3D(id, Gizmo3DState&, matrix, view, projection, rect, options)` | Optional host ImGuizmo adapter for column-major scene transforms |

Image: wheel zooms, middle drag pans, double-click fits; the crop tool returns a
rectangle, and Escape restores the previous selection. Gradient: double-click
adds an interpolated stop, drag moves it, right-click removes a stop when more
than two remain. Host color editors change stop colors. Texture lifetime, color
management and document transforms remain host-owned.

The transform gizmo's handles are ImGui items: over a canvas (imageViewport,
viewport, CanvasScope) they take the hover and the click, so nothing underneath reacts.
Host items drawn over a canvas get the same by calling `markCanvasOverlay()` each frame.
Viewport: Alt-left drag orbits, middle drag pans, wheel changes distance. Host
renderers produce the texture; CyUI does not render a 3D scene. Transform2D rotation
is radians. Ctrl snaps rotation to 15-degree increments. Gizmo3DOptions selects
ImGuizmo operation/mode, snap, orthographic view, revision and enabled state.
Link CyUI::ImGuizmo only when a compatible host target is supplied.

### Commands, logs and documents

- `rankCommands(commands, query)` matches every search word and returns stable
  ranked indices. `commandPalette(id, open, state, commands, size)` displays a modal
  palette with keyboard navigation and returns a requested command ID. Disabled
  entries do not execute; the host maps IDs to actions.
- `LogBuffer(capacity,maxBytes)` provides synchronized append/clear/snapshot.
  `logView(id, LogState&, buffer, size)` filters/follows bounded cached rows and
  returns a selected ID; LogState.levels masks debug/info/warning/error.
  `diagnosticList(id, rows, size)` displays source/line/column/message and returns
  a requested diagnostic ID. The host resolves source navigation.
- `Document::setMarkdown` caches a small renderer subset: headings, paragraphs,
  quotes, bullet/numbered items, rules, fenced code, links, inline code/emphasis
  and pipe tables. `documentView(id, state, document, size)` returns link/copy
  results. Links are returned to the host, not opened by the renderer.
- `transcript(id, TranscriptState&, messages, size)` skips offscreen messages it has
  measured, caches Documents by message
  ID, follows appended/streamed content when at the bottom, and reports copy/link
  requests. Messages contain id/role/content/time. Full CyMD rendering can use Web.

The Markdown subset does not interpret raw HTML or run scripts. It intentionally
uses the same Core widgets and Code colors. Native inline emphasis is a themed
presentation subset rather than a complete CommonMark layout engine.

### Optional Web / CEF

`webPanel`, WebBackend/WebPlacement/WebInput, WebPolicy/webOrigin, webThemeJson
and webEventJson form the engine-independent browser module. `createWebCEF`,
`webCEFApp` and `webCEFBrowser` provide the optional CEF implementation. The
browser draws in a host-owned texture and shares CyUI theme roles through CSS.
See [WEB.md](WEB.md) for the complete lifecycle, native/Python recipes, TypeScript
bridge, CyMD iframe protocol, origin checks, process sandbox and OS packaging.
CEF is separate from Core and from the module's neutral API. The local validated
runtime is Windows; Linux/macOS build/deployment validation is explicitly pending.

Rules when using it:

- Pump `CefDoMessageLoopWork()` (Python `runtime.pump()`) outside the ImGui frame,
  before `NewFrame`. `WebCEFOptions::upload` and the optional `release` callback run
  inside it; `release(texture)` comes once after the browser closed: free that
  texture only after the frames that used it (Python: `create(..., release=)`).
- `external` events are page requests, never commands. Only
  `WebPolicy::externalSchemes` (default http/https) are reported; check
  `userGesture`, `mainFrame` and `sourceOrigin`, apply your own allow-list and ask
  the user before opening anything with the shell. Sub-frame popups without a
  gesture are dropped.
- `WebEventKind::dropped` (data = count) reports events discarded by the queue caps
  (`maxQueuedEvents`, `maxQueuedErrors` for errors/console) or by policy.
- `WebPanelOptions::focus` is one-shot (taken on a false -> true change). A focused
  panel owns the keys it forwards, so ImGui nav and other widgets do not act on them.
- Backend calls must run on the CEF UI thread; others are ignored. The supplied
  bridge, helper and demo run CEF unsandboxed on Windows (WEB.md "Process sandbox").

### Python examples

```python
from cyui_modules.curves import CurvePoint, CurveState, curve_editor
from cyui_modules.common import Range

state = CurveState()
state.view.x.view = Range(0, 10)
state.view.y.view = Range(0, 1)
points = [CurvePoint(1, 0, .2), CurvePoint(2, 10, .8)]
# Each ImGui frame:
edit = curve_editor('automation', state, points, (0, 260), revision=revision)
if edit.committed:
    points = edit.value
```


Python optional APIs are snake_case equivalents in the packages listed in
[MODULES.md](MODULES.md). Python source offsets for SourceSpan are raw UTF-8
bytes; displayed lexer spans use Python character indices. Do not mix those two
coordinate systems. `CodeEditor` in Python offers native ImGui multiline Undo
plus a syntax preview; C++ TextEditor advanced callbacks are not exposed there.
`CEFRuntime` is an explicit optional browser loader; importing model modules is
independent of imgui-bundle, NumPy, audio/video or browser engines.

## 9. Unreal Editor connector (optional)

Prepare `integrations/unreal/CyUI` with `tools/package_unreal.py --layout module`,
selecting only the required modules. Copy the resulting folder to Source/CyUI
inside an existing plugin and declare its Editor module and Build.cs dependency.
For several consumers, use one shared provider (`--layout plugin`) and add the
provider to each consumer's .uplugin Plugins array. Never define CyUI twice in a
project. The module adds no menu or demo window when loaded. It owns ImGui frames,
renderer-managed fonts, clipping, input and clipboard. A separate Editor module depends on `CyUI`
and uses `CyUIModule.h` / `FCyUIModule` to embed MakePanel or register an
`FOnDrawCyUITool` with RegisterTool; unregister it before the owner unloads.
Draw contents without another root Begin/End/frame. A plugin redistributed to
others embeds the module under its own name (`--module-name MyToolCyUI`, then
depend on `MyToolCyUI`) or uses the shared provider; two modules named CyUI
cannot load together. Check `FCyUIModule::IsAvailable()` before `Get()` at shutdown.

`MakePanel` embeds a callback in another Slate layout. `RegisterTexture` returns
an ImTextureID-compatible handle for a pinned Unreal texture; release it after
stopping its use. Module feature definitions and DLL exports are propagated by
UBT. No Unreal code enters CMake's Core or other standalone builds.

Panels use separate ImGui contexts. Each has its own CyUI palette, density and
fonts (see "Several ImGui contexts" in section 1). Numeric entry and
notifications stay local to their context. Outside the draw callback (Slate
handlers, async results) use `SCyUIPanel::RunInContext([] { cyui::setTheme(...); })`
and `SCyUIPanel::Notify(text, Tone)` (or `cyui::notify(context, ...)`). Each key goes
to one side: ImGui only while it takes keyboard input (active item, text field, or a
focused view calling `ImGui::SetNextFrameWantCaptureKeyboard(true)`) or for plain
nav keys; otherwise Unreal, so editor shortcuts never run twice. All drawing runs on the
editor game thread; host transactions and expensive data preparation remain the
tool's responsibility. See [UNREAL.md](UNREAL.md) for complete recipes, lifecycle,
automation tests, platform validation and current input/backend limitations.


## Shared C++ syntax catalogue and plugin engines

`CyUI::Code` owns 101 built-in syntax modes, including HLSL and GLSL. CodeEditor,
CodeDocument and Diff share it. Read [CODE.md](CODE.md) before implementing a host
syntax extension. Use `codeLanguages()` for menus and `codeLanguageForPath()` for
associations; avoid maintaining a second list in the application.

Register `CodeLanguageDefinition` with declarative rules, a built-in base and word
lists, or a host callback returning validated UTF-8 byte spans. Registration and
removal increment `codeLanguageRevision()`; cached views re-highlight on that
revision. Registration never edits the document or adds an Undo transaction.
CyUI does not load plugin DLLs or parse application manifests. Hosts retain those
responsibilities, pin callbacks and their libraries during calls, and unregister
definitions before unloading plugins. Built-in IDs cannot be overwritten.

Gallery's Code page includes a syntax selector for the shared registry. The
native backend accepts shared display spans without replacing its lexical state;
hosts with richer syntax overlays can use `CodeEditor::setSyntaxEnabled(false)`.
