#pragma once

// CyUI - Layout: the shell of an application window.
//
//   +-----------------------------------------------------------+
//   | menu bar (ImGui::BeginMainMenuBar, styled by the theme)   |
//   | top bar   cyui::beginTopBar  (modes, transport, tools)    |
//   +---------+-------------------------------------------------+
//   | sidebar |  dock space / main content                      |
//   | nav     |  (panels: ImGui::Begin + cyui::panelHeader,     |
//   |         |   cyui::beginToolbar, cards, properties...)     |
//   +---------+-------------------------------------------------+
//   | status bar  cyui::beginStatusBar                          |
//   +-----------------------------------------------------------+
//
// Order in the frame: menu bar, top bar, status bar, sidebar, THEN the dock
// space (each bar takes its room off the viewport's work area), then the
// panels, then cyui::renderNotifications().
//
// Every begin* here must be paired with its end*, whatever it returned
// (except beginModal: end only when it returned true).

#include "cyui/CoreWidgets.h"

#include <imgui.h>

namespace cyui {
class TextFilter;

// A bar along the top of the main viewport, under the menu bar.
// height 0: a frame plus metrics().toolbarHeight.
CYUI_API bool beginTopBar(const char* id, float height = 0.0f);
CYUI_API void endTopBar();

// A thin bar along the bottom (status text, progress, frame rate).
CYUI_API bool beginStatusBar(const char* id);
CYUI_API void endStatusBar();
// Status text in the small font: tone neutral = faint, error = red...
CYUI_API void statusText(Tone tone, const char* fmt, ...) IM_FMTARGS(2);

// A navigation column on the left of the main viewport (CyEngineHub).
//   cyui::beginSidebar("##nav");
//   cyui::sidebarHeader("CYENGINEHUB", "ALPHA");
//   cyui::sidebarGroup("ENVIRONMENT");
//   if (cyui::sidebarItem(icon::home, "Home", page == 0)) page = 0;
//   cyui::endSidebar();
CYUI_API bool beginSidebar(const char* id, float width = 190.0f);
CYUI_API void endSidebar();
CYUI_API void sidebarHeader(const char* title, const char* badgeText = nullptr);
CYUI_API void sidebarGroup(const char* label);
CYUI_API bool sidebarItem(const char* icon, const char* label, bool active, const char* badgeText = nullptr);

// A strip at the top of a panel: a title, an optional subtitle, and room on
// the right for buttons (call ImGui::SameLine after, or alignRight).
CYUI_API void panelHeader(const char* title, const char* subtitle = nullptr);
// Optional quick filter below the panel heading. The host decides which rows
// match with TextFilter::matches; stock ImGui calls are never intercepted.
CYUI_API void panelHeader(const char* title, const char* subtitle, TextFilter* filter);

// A toolbar row inside a window: raised ground, a line under it. Put
// iconButton / toolbarSeparator / segmented... on one line inside.
CYUI_API bool beginToolbar(const char* id);
CYUI_API void endToolbar();

// A centred modal dialog with a title and a close cross.
//   if (cyui::beginModal("Delete layer?", &showDelete)) {
//       ImGui::TextWrapped("This cannot be undone.");
//       if (const auto r = cyui::modalButtons("Delete", "Cancel", true); r == 1) remove();
//       cyui::endModal();
//   }
// Set *open = true to show it; it sets *open = false when closed.
CYUI_API bool beginModal(const char* title, bool* open, float width = 460.0f);
CYUI_API void endModal();
// Right-aligned buttons at the foot of a modal: 0 nothing, 1 confirm,
// 2 cancel (the modal closes on both). Enter confirms, Escape cancels.
CYUI_API int modalButtons(const char* confirm, const char* cancel = "Cancel", bool danger = false);

// Puts the cursor so an item `width` wide ends at the right edge of the
// content region, on the current line.
CYUI_API void alignRight(float width);
// The width a button with this label will take.
[[nodiscard]] CYUI_API float buttonWidth(const char* label);

#ifdef IMGUI_HAS_DOCK
// The dock space over the rest of the main viewport, after the bars.
CYUI_API ImGuiID dockSpace(const char* id = "CyUIDockSpace", ImGuiDockNodeFlags flags = 0);
#endif

}  // namespace cyui
