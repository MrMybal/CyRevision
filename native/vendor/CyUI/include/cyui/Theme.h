#pragma once

// CyUI - Theme: one semantic palette, one set of metrics, the fonts.
//
// Every CyUI widget (and every custom-drawn control in an app using CyUI)
// takes its colours from cyui::palette() and its sizes from cyui::metrics(),
// never from hard-coded values. Changing theme is then one call.
//
// Requires Dear ImGui 1.92 or newer (dynamic font sizes, FontScaleDpi).

#include <imgui.h>

#include <string>
#include <vector>

#if !defined(IMGUI_VERSION_NUM) || IMGUI_VERSION_NUM < 19200
 #error "CyUI needs Dear ImGui 1.92 or newer"
#endif

namespace cyui {

// ---- Colours ---------------------------------------------------------------

// A colour from 0xRRGGBB (as written in a design tool), with an alpha.
// ImU32 is ImGui's packed ABGR.
constexpr ImU32 rgb(const unsigned int hex, const float a = 1.0f)
{
    return ((static_cast<unsigned int>(a * 255.0f + 0.5f) & 0xffu) << 24) | ((hex & 0xffu) << 16) | (hex & 0xff00u)
           | ((hex >> 16) & 0xffu);
}

// The semantic palette. Names say what a colour is FOR, not what it looks like:
// pick by role ("a field's ground" = bg3), never by hue.
struct Palette {
    // Grounds, from the deepest to the most raised.
    ImU32 bg0;  // deepest: canvases, timelines, menu bar, status bar, wells
    ImU32 bg1;  // window / panel ground
    ImU32 bg2;  // headers, cards, popups, table headers
    ImU32 bg3;  // fields and buttons at rest
    ImU32 bg4;  // hovered fields and buttons
    ImU32 bg5;  // pressed / active fields, knob tracks, scrollbar grabs
    // Lines.
    ImU32 line;        // borders, separators
    ImU32 lineStrong;  // seams between panels, table outer borders, toolbar separators
    ImU32 grid;        // minor grid in canvases
    ImU32 gridStrong;  // major grid
    // Text.
    ImU32 text;       // primary text
    ImU32 textDim;    // secondary text, labels, inactive icons
    ImU32 textFaint;  // hints, disabled, placeholders
    // The one accent: what is active, selected, focused, the main action.
    ImU32 accent;
    ImU32 accentHover;
    ImU32 accentDim;   // subdued accent (active-but-not-focused, drag handles)
    ImU32 accentText;  // text drawn ON an accent fill
    ImU32 selection;   // translucent selection fill (rows, rectangles, text)
    // Meaning.
    ImU32 success;
    ImU32 warning;
    ImU32 error;
    ImU32 info;
};

// A named theme: palette + a density preset key.
struct Theme {
    std::string key;   // "mid-grey", "amber-night", ...
    std::string name;  // shown to people
    Palette palette {};
    bool light {};     // a light theme (shadows, overlays adapt)
};

[[nodiscard]] const Palette& palette();
// The palette drawn with from now on; ImGui's own colours follow.
void setPalette(const Palette& palette);

// Built-in themes, extracted from CyDAW 2, Shaderixel and CyEngineHub:
//   "mid-grey"     CyDAW 2 default: Bitwig-like mid greys, orange accent
//   "light-grey"   CyDAW 2: Ableton-like light greys, orange accent
//   "amber-night"  CyDAW 2 first dark: deep blue-greys, amber accent
//   "violet"       Shaderixel: dark slate, violet accent, cyan secondary
//   "navy"         CyEngineHub: very dark navy, blue accent
[[nodiscard]] const std::vector<Theme>& builtInThemes();
inline constexpr const char* defaultThemeKey = "mid-grey";
// Applies a built-in theme by key; false when unknown.
bool setTheme(const char* key);
[[nodiscard]] const std::string& currentThemeKey();

// The palette's fields by name, for editors and theme files.
struct PaletteField {
    const char* key;
    const char* label;
    int group;  // 0 grounds, 1 lines, 2 text, 3 accent, 4 meaning
    ImU32 Palette::*member;
};
[[nodiscard]] const std::vector<PaletteField>& paletteFields();

// Theme files (.cytheme): lines `key = #RRGGBB` or `#RRGGBBAA`. A JSON
// object `{ "key": "#RRGGBB", ... }` (CyDAW 2's .cytheme) is read as well.
// What the text lacks keeps `base`'s colour.
[[nodiscard]] std::string paletteToText(const Palette& palette);
[[nodiscard]] Palette paletteFromText(const std::string& text, const Palette& base);
bool savePalette(const char* path, const Palette& palette);
bool loadPalette(const char* path, Palette& palette);

// Colour helpers (all keep ImGui's ABGR packing).
[[nodiscard]] ImU32 alpha(ImU32 colour, float alphaFactor);  // multiplies the alpha
[[nodiscard]] ImU32 mix(ImU32 a, ImU32 b, float t);
[[nodiscard]] ImU32 brighter(ImU32 colour, float amount);  // towards white
[[nodiscard]] ImU32 darker(ImU32 colour, float amount);    // towards black
[[nodiscard]] ImVec4 vec(ImU32 colour);
// The n-th colour of a pleasant categorical set (tracks, layers, tags, series).
[[nodiscard]] ImU32 categorical(int index);

// ---- Metrics ---------------------------------------------------------------

enum class Density {
    compact,      // CyDAW 2: dense pro tool, 14 px text
    comfortable,  // Shaderixel / CyEngineHub: airier, 15-16 px text
};

struct Metrics {
    float fontSize;         // base text size, before DPI
    ImVec2 windowPadding;
    ImVec2 framePadding;
    ImVec2 itemSpacing;
    ImVec2 itemInnerSpacing;
    ImVec2 cellPadding;
    float indent;
    float scrollbar;
    float grabMin;
    float rounding;         // frames, buttons, fields
    float roundingLarge;    // windows, popups, cards
    float roundingSmall;    // grabs, chips, badges
    float cardPadding;      // inside cyui::beginCard
    float toolbarHeight;    // extra height around a frame for top bars
};

[[nodiscard]] const Metrics& metrics();
void setDensity(Density density);
[[nodiscard]] Density density();

// ---- Applying --------------------------------------------------------------

struct Config {
    const char* themeKey = defaultThemeKey;
    Density density = Density::compact;
    float dpiScale = 1.0f;
    // Square, opaque windows when each ImGui window is an OS window of its
    // own (multi-viewports, plug-in editors).
    bool squareWindows = false;
};

// Sets ImGui's style (sizes, roundings, colours) from the theme, scaled.
// Call after ImGui::CreateContext(), and again when the DPI changes.
void apply(const Config& config = {});
// Re-applies with the current settings and a new DPI scale.
void applyDpi(float dpiScale);
[[nodiscard]] float dpi();

// Scales a design value (in 96-dpi pixels) to the screen: use for every
// hard-coded size in custom drawing.
[[nodiscard]] inline float dp(const float value)
{
    return value * ImGui::GetStyle().FontScaleDpi;
}
[[nodiscard]] inline ImVec2 dp(const float x, const float y)
{
    return ImVec2(dp(x), dp(y));
}

// ---- Fonts -----------------------------------------------------------------

struct FontConfig {
    float size = 0.0f;  // 0: metrics().fontSize
    // Folder with fallback .ttf files (Inter, Roboto, Cousine...) tried when
    // the Windows fonts are missing; may be null.
    const char* fallbackFolder = nullptr;
    // Merge Segoe MDL2 Assets (Windows) so cyui::icon:: glyphs draw inline.
    bool icons = true;
};

// Loads the interface font (Segoe UI), semibold (Segoe UI Semibold), mono
// (Consolas), each with the icon glyphs merged; falls back to ImGui's own
// font. Call before the first frame (the backend builds the atlas).
void loadFonts(const FontConfig& config = {});
[[nodiscard]] ImFont* uiFont();
[[nodiscard]] ImFont* boldFont();
[[nodiscard]] ImFont* monoFont();
// Whether the icon font was found (icon glyphs render as boxes otherwise).
[[nodiscard]] bool iconsAvailable();

}  // namespace cyui
