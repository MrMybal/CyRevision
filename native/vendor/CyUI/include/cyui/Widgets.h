#pragma once

// CyUI - Widgets: the controls of CyDAW 2 / Shaderixel / CyEngineHub, made
// generic. Each one draws with cyui::palette() and cyui::metrics(), so it
// follows the theme. They are ordinary ImGui items: SameLine, PushID,
// IsItemHovered... all work after them.
//
// Shared behaviour of value controls (knob, fader, dragValue):
//   drag to change, Ctrl = fine, Shift = fast, mouse wheel nudges,
//   double click restores the default, the value shows while hovered.

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
[[nodiscard]] ImU32 toneColour(Tone tone);

// ---- Text --------------------------------------------------------------------

// A title: level 1 (page, 1.5x semibold), 2 (panel, 1.2x), 3 (group, 1.0x).
void heading(const char* text, int level = 2);
// Small semibold dimmed title for a group inside a panel ("Transform").
void sectionTitle(const char* text);
// CyEngineHub-style label: SMALL UPPERCASE, bold, accent-tinted. Pass the
// text already uppercased.
void overline(const char* text, ImU32 colour = 0);
void textDim(const char* fmt, ...) IM_FMTARGS(1);
void textFaint(const char* fmt, ...) IM_FMTARGS(1);
void textColoured(ImU32 colour, const char* fmt, ...) IM_FMTARGS(2);
// Monospace text (paths, logs, values).
void textMono(const char* fmt, ...) IM_FMTARGS(1);
// A keyboard shortcut drawn as a key cap: kbd("Ctrl+S").
void kbd(const char* keys);
// A small (?) or (i) after the previous item, its tooltip on hover.
void helpMarker(const char* text);
// A delayed tooltip for the last item (nothing when text is null/empty).
void tooltip(const char* text);
// Text clipped to `maxWidth` with an ellipsis, drawn at `position`.
void clippedText(ImDrawList* list, ImVec2 position, float maxWidth, ImU32 colour, const char* text);
// Text centred in a rectangle.
void centredText(ImDrawList* list, ImVec2 min, ImVec2 max, ImU32 colour, const char* text);

// ---- Buttons -----------------------------------------------------------------

enum class Variant {
    secondary,  // default ground (bg3): most buttons
    primary,    // accent fill: the ONE main action of a panel or dialog
    ghost,      // no ground until hovered: toolbars, secondary actions
    outline,    // transparent with a line: CyEngineHub's "ghost" button
    danger,     // destructive action (delete, discard)
    success,    // confirm / go
};

bool button(const char* label, Variant variant = Variant::secondary, ImVec2 size = ImVec2(0.0f, 0.0f));
bool primaryButton(const char* label, ImVec2 size = ImVec2(0.0f, 0.0f));
bool dangerButton(const char* label, ImVec2 size = ImVec2(0.0f, 0.0f));
bool ghostButton(const char* label, ImVec2 size = ImVec2(0.0f, 0.0f));
// An icon glyph then a label: iconTextButton(icon::save, "Save").
bool iconTextButton(const char* icon, const char* label, Variant variant = Variant::secondary,
                    ImVec2 size = ImVec2(0.0f, 0.0f));
// A flat square icon button, tinted when active (a tool, a mode, play...).
bool iconButton(const char* icon, const char* tooltipText = nullptr, bool active = false, ImU32 activeColour = 0,
                ImVec2 size = ImVec2(0.0f, 0.0f));
// A compact rounded toggle that fills with `onColour` when on (M, S, R, a word).
bool toggleButton(const char* label, bool* value, ImU32 onColour = 0, ImVec2 size = ImVec2(0.0f, 0.0f),
                  const char* tooltipText = nullptr);
// An on/off switch with its label after it (settings).
bool toggleSwitch(const char* label, bool* value);
// Joined buttons, one selected: view modes, units, tabs in a toolbar.
// Returns true when the selection changed.
bool segmented(const char* id, const char* const labels[], int count, int* current, float width = 0.0f);
// A clickable "link" text.
bool linkText(const char* label);

// ---- Value controls ------------------------------------------------------------

enum KnobFlags : int {
    knobNone = 0,
    knobBipolar = 1 << 0,      // the arc starts at the centre (pan, detune)
    knobLogarithmic = 1 << 1,  // frequency-like ranges (min > 0)
    knobNoLabel = 1 << 2,
    knobInteger = 1 << 3,
};

struct KnobStyle {
    float diameter = 30.0f;     // in dp
    ImU32 colour = 0;           // 0: the accent
    const char* label = nullptr;  // under the knob; replaced by the value while hovered
    const char* format = "%.2f";
    const char* unit = nullptr;
    int flags = knobNone;
    float modulation = -1.0f;   // a live position (0..1) shown as a dot, <0 for none
    int steps = 0;              // snap to this many steps across the range; 0: continuous
};

// A rotary knob. Returns true while the value changes. Clicking its value
// (under it) without dragging lets it be typed.
bool knob(const char* id, float* value, float minimum, float maximum, float defaultValue, const KnobStyle& style = {});

// A vertical fader in dB with its scale. Returns true while it changes.
bool fader(const char* id, float* gainDb, float minimumDb, float maximumDb, ImVec2 size, ImU32 colour = 0);
// A level meter: peak and RMS of one or two channels, linear gain (1 = 0 dB).
void meter(const char* id, ImVec2 size, float peakLeft, float peakRight, float rmsLeft, float rmsRight,
           bool vertical = true);
// A meter's position (0..1) for a level in dB.
[[nodiscard]] float meterPosition(float gainDb);

// A labelled drag field for numbers: dragValue("Tempo", &bpm, 0.1f, 20, 300, "%.2f BPM").
bool dragValue(const char* id, float* value, float speed, float minimum, float maximum, const char* format = "%.2f",
               float width = 0.0f);
// A slider drawn as a filled bar with the value in it (inspectors).
bool sliderBar(const char* id, float* value, float minimum, float maximum, const char* format = "%.2f",
               float width = 0.0f, ImU32 colour = 0);

// A number typed in over a rectangle: call each frame; `open` opens it.
// Enter sets (a comma is a decimal point, "k" multiplies by 1000), Escape leaves.
bool valueEntry(ImGuiID id, ImVec2 min, ImVec2 max, double& value, double minimum, double maximum, bool open,
                double scale = 1.0);
[[nodiscard]] bool valueEntryOpen();

// The wheel this frame went to a control under the pointer: a view that
// scrolls with the wheel should leave it alone when wheelTaken().
void takeWheel();
[[nodiscard]] bool wheelTaken();
// Ctrl: 0.15 (fine), Shift: 4 (fast), else 1.
[[nodiscard]] float valueSpeed();

// ---- Fields --------------------------------------------------------------------

// A search box: magnifier, hint, a clear button. True when the text changed.
bool searchField(const char* id, char* buffer, int bufferSize, const char* hint = "Search", float width = 0.0f);
// A text field with a hint (placeholder). True when the text changed.
bool textField(const char* id, char* buffer, int bufferSize, const char* hint = nullptr, float width = 0.0f);
// A combo from a list of strings. True when the selection changed.
bool combo(const char* id, int* current, const char* const items[], int count, float width = 0.0f);

// ---- Containers ----------------------------------------------------------------

// A card: rounded raised ground (bg2), a line, padding, an optional title.
// Always call endCard() (even when beginCard returns false).
// height: 0 = fit content, >0 = fixed, <0 = fill minus that much.
bool beginCard(const char* id, const char* title = nullptr, float height = 0.0f, Tone tone = Tone::neutral);
void endCard();

// A collapsible section with a full-width header bar (inspectors).
// Call endSection() only when it returned true.
bool beginSection(const char* label, bool defaultOpen = true);
void endSection();

// Label / value rows in two aligned columns (inspectors, settings):
//   if (cyui::beginProperties("##transform")) {
//       cyui::property("Position");  ImGui::DragFloat2("##pos", pos);
//       cyui::property("Visible");   cyui::toggleSwitch("##vis", &visible);
//       cyui::endProperties();
//   }
// property() writes the label in the first column and readies the second
// (full width) for the control.
bool beginProperties(const char* id, float labelWidth = 0.0f);
void property(const char* label, const char* help = nullptr);
void endProperties();

// A thin vertical bar between toolbar groups (on the same line).
void toolbarSeparator();
// A horizontal line with optional spacing, in the theme's line colour.
void divider(float spacing = 4.0f);
// Vertical space in dp.
void space(float height = 8.0f);

// ---- Status & feedback -----------------------------------------------------------

// A small rounded pill: badge("BETA", Tone::warning).
void badge(const char* text, Tone tone = Tone::neutral);
// A coloured dot (status: online, error...), on the text line.
void statusDot(ImU32 colour, const char* tooltipText = nullptr);
// A thin progress bar; fraction < 0 draws an indeterminate one.
void progressBar(float fraction, ImVec2 size = ImVec2(-1.0f, 0.0f), const char* overlay = nullptr,
                 Tone tone = Tone::accent);
// A spinning arc (work in progress).
void spinner(const char* id, float radius = 7.0f, ImU32 colour = 0);
// A centred title and message filling the rest of a panel ("No layers yet").
void emptyState(const char* title, const char* message, const char* icon = nullptr);
// A notice block with a coloured bar: callout(Tone::warning, "Unsaved changes").
void callout(Tone tone, const char* text);
// A coloured square (track colour, swatch). True when clicked.
bool colourChip(const char* id, ImU32 colour, ImVec2 size);

// Toast notifications, bottom-right of the main viewport. notify() from
// anywhere; renderNotifications() once per frame, after everything else.
void notify(const char* text, Tone tone = Tone::neutral, float seconds = 4.0f);
void renderNotifications();

}  // namespace cyui
