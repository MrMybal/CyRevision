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

#include "cyui/Widgets.h"

#include <imgui.h>

namespace cyui {

// A bar along the top of the main viewport, under the menu bar.
// height 0: a frame plus metrics().toolbarHeight.
bool beginTopBar(const char* id, float height = 0.0f);
void endTopBar();

// A thin bar along the bottom (status text, progress, frame rate).
bool beginStatusBar(const char* id);
void endStatusBar();
// Status text in the small font: tone neutral = faint, error = red...
void statusText(Tone tone, const char* fmt, ...) IM_FMTARGS(2);

// A navigation column on the left of the main viewport (CyEngineHub).
//   cyui::beginSidebar("##nav");
//   cyui::sidebarHeader("CYENGINEHUB", "ALPHA");
//   cyui::sidebarGroup("ENVIRONMENT");
//   if (cyui::sidebarItem(icon::home, "Home", page == 0)) page = 0;
//   cyui::endSidebar();
bool beginSidebar(const char* id, float width = 190.0f);
void endSidebar();
void sidebarHeader(const char* title, const char* badgeText = nullptr);
void sidebarGroup(const char* label);
bool sidebarItem(const char* icon, const char* label, bool active, const char* badgeText = nullptr);

// A strip at the top of a panel: a title, an optional subtitle, and room on
// the right for buttons (call ImGui::SameLine after, or alignRight).
void panelHeader(const char* title, const char* subtitle = nullptr);

// A toolbar row inside a window: raised ground, a line under it. Put
// iconButton / toolbarSeparator / segmented... on one line inside.
bool beginToolbar(const char* id);
void endToolbar();

// A centred modal dialog with a title and a close cross.
//   if (cyui::beginModal("Delete layer?", &showDelete)) {
//       ImGui::TextWrapped("This cannot be undone.");
//       if (const auto r = cyui::modalButtons("Delete", "Cancel", true); r == 1) remove();
//       cyui::endModal();
//   }
// Set *open = true to show it; it sets *open = false when closed.
bool beginModal(const char* title, bool* open, float width = 460.0f);
void endModal();
// Right-aligned buttons at the foot of a modal: 0 nothing, 1 confirm,
// 2 cancel (the modal closes on both). Enter confirms, Escape cancels.
int modalButtons(const char* confirm, const char* cancel = "Cancel", bool danger = false);

// Puts the cursor so an item `width` wide ends at the right edge of the
// content region, on the current line.
void alignRight(float width);
// The width a button with this label will take.
[[nodiscard]] float buttonWidth(const char* label);

#ifdef IMGUI_HAS_DOCK
// The dock space over the rest of the main viewport, after the bars.
ImGuiID dockSpace(const char* id = "CyUIDockSpace", ImGuiDockNodeFlags flags = 0);
#endif

}  // namespace cyui
