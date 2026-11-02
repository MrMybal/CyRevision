#pragma once
#include "cyui/Export.h"
#include <imgui.h>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace cyui {
class TextFilter;
struct FilterOptions {
    const char *hint = "Filter (Ctrl/Cmd+F)...";
    float width = 0; // screen pixels; 0 fills the available row
    bool shortcut = true;
    bool focusOnAppearing = false;
    bool resetOnAppearing = false;
};
// Returns whether the query changed. Escape in the field and the clear button
// clear it. Ctrl+F (Cmd+F on macOS) focuses the owning view's field.
CYUI_API bool filterBox(const char *id, TextFilter &, const FilterOptions & = {});

// Host-owned, one per panel/menu/list. Models can be queried without an ImGui
// context. Every word must match across the visible label, its translation and
// optional keywords/context. Hidden ##/### IDs are not searched.
class TextFilter {
  public:
    std::string query;
    CYUI_API bool active() const;
    CYUI_API bool matches(std::string_view label, std::string_view keywords = {}) const;
    CYUI_API void clear();
    void focus() {
        focusNext_ = true;
    }

  private:
    friend CYUI_API bool filterBox(const char *, TextFilter &, const FilterOptions &);
    void compile() const;
    mutable std::string compiled_;
    mutable std::vector<std::string> words_;
    bool focusNext_ = false;
};
// ASCII case + Latin accent/ligature folding; combining accents are ignored.
// Other UTF-8 characters are preserved. This is not full Unicode case folding.
CYUI_API std::string foldFilterText(std::string_view);

// Render a matching section open while searching, preserving its saved collapse
// preference. The host computes hasMatchingChildren from its own model. Pair
// with endSection() only when true, just like beginSection().
CYUI_API bool beginFilteredSection(const char *label, const TextFilter &, bool hasMatchingChildren,
                                   bool defaultOpen = true);

struct FilterMenuItem {
    std::uint64_t id = 0; // unique nonzero action ID; zero for groups/separators
    std::string label, shortcut, keywords;
    bool enabled = true, checked = false;
    std::vector<FilterMenuItem> children; // group; an empty label/ID is a separator
};
struct FilterMenuMatch {
    std::uint64_t id = 0;
    std::string label, shortcut, breadcrumb;
    bool enabled = true, checked = false;
};
// A snapshot of matching actions, including matches in ancestor group names.
// Labels and breadcrumbs are translated for display; action IDs never change.
CYUI_API std::vector<FilterMenuMatch> filterMenuMatches(const std::vector<FilterMenuItem> &,
                                                        const TextFilter &);
struct FilterMenuOptions {
    float width = 260; // design units, scaled internally
    bool resetOnOpen = true, focusOnOpen = true;
};
// Draw inside an already open menu, popup or panel. Unfiltered: normal nested
// menus. Filtered: reachable matching actions with their group breadcrumbs.
// Returns an action ID; the host executes it and owns all toggle/model changes.
CYUI_API std::uint64_t filterMenu(const char *id, TextFilter &, const std::vector<FilterMenuItem> &,
                                  const FilterMenuOptions & = {});
} // namespace cyui
