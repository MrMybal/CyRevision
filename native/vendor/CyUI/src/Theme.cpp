#if defined(_MSC_VER) && !defined(_CRT_SECURE_NO_WARNINGS)
 #define _CRT_SECURE_NO_WARNINGS
#endif

#include "cyui/Theme.h"

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace cyui {
namespace {

// CyDAW 2 default: Bitwig's mid greys, dark seams between panels, orange.
const Palette kMidGrey {
    rgb(0x262626), rgb(0x2e2e2e), rgb(0x373737), rgb(0x414141), rgb(0x4b4b4b), rgb(0x565656),
    rgb(0x1f1f1f), rgb(0x181818), rgb(0x2d2d2d), rgb(0x383838),
    rgb(0xdadada), rgb(0xa6a6a6), rgb(0x707070),
    rgb(0xf28a2e), rgb(0xffa552), rgb(0x7d4a1d), rgb(0x1e1e1e), rgb(0xf28a2e, 0.24f),
    rgb(0x5cd26e), rgb(0xf5a623), rgb(0xee5149), rgb(0x5cb4ef),
};

// CyDAW 2: Ableton Live's light greys, dark text, orange, blue selections.
const Palette kLightGrey {
    rgb(0xb3b3b3), rgb(0xc2c2c2), rgb(0xcbcbcb), rgb(0xd6d6d6), rgb(0xe0e0e0), rgb(0xebebeb),
    rgb(0x9c9c9c), rgb(0x7f7f7f), rgb(0xa9a9a9), rgb(0x969696),
    rgb(0x161616), rgb(0x3d3d3d), rgb(0x6e6e6e),
    rgb(0xff8a00), rgb(0xffa233), rgb(0xf0c27e), rgb(0x161616), rgb(0x3d8eff, 0.26f),
    rgb(0x15a34e), rgb(0xd98200), rgb(0xe0302b), rgb(0x2a86d1),
};

// CyDAW 2's first dark one: deep blue-greys, an amber accent.
const Palette kAmberNight {
    rgb(0x111317), rgb(0x16181d), rgb(0x1c1f25), rgb(0x24282f), rgb(0x2c313a), rgb(0x363c47),
    rgb(0x262a31), rgb(0x363b45), rgb(0x1f2228), rgb(0x2b2f37),
    rgb(0xdce1e8), rgb(0x9aa3b0), rgb(0x5f6875),
    rgb(0xf0a43a), rgb(0xf8b95c), rgb(0x7a5524), rgb(0x111317), rgb(0xf0a43a, 0.22f),
    rgb(0x3dd68c), rgb(0xf5a623), rgb(0xe5484d), rgb(0x5ab0f0),
};

// Shaderixel: dark slate, a violet accent, cyan for information.
const Palette kViolet {
    rgb(0x16171b), rgb(0x1b1c21), rgb(0x222329), rgb(0x282a32), rgb(0x343640), rgb(0x40424e),
    rgb(0x30323a), rgb(0x3a3c46), rgb(0xffffff, 0.04f), rgb(0xffffff, 0.09f),
    rgb(0xdee0e8), rgb(0x8c909e), rgb(0x686c7a),
    rgb(0x7c5cff), rgb(0x967cff), rgb(0x4c3fa0), rgb(0xffffff), rgb(0x7c5cff, 0.35f),
    rgb(0x4cd4a0), rgb(0xf5a623), rgb(0xf0505a), rgb(0x2ec4d6),
};

// CyEngineHub: a very dark navy, flat, a blue accent.
const Palette kNavy {
    rgb(0x0a0e13), rgb(0x0d1218), rgb(0x111820), rgb(0x18212c), rgb(0x1f2a37), rgb(0x263343),
    rgb(0x1c2632), rgb(0x283748), rgb(0x111820), rgb(0x1c2632),
    rgb(0xd9e2ec), rgb(0xa4b3c2), rgb(0x65788d),
    rgb(0x2d7ff9), rgb(0x4a93ff), rgb(0x1d4e7a), rgb(0xffffff), rgb(0x2d7ff9, 0.25f),
    rgb(0x65cca9), rgb(0xf2b84b), rgb(0xe8747c), rgb(0x70a8fa),
};

const Metrics kCompact {
    14.0f, ImVec2(8.0f, 6.0f), ImVec2(6.0f, 3.0f), ImVec2(6.0f, 4.0f), ImVec2(4.0f, 4.0f), ImVec2(5.0f, 2.0f),
    14.0f, 10.0f, 9.0f, 4.0f, 6.0f, 3.0f, 10.0f, 14.0f,
};

const Metrics kComfortable {
    16.0f, ImVec2(10.0f, 10.0f), ImVec2(8.0f, 5.0f), ImVec2(8.0f, 6.0f), ImVec2(6.0f, 4.0f), ImVec2(6.0f, 4.0f),
    16.0f, 11.0f, 10.0f, 6.0f, 8.0f, 4.0f, 14.0f, 16.0f,
};

Palette gPalette = kMidGrey;
std::string gThemeKey = defaultThemeKey;
bool gLight = false;
Density gDensity = Density::compact;
Metrics gMetrics = kCompact;
Config gConfig {};
float gDpi = 1.0f;
bool gIcons = false;

void applyColours(ImGuiStyle& style)
{
    const auto& p = gPalette;
    auto* c = style.Colors;
    c[ImGuiCol_Text] = vec(p.text);
    c[ImGuiCol_TextDisabled] = vec(p.textFaint);
    c[ImGuiCol_WindowBg] = vec(p.bg1);
    c[ImGuiCol_ChildBg] = vec(alpha(p.bg2, 0.0f));
    c[ImGuiCol_PopupBg] = vec(alpha(p.bg2, 0.98f));
    c[ImGuiCol_Border] = vec(p.line);
    c[ImGuiCol_BorderShadow] = vec(0);
    c[ImGuiCol_FrameBg] = vec(p.bg3);
    c[ImGuiCol_FrameBgHovered] = vec(p.bg4);
    c[ImGuiCol_FrameBgActive] = vec(p.bg5);
    c[ImGuiCol_TitleBg] = vec(p.bg0);
    c[ImGuiCol_TitleBgActive] = vec(p.bg0);
    c[ImGuiCol_TitleBgCollapsed] = vec(p.bg0);
    c[ImGuiCol_MenuBarBg] = vec(p.bg0);
    c[ImGuiCol_ScrollbarBg] = vec(alpha(p.bg0, 0.0f));
    c[ImGuiCol_ScrollbarGrab] = vec(p.bg5);
    c[ImGuiCol_ScrollbarGrabHovered] = vec(brighter(p.bg5, 0.12f));
    c[ImGuiCol_ScrollbarGrabActive] = vec(p.accentDim);
    c[ImGuiCol_CheckMark] = vec(p.accent);
    c[ImGuiCol_SliderGrab] = vec(p.accent);
    c[ImGuiCol_SliderGrabActive] = vec(p.accentHover);
    c[ImGuiCol_Button] = vec(p.bg3);
    c[ImGuiCol_ButtonHovered] = vec(p.bg4);
    c[ImGuiCol_ButtonActive] = vec(p.bg5);
    c[ImGuiCol_Header] = vec(alpha(p.accent, 0.16f));
    c[ImGuiCol_HeaderHovered] = vec(p.bg4);
    c[ImGuiCol_HeaderActive] = vec(alpha(p.accent, 0.26f));
    c[ImGuiCol_Separator] = vec(p.line);
    c[ImGuiCol_SeparatorHovered] = vec(p.accentDim);
    c[ImGuiCol_SeparatorActive] = vec(p.accent);
    c[ImGuiCol_ResizeGrip] = vec(alpha(p.bg5, 0.0f));
    c[ImGuiCol_ResizeGripHovered] = vec(p.accentDim);
    c[ImGuiCol_ResizeGripActive] = vec(p.accent);
    c[ImGuiCol_TabHovered] = vec(p.bg4);
    c[ImGuiCol_Tab] = vec(p.bg0);
    c[ImGuiCol_TabSelected] = vec(p.bg1);
    c[ImGuiCol_TabSelectedOverline] = vec(p.accent);
    c[ImGuiCol_TabDimmed] = vec(p.bg0);
    c[ImGuiCol_TabDimmedSelected] = vec(p.bg1);
    c[ImGuiCol_TabDimmedSelectedOverline] = vec(alpha(p.accent, 0.35f));
#ifdef IMGUI_HAS_DOCK
    c[ImGuiCol_DockingPreview] = vec(alpha(p.accent, 0.45f));
    c[ImGuiCol_DockingEmptyBg] = vec(p.bg0);
#endif
    c[ImGuiCol_PlotLines] = vec(p.info);
    c[ImGuiCol_PlotLinesHovered] = vec(p.accent);
    c[ImGuiCol_PlotHistogram] = vec(p.accent);
    c[ImGuiCol_PlotHistogramHovered] = vec(p.accentHover);
    c[ImGuiCol_TableHeaderBg] = vec(p.bg2);
    c[ImGuiCol_TableBorderStrong] = vec(p.lineStrong);
    c[ImGuiCol_TableBorderLight] = vec(p.line);
    c[ImGuiCol_TableRowBg] = vec(0);
    c[ImGuiCol_TableRowBgAlt] = vec(alpha(p.text, 0.025f));
    c[ImGuiCol_TextLink] = vec(p.info);
    c[ImGuiCol_TextSelectedBg] = vec(alpha(p.accent, 0.30f));
    c[ImGuiCol_DragDropTarget] = vec(p.accent);
    c[ImGuiCol_NavCursor] = vec(p.accent);
    c[ImGuiCol_NavWindowingHighlight] = vec(alpha(p.accent, 0.7f));
    c[ImGuiCol_NavWindowingDimBg] = vec(rgb(0x000000, 0.35f));
    c[ImGuiCol_ModalWindowDimBg] = vec(rgb(0x000000, gLight ? 0.25f : 0.45f));
#if IMGUI_VERSION_NUM >= 19200
    c[ImGuiCol_InputTextCursor] = vec(p.accent);
    c[ImGuiCol_TreeLines] = vec(p.line);
#endif
#if IMGUI_VERSION_NUM >= 19297 // verified on 1.93.0 WIP; may exist earlier
    c[ImGuiCol_DragDropTargetBg] = vec(alpha(p.accent, 0.10f));
    c[ImGuiCol_UnsavedMarker] = vec(p.accent);
#endif
#if IMGUI_VERSION_NUM >= 19297
    c[ImGuiCol_CheckboxSelectedBg] = vec(p.bg4);
#endif
    if (gConfig.squareWindows)
        c[ImGuiCol_WindowBg].w = 1.0f;
}

void applyStyle()
{
    auto& style = ImGui::GetStyle();
    style = ImGuiStyle();
    const auto& m = gMetrics;
    style.FontSizeBase = m.fontSize;
    style.WindowPadding = m.windowPadding;
    style.FramePadding = m.framePadding;
    style.ItemSpacing = m.itemSpacing;
    style.ItemInnerSpacing = m.itemInnerSpacing;
    style.CellPadding = m.cellPadding;
    style.IndentSpacing = m.indent;
    style.ScrollbarSize = m.scrollbar;
#if IMGUI_VERSION_NUM >= 19297
    style.ScrollbarPadding = 1.0f;
#endif
    style.GrabMinSize = m.grabMin;
    style.WindowBorderSize = 1.0f;
    style.ChildBorderSize = 1.0f;
    style.PopupBorderSize = 1.0f;
    style.FrameBorderSize = 0.0f;
    style.TabBorderSize = 0.0f;
    style.TabBarBorderSize = 1.0f;
    style.TabBarOverlineSize = 2.0f;
    style.WindowRounding = m.roundingLarge;
    style.ChildRounding = m.roundingLarge - 1.0f;
    style.FrameRounding = m.rounding;
    style.PopupRounding = m.roundingLarge;
    style.ScrollbarRounding = 8.0f;
    style.GrabRounding = m.roundingSmall;
    style.TabRounding = m.rounding + 1.0f;
#if IMGUI_VERSION_NUM >= 19297
    style.MenuItemRounding = m.rounding;
    style.SelectableRounding = m.rounding;
#endif
    style.WindowTitleAlign = ImVec2(0.0f, 0.5f);
    style.WindowMenuButtonPosition = ImGuiDir_None;
    style.SeparatorTextBorderSize = 1.0f;
    style.SeparatorTextPadding = ImVec2(10.0f, 2.0f);
#ifdef IMGUI_HAS_DOCK
    style.DockingSeparatorSize = 2.0f;
    style.DockingNodeHasCloseButton = false;
#endif
    style.WindowMinSize = ImVec2(60.0f, 40.0f);
    style.HoverDelayShort = 0.25f;
    style.HoverDelayNormal = 0.55f;
    style.AntiAliasedLines = true;
    style.AntiAliasedFill = true;
    applyColours(style);

    style.ScaleAllSizes(gDpi);
    if (gConfig.squareWindows)
        style.WindowRounding = 0.0f;
    style.FontScaleDpi = gDpi;
}

unsigned int hexDigit(const char c)
{
    if (c >= '0' && c <= '9')
        return static_cast<unsigned int>(c - '0');
    if (c >= 'a' && c <= 'f')
        return static_cast<unsigned int>(c - 'a' + 10);
    if (c >= 'A' && c <= 'F')
        return static_cast<unsigned int>(c - 'A' + 10);
    return 16;
}

bool fileExists(const std::string& path)
{
    if (path.empty())
        return false;
    if (auto* file = std::fopen(path.c_str(), "rb")) {
        std::fclose(file);
        return true;
    }
    return false;
}

std::string windowsFont(const char* name)
{
#if defined(_WIN32)
    const char* windows = std::getenv("WINDIR");
    return std::string(windows != nullptr ? windows : "C:\\Windows") + "\\Fonts\\" + name;
#else
    (void) name;
    return {};
#endif
}

std::string inFolder(const char* folder, const char* name)
{
    if (folder == nullptr || folder[0] == 0)
        return {};
    std::string out(folder);
    if (out.back() != '/' && out.back() != '\\')
        out += '/';
    return out + name;
}

// The fonts in the order loadFonts adds them; looked up in the current
// context's atlas, so several contexts (plug-in windows) each find theirs.
ImFont* fontAt(const int index)
{
    if (ImGui::GetCurrentContext() == nullptr)
        return nullptr;
    const auto& fonts = ImGui::GetIO().Fonts->Fonts;
    if (fonts.Size == 0)
        return nullptr;
    return index < fonts.Size ? fonts[index] : fonts[0];
}

ImFont* addFont(const std::vector<std::string>& candidates, const float size)
{
    auto& io = ImGui::GetIO();
    ImFontConfig config;
    config.OversampleH = 2;
    config.OversampleV = 2;
    config.PixelSnapH = true;
    for (const auto& path : candidates)
        if (fileExists(path))
            if (auto* font = io.Fonts->AddFontFromFileTTF(path.c_str(), size, &config))
                return font;
    return io.Fonts->AddFontDefault();
}

void mergeIcons(const float size, const bool wanted)
{
    if (!wanted)
        return;
    const auto icons = windowsFont("segmdl2.ttf");
    if (!fileExists(icons))
        return;
    ImFontConfig config;
    config.MergeMode = true;
    config.PixelSnapH = true;
    config.GlyphOffset = ImVec2(0.0f, 2.0f);
    config.GlyphMinAdvanceX = size;
    if (ImGui::GetIO().Fonts->AddFontFromFileTTF(icons.c_str(), size * 0.95f, &config) != nullptr)
        gIcons = true;
}

}  // namespace

// ---- Palette -----------------------------------------------------------------

const Palette& palette()
{
    return gPalette;
}

void setPalette(const Palette& value)
{
    gPalette = value;
    if (ImGui::GetCurrentContext() != nullptr)
        applyColours(ImGui::GetStyle());
}

const std::vector<Theme>& builtInThemes()
{
    static const std::vector<Theme> themes {
        { "mid-grey", "Mid grey (CyDAW)", kMidGrey, false },
        { "light-grey", "Light grey (CyDAW)", kLightGrey, true },
        { "amber-night", "Amber night (CyDAW)", kAmberNight, false },
        { "violet", "Violet (Shaderixel)", kViolet, false },
        { "navy", "Navy (CyEngineHub)", kNavy, false },
    };
    return themes;
}

bool setTheme(const char* key)
{
    for (const auto& theme : builtInThemes())
        if (key != nullptr && theme.key == key) {
            gThemeKey = theme.key;
            gLight = theme.light;
            setPalette(theme.palette);
            return true;
        }
    return false;
}

const std::string& currentThemeKey()
{
    return gThemeKey;
}

const std::vector<PaletteField>& paletteFields()
{
    static const std::vector<PaletteField> fields {
        { "bg0", "Deepest ground (canvas, bars)", 0, &Palette::bg0 },
        { "bg1", "Panel ground", 0, &Palette::bg1 },
        { "bg2", "Header / card ground", 0, &Palette::bg2 },
        { "bg3", "Fields and buttons", 0, &Palette::bg3 },
        { "bg4", "Hover", 0, &Palette::bg4 },
        { "bg5", "Active", 0, &Palette::bg5 },
        { "line", "Lines", 1, &Palette::line },
        { "lineStrong", "Strong lines", 1, &Palette::lineStrong },
        { "grid", "Grid", 1, &Palette::grid },
        { "gridStrong", "Grid (major)", 1, &Palette::gridStrong },
        { "text", "Text", 2, &Palette::text },
        { "textDim", "Secondary text", 2, &Palette::textDim },
        { "textFaint", "Faint text", 2, &Palette::textFaint },
        { "accent", "Accent", 3, &Palette::accent },
        { "accentHover", "Accent hovered", 3, &Palette::accentHover },
        { "accentDim", "Accent dimmed", 3, &Palette::accentDim },
        { "accentText", "Text on accent", 3, &Palette::accentText },
        { "selection", "Selection", 3, &Palette::selection },
        { "success", "Success", 4, &Palette::success },
        { "warning", "Warning", 4, &Palette::warning },
        { "error", "Error", 4, &Palette::error },
        { "info", "Information", 4, &Palette::info },
    };
    return fields;
}

std::string paletteToText(const Palette& value)
{
    std::string out = "// CyUI theme: key = #RRGGBB or #RRGGBBAA\n";
    for (const auto& field : paletteFields()) {
        const auto colour = value.*field.member;
        const auto r = colour & 0xffu;
        const auto g = (colour >> 8) & 0xffu;
        const auto b = (colour >> 16) & 0xffu;
        const auto a = (colour >> 24) & 0xffu;
        char line[96];
        if (a == 0xffu)
            std::snprintf(line, sizeof(line), "%s = #%02X%02X%02X\n", field.key, r, g, b);
        else
            std::snprintf(line, sizeof(line), "%s = #%02X%02X%02X%02X\n", field.key, r, g, b, a);
        out += line;
    }
    return out;
}

Palette paletteFromText(const std::string& text, const Palette& base)
{
    auto out = base;
    std::string key;
    for (std::size_t i = 0; i < text.size();) {
        const auto c = text[i];
        if (c == '/' && i + 1 < text.size() && text[i + 1] == '/') {
            while (i < text.size() && text[i] != '\n')
                ++i;
            continue;
        }
        if (std::isalpha(static_cast<unsigned char>(c)) || c == '_') {
            key.clear();
            while (i < text.size() && (std::isalnum(static_cast<unsigned char>(text[i])) || text[i] == '_'))
                key += text[i++];
            continue;
        }
        if (c == '#') {
            ++i;
            unsigned int value = 0;
            int digits = 0;
            while (i < text.size() && digits < 8 && hexDigit(text[i]) < 16) {
                value = (value << 4) | hexDigit(text[i++]);
                ++digits;
            }
            if (digits == 6 || digits == 8) {
                const auto a = digits == 8 ? value & 0xffu : 0xffu;
                const auto colour = digits == 8 ? value >> 8 : value;
                for (const auto& field : paletteFields())
                    if (key == field.key)
                        out.*field.member = (a << 24) | ((colour & 0xffu) << 16) | (colour & 0xff00u) | ((colour >> 16) & 0xffu);
            }
            key.clear();
            continue;
        }
        ++i;
    }
    return out;
}

bool savePalette(const char* path, const Palette& value)
{
    auto* file = std::fopen(path, "wb");
    if (file == nullptr)
        return false;
    const auto text = paletteToText(value);
    const auto written = std::fwrite(text.data(), 1, text.size(), file);
    std::fclose(file);
    return written == text.size();
}

bool loadPalette(const char* path, Palette& value)
{
    auto* file = std::fopen(path, "rb");
    if (file == nullptr)
        return false;
    std::string text;
    char buffer[4096];
    std::size_t count = 0;
    while ((count = std::fread(buffer, 1, sizeof(buffer), file)) > 0 && text.size() < 1024 * 1024)
        text.append(buffer, count);
    std::fclose(file);
    value = paletteFromText(text, value);
    return true;
}

ImU32 alpha(const ImU32 colour, const float factor)
{
    const auto current = static_cast<float>((colour >> 24) & 0xffu) / 255.0f;
    const auto byte = static_cast<unsigned int>(std::clamp(current * factor, 0.0f, 1.0f) * 255.0f + 0.5f);
    return (colour & 0x00ffffffu) | (byte << 24);
}

ImU32 mix(const ImU32 a, const ImU32 b, const float t)
{
    const auto lerp = [t](const unsigned int x, const unsigned int y) {
        return static_cast<unsigned int>(static_cast<float>(x) + (static_cast<float>(y) - static_cast<float>(x)) * t + 0.5f);
    };
    unsigned int out = 0;
    for (auto shift = 0; shift < 32; shift += 8)
        out |= (lerp((a >> shift) & 0xffu, (b >> shift) & 0xffu) & 0xffu) << shift;
    return out;
}

ImU32 brighter(const ImU32 colour, const float amount)
{
    return mix(colour, (colour & 0xff000000u) | 0x00ffffffu, amount);
}

ImU32 darker(const ImU32 colour, const float amount)
{
    return mix(colour, colour & 0xff000000u, amount);
}

ImVec4 vec(const ImU32 colour)
{
    return ImGui::ColorConvertU32ToFloat4(colour);
}

ImU32 categorical(const int index)
{
    static constexpr unsigned int colours[] = { 0xf0a43a, 0x5ab0f0, 0x52d1a2, 0xc792ea, 0xe8765a, 0x7fd1e8,
                                                0xe8c547, 0x9ccc65, 0xf06292, 0x8c9eff, 0x4dd0b5, 0xffb74d };
    constexpr auto count = static_cast<int>(sizeof(colours) / sizeof(colours[0]));
    return rgb(colours[((index % count) + count) % count]);
}

// ---- Metrics -----------------------------------------------------------------

const Metrics& metrics()
{
    return gMetrics;
}

void setDensity(const Density value)
{
    gDensity = value;
    gMetrics = value == Density::compact ? kCompact : kComfortable;
    gConfig.density = value;
    if (ImGui::GetCurrentContext() != nullptr)
        applyStyle();
}

Density density()
{
    return gDensity;
}

// ---- Applying ----------------------------------------------------------------

void apply(const Config& config)
{
    gConfig = config;
    gDpi = std::max(0.5f, config.dpiScale);
    gDensity = config.density;
    gMetrics = config.density == Density::compact ? kCompact : kComfortable;
    if (!setTheme(config.themeKey))
        setTheme(defaultThemeKey);
    applyStyle();
}

void applyDpi(const float dpiScale)
{
    gConfig.dpiScale = dpiScale;
    gDpi = std::max(0.5f, dpiScale);
    applyStyle();
}

float dpi()
{
    return gDpi;
}

// ---- Fonts -------------------------------------------------------------------

void loadFonts(const FontConfig& config)
{
    auto& io = ImGui::GetIO();
    io.Fonts->Clear();
    gIcons = false;
    const auto size = config.size > 0.0f ? config.size : gMetrics.fontSize;
    const auto* folder = config.fallbackFolder;
    addFont({ windowsFont("segoeui.ttf"), inFolder(folder, "Inter-Regular.ttf"), inFolder(folder, "Roboto-Regular.ttf"),
              inFolder(folder, "Roboto-Medium.ttf") },
            size);
    mergeIcons(size, config.icons);
    addFont({ windowsFont("seguisb.ttf"), inFolder(folder, "Inter-SemiBold.ttf"), inFolder(folder, "Roboto-Bold.ttf"),
              inFolder(folder, "Roboto-Medium.ttf") },
            size);
    mergeIcons(size, config.icons);
    addFont({ windowsFont("consola.ttf"), inFolder(folder, "JetBrainsMono-Regular.ttf"), inFolder(folder, "Cousine-Regular.ttf") },
            size - 1.0f);
    io.FontDefault = fontAt(0);
}

ImFont* uiFont()
{
    return fontAt(0);
}

ImFont* boldFont()
{
    return fontAt(1);
}

ImFont* monoFont()
{
    return fontAt(2);
}

bool iconsAvailable()
{
    return gIcons;
}

}  // namespace cyui
