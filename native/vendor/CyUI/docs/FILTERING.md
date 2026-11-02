# Quick filters for menus, panels and lists

Filtering is included in `CyUI::Core` and `<cyui/Core.h>`. It is opt-in per view:
the host chooses which labeled controls, property rows or menu actions belong to
the filter. Keep one `TextFilter` per panel/menu instance so independent views do
not share queries. Dear ImGui is not patched, and raw ImGui calls are not
automatically hidden.

## Panel and list filtering

```cpp
// Persist alongside the panel's other state.
cyui::TextFilter filter;

// During the frame, inside the panel's ImGui window:
cyui::panelHeader(cyui::tr("Properties").c_str(), nullptr, &filter);
if (cyui::beginProperties("##properties")) {
    if (filter.matches("Opacity", "Surface")) {
        cyui::property(cyui::tr("Opacity").c_str());
        cyui::sliderBar("##opacity", &opacity, 0, 1, "%.2f", -1);
    }
    cyui::endProperties();
}
```

For a list, draw `filterBox("##find", filter)` and render matching rows from the
host's model. `filterBox` returns whether the query changed. `FilterOptions`
provides a translated hint, screen-pixel width, shortcut behavior, and optional
focus/reset when the window appears. A zero width fills the row.

Ctrl+F focuses the field in the focused view (Cmd+F with ImGui's macOS behavior).
The shortcut does not steal focus while another text editor accepts input;
applications can call `filter.focus()` from their own shortcut routing. Escape
inside the filter clears it, as does its clear button. Existing widgets outside
the host's match conditions remain usable.

`matches` requires every whitespace-separated word to appear across the visible
English label, its current translation and the supplied keywords. Match order
does not matter. ASCII/Latin case, Latin accents/ligatures and combining accents
are folded, so `rugosite` finds French `Rugosité`, and `roughness` still finds the
same property. Other UTF-8 text is preserved; this is not full Unicode case
folding. Keywords are searched as supplied: include translated keywords or paths
when appropriate. Hidden `##` and `###` label identities are not indexed.

## Collapsible sections and Inspector

```cpp
cyui::InspectorOptions options{&filter, "Surface"};
const bool any = std::any_of(fields.begin(), fields.end(), [&](const auto& field) {
    return cyui::propertyMatches(filter, field, "Surface");
});
if (cyui::beginFilteredSection("Surface", filter, any)) {
    for (const auto& event : cyui::inspector("##surface", state, fields, revision,
                                            validator, options)) {
        handlePropertyEdit(event);
    }
    cyui::endSection();
}
```

Matching sections open temporarily while filtering. Clearing the query restores
their stored collapse preference. Include ancestor section names in row contexts
so a section query keeps its children visible. Filtered Inspector rows retain
their property IDs, draft values and host validation behavior; an active edit
hidden by the filter emits a cancel proposal. Supply the full property model to
Inspector rather than removing rows from its input.

Property matching considers names, translated help and section context, not
property values or passwords. If the host stops calling Inspector for an entire
hidden section/panel, it must cancel that section's outstanding previews itself.
Clearing a filter never modifies the application's model.

## Searchable menus

```cpp
cyui::TextFilter menuFilter; // persistent with this menu's state
std::vector<cyui::FilterMenuItem> entries{
    {1, "Save", "Ctrl+S"},
    {0, "Export", {}, {}, true, false, {
        {2, "Export PNG", {}, "image texture"},
        {3, "Export HDR", {}, "image", false}}}
};
if (ImGui::BeginMenu(cyui::trLabel("File").c_str())) {
    const auto action = cyui::filterMenu("##file", menuFilter, entries);
    if (action) dispatchAction(action);
    ImGui::EndMenu();
}
```

An item with children is a submenu; a zero-ID item with no label/children is a
separator. Actions need unique nonzero 64-bit IDs. The host owns checked/enabled
states and executes the returned ID. No filesystem, command execution or
application callbacks are run by the filter.

With no query, the original nested menu is drawn. A query reveals matching
descendant actions with translated breadcrumb paths. A parent title or keyword
can match its descendants, and multiple words can match across the path and leaf
caption. Disabled parents keep their results disabled. `FilterMenuOptions`
controls design-unit width, reset and focus on opening; disable reset/focus when
embedding the same function inline in a panel. `filterMenuMatches(entries, filter)`
returns a snapshot for custom renderers without drawing UI.

## Python

```python
import cyui as c
from cyui_modules.inspector import inspector

query = c.TextFilter()  # persistent
c.panel_header(c.tr('Properties'), filter_state=query)
for key, edit in inspector('details', state, properties, filter_state=query,
                            filter_context='Surface'):
    handle_edit(key, edit)

entries = [c.FilterMenuItem(1, 'Save', 'Ctrl+S')]
# Inside an open imgui menu or popup:
action = c.filter_menu('file', menu_query, entries)
```

`cyui_modules.filters` also imports without ImGui for model-only matching.
The APIs mirror C++ as `filter_box`, `begin_filtered_section`, `filter_menu`,
`filter_menu_matches` and `fold_filter_text`. Python Inspector accepts
`filter_state` and `filter_context` keyword arguments. The host handles data edits
and cancellation in the same way as C++.

For large models, cache match results when the query, language or model revision
changes, then clip the visible rows. No automatic search index or global view
registry is maintained. Web content inside CEF remains owned by its Web app; the
native filter does not rewrite an HTML document.

The C++ gallery's Filtering page (21), Details page (12), and Python gallery show
these APIs. All controls use the shared palette, DPI sizes and English/French
catalogs. Unreal panels can use the same Core functions in their draw callback.
