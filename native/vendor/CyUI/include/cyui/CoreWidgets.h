#pragma once

// CyUI - Widgets: the controls of CyDAW 2 / Shaderixel / CyEngineHub, made
// generic. Each one draws with cyui::palette() and cyui::metrics(), so it
// follows the theme. They are ordinary ImGui items: SameLine, PushID,
// IsItemHovered... all work after them. A control that changes a value marks
// it edited: IsItemEdited() is true on that frame. Clicks and drags also give
// IsItemDeactivatedAfterEdit() when released (one undo step per gesture);
// wheel nudges and typed values are one-shot edits that only set
// IsItemEdited(). Inside ScopedDisabled they are greyed out and inert.
//
// Shared behaviour of value controls (dragValue, sliderBar, audio knobs and
// faders): drag to change, Ctrl = fine, Shift = fast (valueSpeed()), the mouse
// wheel nudges (the panel under the control does not scroll; a dragValue only
// once it has the focus). dragValue and sliderBar: a double click types the
// value. Knobs: a double click restores the default, a click on the value
// types it.
//
// Sizes: `size` and `width` parameters are screen pixels (wrap design values
// in dp(); 0 = automatic, < 0 = fill minus that much where noted). Heights,
// spacings and radii documented "in dp" are design units scaled inside.

#include "cyui/Theme.h"

#include <imgui.h>

namespace cyui {

// ---- Scoped helpers (RAII, pop on scope exit) -------------------------------

class ScopedColor final {
public:
    ScopedColor(ImGuiCol index, ImU32 colour) { push(index, colour); }
    ScopedColor(ImGuiCol index, const ImVec4& colour) { ImGui::PushStyleColor(index, colour); ++count_; }
    ~ScopedColor() { ImGui::PopStyleColor(count_); }
    ScopedColor& push(ImGuiCol index, ImU32 colour) { ImGui::PushStyleColor(index, colour); ++count_; return *this; }
    ScopedColor(const ScopedColor&) = delete;
    ScopedColor& operator=(const ScopedColor&) = delete;
private:
    int count_ {};
};

class ScopedVar final {
public:
    ScopedVar(ImGuiStyleVar index, float value) { push(index, value); }
    ScopedVar(ImGuiStyleVar index, ImVec2 value) { push(index, value); }
    ~ScopedVar() { ImGui::PopStyleVar(count_); }
    ScopedVar& push(ImGuiStyleVar index, float value) { ImGui::PushStyleVar(index, value); ++count_; return *this; }
    ScopedVar& push(ImGuiStyleVar index, ImVec2 value) { ImGui::PushStyleVar(index, value); ++count_; return *this; }
    ScopedVar(const ScopedVar&) = delete;
    ScopedVar& operator=(const ScopedVar&) = delete;
private:
    int count_ {};
};

// A font and/or a size factor of the base size (1 = normal, 0.85 = small).
// A null font (uiFont() before loadFonts) keeps the current font.
class ScopedFont final {
public:
    explicit ScopedFont(ImFont* font, float sizeFactor = 1.0f) { ImGui::PushFont(font, ImGui::GetStyle().FontSizeBase * sizeFactor); }
    ~ScopedFont() { ImGui::PopFont(); }
    ScopedFont(const ScopedFont&) = delete;
    ScopedFont& operator=(const ScopedFont&) = delete;
};

class ScopedID final {
public:
    explicit ScopedID(const char* id) { ImGui::PushID(id); }
    explicit ScopedID(int id) { ImGui::PushID(id); }
    explicit ScopedID(const void* id) { ImGui::PushID(id); }
    ~ScopedID() { ImGui::PopID(); }
    ScopedID(const ScopedID&) = delete;
    ScopedID& operator=(const ScopedID&) = delete;
};

class ScopedDisabled final {
public:
    explicit ScopedDisabled(bool disabled = true) { ImGui::BeginDisabled(disabled); }
    ~ScopedDisabled() { ImGui::EndDisabled(); }
    ScopedDisabled(const ScopedDisabled&) = delete;
    ScopedDisabled& operator=(const ScopedDisabled&) = delete;
};

// ---- Tones -------------------------------------------------------------------

// The meaning of a badge, a button, a notice, a progress bar.
enum class Tone { neutral, accent, success, warning, error, info };
[[nodiscard]] CYUI_API ImU32 toneColour(Tone tone);

// ---- Text --------------------------------------------------------------------

// A title: level 1 (page, 1.5x semibold), 2 (panel, 1.2x), 3 (group, 1.0x).
CYUI_API void heading(const char* text, int level = 2);
// Small semibold dimmed title for a group inside a panel ("Transform").
CYUI_API void sectionTitle(const char* text);
// CyEngineHub-style label: SMALL UPPERCASE, bold, accent-tinted. Pass the
// text already uppercased.
CYUI_API void overline(const char* text, ImU32 colour = 0);
CYUI_API void textDim(const char* fmt, ...) IM_FMTARGS(1);
CYUI_API void textFaint(const char* fmt, ...) IM_FMTARGS(1);
CYUI_API void textColoured(ImU32 colour, const char* fmt, ...) IM_FMTARGS(2);
// Monospace text (paths, logs, values).
CYUI_API void textMono(const char* fmt, ...) IM_FMTARGS(1);
// A keyboard shortcut drawn as a key cap: kbd("Ctrl+S").
CYUI_API void kbd(const char* keys);
// A small (?) or (i) after the previous item, its tooltip on hover.
CYUI_API void helpMarker(const char* text);
// A delayed tooltip for the last item (nothing when text is null/empty).
CYUI_API void tooltip(const char* text);
// Text clipped to `maxWidth` with an ellipsis, drawn at `position`. Both draw
// in the current font, fading with ImGui's style alpha (disabled blocks).
CYUI_API void clippedText(ImDrawList* list, ImVec2 position, float maxWidth, ImU32 colour, const char* text);
// Text centred in a rectangle.
CYUI_API void centredText(ImDrawList* list, ImVec2 min, ImVec2 max, ImU32 colour, const char* text);

// ---- Buttons -----------------------------------------------------------------

enum class Variant {
    secondary,  // default ground (bg3): most buttons
    primary,    // accent fill: the ONE main action of a panel or dialog
    ghost,      // no ground until hovered: toolbars, secondary actions
    outline,    // transparent with a line: CyEngineHub's "ghost" button
    danger,     // destructive action (delete, discard)
    success,    // confirm / go
};

CYUI_API bool button(const char* label, Variant variant = Variant::secondary, ImVec2 size = ImVec2(0.0f, 0.0f));
CYUI_API bool primaryButton(const char* label, ImVec2 size = ImVec2(0.0f, 0.0f));
CYUI_API bool dangerButton(const char* label, ImVec2 size = ImVec2(0.0f, 0.0f));
CYUI_API bool ghostButton(const char* label, ImVec2 size = ImVec2(0.0f, 0.0f));
// An icon glyph then a label: iconTextButton(icon::save, "Save").
CYUI_API bool iconTextButton(const char* icon, const char* label, Variant variant = Variant::secondary,
                    ImVec2 size = ImVec2(0.0f, 0.0f));
// A flat square icon button, tinted when active (a tool, a mode, play...).
// When the current font lacks the glyph, the tooltip's first letter stands in.
CYUI_API bool iconButton(const char* icon, const char* tooltipText = nullptr, bool active = false, ImU32 activeColour = 0,
                ImVec2 size = ImVec2(0.0f, 0.0f));
// A compact rounded toggle that fills with `onColour` when on (M, S, R, a word).
CYUI_API bool toggleButton(const char* label, bool* value, ImU32 onColour = 0, ImVec2 size = ImVec2(0.0f, 0.0f),
                  const char* tooltipText = nullptr);
// An on/off switch with its label after it (settings).
CYUI_API bool toggleSwitch(const char* label, bool* value);
// Joined buttons, one selected: view modes, units, tabs in a toolbar.
// Returns true when the selection changed.
CYUI_API bool segmented(const char* id, const char* const labels[], int count, int* current, float width = 0.0f);
// A clickable "link" text.
CYUI_API bool linkText(const char* label);

// ---- Value controls ------------------------------------------------------------

// A labelled drag field for numbers: dragValue("Tempo", &bpm, 0.1f, 20, 300, "%.2f BPM").
// `speed` per pixel dragged (x valueSpeed()); once the field has the focus
// (clicked, navigated to), a wheel notch nudges by 10 x speed, otherwise the
// wheel scrolls the panel; a double click types the value.
// minimum >= maximum: unbounded.
CYUI_API bool dragValue(const char* id, float* value, float speed, float minimum, float maximum, const char* format = "%.2f",
               float width = 0.0f);
// A slider drawn as a filled bar with the value in it (inspectors). A click
// or drag sets the value, the wheel nudges by 1 % of the range, a double click
// types it. width: 0 = item width, < 0 = fill minus that much.
CYUI_API bool sliderBar(const char* id, float* value, float minimum, float maximum, const char* format = "%.2f",
               float width = 0.0f, ImU32 colour = 0);

// A number typed in over a rectangle: call each frame; `open` opens it.
// Enter sets (a comma is a decimal point, "k" multiplies by 1000), Escape leaves.
CYUI_API bool valueEntry(ImGuiID id, ImVec2 min, ImVec2 max, double& value, double minimum, double maximum, bool open,
                double scale = 1.0);
[[nodiscard]] CYUI_API bool valueEntryOpen();

// Marks item `id` edited after a hand-made value change (IsItemEdited, and
// IsItemDeactivatedAfterEdit when it is or just was the active item). Safe to
// call when another item is active (it then does nothing).
CYUI_API void markEdited(ImGuiID id);
// The wheel for the last item: claims it on every frame the item is hovered,
// so the window under it does not scroll too, and returns the wheel delta the
// item may use (0 when not hovered, or when a view or window took it).
[[nodiscard]] CYUI_API float itemWheel();
// The wheel this frame went to a control under the pointer: a view that
// scrolls with the wheel should leave it alone when wheelTaken(). takeWheel()
// claims it after the fact (prefer itemWheel(), which claims it in time).
CYUI_API void takeWheel();
[[nodiscard]] CYUI_API bool wheelTaken();
// Ctrl: 0.15 (fine), Shift: 4 (fast), else 1.
[[nodiscard]] CYUI_API float valueSpeed();

// ---- Fields --------------------------------------------------------------------

// A search box: magnifier, hint, a clear button. True when the text changed.
CYUI_API bool searchField(const char* id, char* buffer, int bufferSize, const char* hint = "Search", float width = 0.0f);
// A text field with a hint (placeholder). True when the text changed.
CYUI_API bool textField(const char* id, char* buffer, int bufferSize, const char* hint = nullptr, float width = 0.0f);
// A combo from a list of strings. True when the selection changed.
CYUI_API bool combo(const char* id, int* current, const char* const items[], int count, float width = 0.0f);

// ---- Containers ----------------------------------------------------------------

// A card: rounded raised ground (bg2), a line, padding, an optional title.
// Always call endCard() (even when beginCard returns false).
// height in dp: 0 = fit content, >0 = fixed, <0 = fill minus that much.
CYUI_API bool beginCard(const char* id, const char* title = nullptr, float height = 0.0f, Tone tone = Tone::neutral);
CYUI_API void endCard();

// A collapsible section with a full-width header bar (inspectors).
// Call endSection() only when it returned true.
CYUI_API bool beginSection(const char* label, bool defaultOpen = true);
CYUI_API void endSection();

// Label / value rows in two aligned columns (inspectors, settings):
//   if (cyui::beginProperties("##transform")) {
//       cyui::property("Position");  ImGui::DragFloat2("##pos", pos);
//       cyui::property("Visible");   cyui::toggleSwitch("##vis", &visible);
//       cyui::endProperties();
//   }
// property() writes the label in the first column and readies the second
// (full width) for the control.
// labelWidth in dp (0: 38 % of the row).
CYUI_API bool beginProperties(const char* id, float labelWidth = 0.0f);
CYUI_API void property(const char* label, const char* help = nullptr);
CYUI_API void endProperties();

// A thin vertical bar between toolbar groups (on the same line).
CYUI_API void toolbarSeparator();
// A horizontal line with optional spacing (dp), in the theme's line colour.
CYUI_API void divider(float spacing = 4.0f);
// Vertical space in dp.
CYUI_API void space(float height = 8.0f);

// ---- Status & feedback -----------------------------------------------------------

// A small rounded pill: badge("BETA", Tone::warning).
CYUI_API void badge(const char* text, Tone tone = Tone::neutral);
// A coloured dot (status: online, error...), on the text line.
CYUI_API void statusDot(ImU32 colour, const char* tooltipText = nullptr);
// A thin progress bar; fraction < 0 draws an indeterminate one.
CYUI_API void progressBar(float fraction, ImVec2 size = ImVec2(-1.0f, 0.0f), const char* overlay = nullptr,
                 Tone tone = Tone::accent);
// A spinning arc (work in progress); radius in dp.
CYUI_API void spinner(const char* id, float radius = 7.0f, ImU32 colour = 0);
// A centred title and message filling the rest of a panel ("No layers yet").
CYUI_API void emptyState(const char* title, const char* message, const char* icon = nullptr);
// A notice block with a coloured bar: callout(Tone::warning, "Unsaved changes").
CYUI_API void callout(Tone tone, const char* text);
// A coloured square (track colour, swatch). True when clicked.
CYUI_API bool colourChip(const char* id, ImU32 colour, ImVec2 size);

// Toast notifications, bottom-right of the main viewport, at most 6. notify()
// shows in the current ImGui context. With none current, it goes to the one
// context CyUI knows (it asserts and drops when there are several or none):
// pass a context to post to a given panel, from any thread.
// renderNotifications() once per frame, after everything else.
CYUI_API void notify(const char* text, Tone tone = Tone::neutral, float seconds = 4.0f);
CYUI_API void notify(ImGuiContext* context, const char* text, Tone tone = Tone::neutral, float seconds = 4.0f);
CYUI_API void renderNotifications();

}  // namespace cyui
