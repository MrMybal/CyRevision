#if defined(_MSC_VER) && !defined(_CRT_SECURE_NO_WARNINGS)
 #define _CRT_SECURE_NO_WARNINGS
#endif

#include "cyui/Theme.h"
#include "cyui/Locale.h"

#include <imgui_internal.h>

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <atomic>
#include <cstring>
#include <mutex>
#include <unordered_map>

namespace cyui {
namespace {

// Mid greys, dark seams between panels and an orange accent.
const Palette kMidGrey {
    rgb(0x262626), rgb(0x2e2e2e), rgb(0x373737), rgb(0x414141), rgb(0x4b4b4b), rgb(0x565656),
    rgb(0x1f1f1f), rgb(0x181818), rgb(0x2d2d2d), rgb(0x383838),
    rgb(0xdadada), rgb(0xa6a6a6), rgb(0x707070),
    rgb(0xf28a2e), rgb(0xffa552), rgb(0x7d4a1d), rgb(0x1e1e1e), rgb(0xf28a2e, 0.24f),
    rgb(0x5cd26e), rgb(0xf5a623), rgb(0xee5149), rgb(0x5cb4ef),
};

// Light greys, dark text, an orange accent and blue selections.
const Palette kLightGrey {
    rgb(0xb3b3b3), rgb(0xc2c2c2), rgb(0xcbcbcb), rgb(0xd6d6d6), rgb(0xe0e0e0), rgb(0xebebeb),
    rgb(0x9c9c9c), rgb(0x7f7f7f), rgb(0xa9a9a9), rgb(0x969696),
    rgb(0x161616), rgb(0x3d3d3d), rgb(0x6e6e6e),
    rgb(0xff8a00), rgb(0xffa233), rgb(0xf0c27e), rgb(0x161616), rgb(0x3d8eff, 0.26f),
    rgb(0x15a34e), rgb(0xd98200), rgb(0xe0302b), rgb(0x2a86d1),
};

// Deep blue-greys and an amber accent.
const Palette kAmberNight {
    rgb(0x111317), rgb(0x16181d), rgb(0x1c1f25), rgb(0x24282f), rgb(0x2c313a), rgb(0x363c47),
    rgb(0x262a31), rgb(0x363b45), rgb(0x1f2228), rgb(0x2b2f37),
    rgb(0xdce1e8), rgb(0x9aa3b0), rgb(0x5f6875),
    rgb(0xf0a43a), rgb(0xf8b95c), rgb(0x7a5524), rgb(0x111317), rgb(0xf0a43a, 0.22f),
    rgb(0x3dd68c), rgb(0xf5a623), rgb(0xe5484d), rgb(0x5ab0f0),
};

// Dark slate, a violet accent and cyan for information.
const Palette kViolet {
    rgb(0x16171b), rgb(0x1b1c21), rgb(0x222329), rgb(0x282a32), rgb(0x343640), rgb(0x40424e),
    rgb(0x30323a), rgb(0x3a3c46), rgb(0xffffff, 0.04f), rgb(0xffffff, 0.09f),
    rgb(0xdee0e8), rgb(0x8c909e), rgb(0x686c7a),
    rgb(0x7c5cff), rgb(0x967cff), rgb(0x4c3fa0), rgb(0xffffff), rgb(0x7c5cff, 0.35f),
    rgb(0x4cd4a0), rgb(0xf5a623), rgb(0xf0505a), rgb(0x2ec4d6),
};

// Deep navy surfaces and a blue accent.
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

// Everything a theme call changes, one per ImGui context (plug-in windows,
// Unreal panels), so contexts never mix themes or fonts. Calls made with no
// current context change the defaults and every live context.
struct ThemeState {
    Palette palette = kMidGrey;
    std::string themeKey = defaultThemeKey;
    std::string locale = "en";
    std::vector<std::string> localizedImGui;
    Density density = Density::compact;
    Metrics metrics = kCompact;
    bool squareWindows = false;
    float dpi = 1.0f;
    // What loadFonts added, in which atlas.
    ImFontAtlas* atlas {};
    ImFont* fonts[3] {};
    bool icons = false;
    // What the ImGui style was last built from (applyDpi skips a no-op).
    bool styled = false;
    float styledDpi = 0.0f;
    Density styledDensity = Density::compact;
    bool hooked = false;  // released with its context
};
void applyLocale(ThemeState& state);

// Contexts may live on different threads: the map is locked, and each thread
// caches its last lookup, valid while no context was released since.
bool gThemesAlive = false;  // false again once static destruction removed the map
struct ThemeRegistry {
    std::mutex mutex;
    std::unordered_map<ImGuiContext*, ThemeState> states;
    std::atomic<unsigned> generation { 0 };
    ThemeRegistry() { gThemesAlive = true; }
    ~ThemeRegistry() { gThemesAlive = false; }
};
ThemeState gDefaults;
ThemeRegistry gThemes;
thread_local ImGuiContext* tCachedContext {};
thread_local ThemeState* tCached {};
thread_local unsigned tCachedGeneration {};

void releaseTheme(ImGuiContext* closing, ImGuiContextHook*)
{
    if (!gThemesAlive)
        return;
    std::lock_guard<std::mutex> lock(gThemes.mutex);
    gThemes.states.erase(closing);
    ++gThemes.generation;
}

// Adds a shutdown hook, unless ImGui may be walking the hook list right now
// (CyUI first used from another hook) and adding would reallocate it: then
// the caller tries again later.
bool hookShutdown(ImGuiContext* context, const ImGuiContextHookCallback callback)
{
    auto& hooks = context->Hooks;
    if (hooks.Size == 0)
        hooks.reserve(8);
    else if (hooks.Size == hooks.Capacity)
        return false;
    ImGuiContextHook hook;
    hook.Type = ImGuiContextHookType_Shutdown;
    hook.Callback = callback;
    ImGui::AddContextHook(context, &hook);
    return true;
}

ThemeState& themeState()
{
    auto* context = ImGui::GetCurrentContext();
    if (context == nullptr)
        return gDefaults;
    if (context == tCachedContext && tCachedGeneration == gThemes.generation.load())
        return *tCached;
    std::lock_guard<std::mutex> lock(gThemes.mutex);
    auto [entry, inserted] = gThemes.states.try_emplace(context);
    auto& state = entry->second;
    if (inserted) {
        state.palette = gDefaults.palette;
        state.themeKey = gDefaults.themeKey;
        state.locale = gDefaults.locale;
        state.density = gDefaults.density;
        state.metrics = gDefaults.metrics;
        state.squareWindows = gDefaults.squareWindows;
        state.dpi = gDefaults.dpi;
    }
    if (!state.hooked)
        state.hooked = hookShutdown(context, releaseTheme);
    // Not hooked yet: no caching, so the next call tries again.
    tCachedContext = state.hooked ? context : nullptr;
    tCached = &state;
    tCachedGeneration = gThemes.generation.load();
    if (inserted)
        applyLocale(state);
    return state;
}

// A theme change: the current context's or, with no context current, the
// defaults and every live context (their style too; no other thread may be
// drawing with them meanwhile).
template <class F>
void change(F&& apply)
{
    if (ImGui::GetCurrentContext() != nullptr) {
        apply(themeState());
        return;
    }
    std::vector<ImGuiContext*> contexts;
    {
        std::lock_guard<std::mutex> lock(gThemes.mutex);
        apply(gDefaults);
        for (const auto& entry : gThemes.states)
            contexts.push_back(entry.first);
    }
    for (auto* context : contexts) {
        ImGui::SetCurrentContext(context);
        apply(themeState());
    }
    ImGui::SetCurrentContext(nullptr);
}

struct LocaleRegistry {
    std::mutex mutex;
    std::unordered_map<std::string, TranslationCatalog> catalogs{{"en", {}}, {"fr", {
#define CYUI_TRANSLATION(key, value) {key, value},
#include "cyui/detail/LocaleFrench.inc"
#undef CYUI_TRANSLATION
    }}};
    std::unordered_map<std::string, std::string> names{{"en", "English"}, {"fr", "Français"}};
};
LocaleRegistry& localeRegistry() { static LocaleRegistry registry; return registry; }
std::string translateForLocale(const std::string& language, const char* key, const char* englishFallback = nullptr)
{
    auto& registry = localeRegistry();
    std::lock_guard<std::mutex> lock(registry.mutex);
    const auto catalog = registry.catalogs.find(language);
    if (catalog != registry.catalogs.end()) {
        const auto item = catalog->second.find(key);
        if (item != catalog->second.end()) return item->second;
    }
    const auto base = registry.catalogs.find(language.substr(0, language.find('-')));
    if (base != registry.catalogs.end()) {
        const auto item = base->second.find(key);
        if (item != base->second.end()) return item->second;
    }
    const auto& english = registry.catalogs.at("en");
    const auto fallback = english.find(key);
    return fallback != english.end() ? fallback->second : englishFallback ? englishFallback : key;
}
void applyLocale(ThemeState& state)
{
    const std::pair<ImGuiLocKey, const char*> entries[]{
        {ImGuiLocKey_TableSizeOne, "Size column to fit###SizeOne"},
        {ImGuiLocKey_TableSizeAllFit, "Size all columns to fit###SizeAll"},
        {ImGuiLocKey_TableSizeAllDefault, "Size all columns to default###SizeAll"},
        {ImGuiLocKey_TableReset, "Reset"},
        {ImGuiLocKey_TableResetOrder, "Reset order###ResetOrder"},
        {ImGuiLocKey_TableResetVisibility, "Reset visibility###ResetVisibility"}};
    state.localizedImGui.clear();
    for (const auto& entry : entries) {
        const std::string key(entry.second);
        const auto marker = key.find("###");
        const auto visible = key.substr(0, marker);
        state.localizedImGui.push_back(translateForLocale(state.locale, visible.c_str()) + "###" +
                                      (marker == std::string::npos ? key : key.substr(marker + 3)));
    }
    for (std::size_t i = 0; i < state.localizedImGui.size(); ++i) {
        ImGuiLocEntry entry{entries[i].first, state.localizedImGui[i].c_str()};
        ImGui::LocalizeRegisterEntries(&entry, 1);
    }
}
std::string normalizeLocale(const char* code)
{
    std::string result;
    if (!code) return result;
    for (const unsigned char c : std::string(code)) {
        if (result.size() >= 35 || !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                                    (c >= '0' && c <= '9') || c == '-' || c == '_')) return {};
        result += c == '_' ? '-' : char(std::tolower(c));
    }
    return result;
}

float luminance(const ImU32 colour)
{
    return 0.2126f * static_cast<float>(colour & 0xffu) / 255.0f + 0.7152f * static_cast<float>((colour >> 8) & 0xffu) / 255.0f
           + 0.0722f * static_cast<float>((colour >> 16) & 0xffu) / 255.0f;
}

void applyColours(ImGuiStyle& style, const ThemeState& state)
{
    const auto& p = state.palette;
    // Dimming behind modals: the darkest of text and deepest ground.
    const auto shade = luminance(p.text) < luminance(p.bg0) ? p.text : p.bg0;
    const auto light = luminance(p.bg1) > 0.5f;
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
    c[ImGuiCol_NavWindowingDimBg] = vec(alpha(shade, 0.35f));
    c[ImGuiCol_ModalWindowDimBg] = vec(alpha(shade, light ? 0.25f : 0.45f));
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
    if (state.squareWindows)
        c[ImGuiCol_WindowBg].w = 1.0f;
}

void applyStyle(ThemeState& state)
{
    auto& style = ImGui::GetStyle();
    style = ImGuiStyle();
    const auto& m = state.metrics;
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
    applyColours(style, state);

    style.ScaleAllSizes(state.dpi);
    if (state.squareWindows)
        style.WindowRounding = 0.0f;
    style.FontScaleDpi = state.dpi;
    state.styled = true;
    state.styledDpi = state.dpi;
    state.styledDensity = state.density;
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

struct FontSet {
    ImFont* fonts[3] {};
    bool icons = false;
};

// What loadFonts put in the current atlas: in this context, or in another
// sharing the atlas. Empty when unknown or the atlas was cleared since.
FontSet fontSet()
{
    if (ImGui::GetCurrentContext() == nullptr)
        return {};
    auto* atlas = ImGui::GetIO().Fonts;
    const auto owns = [atlas](const ThemeState& state) {
        return state.atlas == atlas && state.fonts[0] != nullptr && atlas->Fonts.contains(state.fonts[0]);
    };
    const auto set = [](const ThemeState& state) { return FontSet { { state.fonts[0], state.fonts[1], state.fonts[2] }, state.icons }; };
    if (const auto& own = themeState(); owns(own))
        return set(own);
    std::lock_guard<std::mutex> lock(gThemes.mutex);
    for (const auto& entry : gThemes.states)
        if (owns(entry.second))
            return set(entry.second);
    return {};
}

ImFont* fontAt(const int index)
{
    auto* font = fontSet().fonts[index];
    return font != nullptr && ImGui::GetIO().Fonts->Fonts.contains(font) ? font : nullptr;
}

ImFont* addFont(const std::vector<std::string>& candidates, const float size)
{
    auto& io = ImGui::GetIO();
    ImFontConfig config;
    config.OversampleH = 2;
    config.OversampleV = 2;
    for (const auto& path : candidates)
        if (fileExists(path))
            if (auto* font = io.Fonts->AddFontFromFileTTF(path.c_str(), size, &config))
                return font;
    return io.Fonts->AddFontDefault();
}

void mergeIcons(ThemeState& state, const float size, const bool wanted)
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
        state.icons = true;
}

}  // namespace

// ---- Palette -----------------------------------------------------------------

bool setLocale(const char* code)
{
    auto wanted = normalizeLocale(code);
    auto& registry = localeRegistry();
    {
        std::lock_guard<std::mutex> lock(registry.mutex);
        if (!registry.catalogs.count(wanted)) wanted = wanted.substr(0, wanted.find('-'));
        if (!registry.catalogs.count(wanted)) return false;
    }
    change([&](ThemeState& state) {
        state.locale = wanted;
        if (!ImGui::GetCurrentContext()) return;
        applyLocale(state);
    });
    return true;
}
const std::string& locale() { return themeState().locale; }
std::vector<LocaleInfo> availableLocales()
{
    auto& registry = localeRegistry();
    std::lock_guard<std::mutex> lock(registry.mutex);
    std::vector<LocaleInfo> result{{"en", registry.names.at("en")}, {"fr", registry.names.at("fr")}};
    for (const auto& item : registry.names)
        if (item.first != "en" && item.first != "fr") result.push_back({item.first, item.second});
    std::sort(result.begin() + 2, result.end(), [](const LocaleInfo& a, const LocaleInfo& b) { return a.code < b.code; });
    return result;
}
bool registerTranslations(const char* code, const TranslationCatalog& translations, const char* name)
{
    const auto normalized = normalizeLocale(code);
    if (normalized.empty()) return false;
    auto& registry = localeRegistry();
    std::lock_guard<std::mutex> lock(registry.mutex);
    auto& catalog = registry.catalogs[normalized];
    for (const auto& item : translations) catalog[item.first] = item.second;
    if (name) registry.names[normalized] = name;
    else if (!registry.names.count(normalized)) registry.names[normalized] = normalized;
    return true;
}
std::string tr(const char* key, const char* englishFallback)
{
    if (!key) return {};
    return translateForLocale(locale(), key, englishFallback);
}
std::string trLabel(const char* key, const char* englishFallback)
{
    if (!key) return {};
    const std::string label(key);
    const auto marker = label.find("##");
    const auto visible = label.substr(0, marker);
    const auto explicitId = label.find("###");
    return tr(visible.c_str(), englishFallback) + "###" +
           (explicitId == std::string::npos ? label : label.substr(explicitId + 3));
}

const Palette& palette()
{
    return themeState().palette;
}

namespace {
void usePalette(ThemeState& state, const Palette& value)
{
    state.palette = value;
    if (ImGui::GetCurrentContext() != nullptr)
        applyColours(ImGui::GetStyle(), state);
}

bool useTheme(ThemeState& state, const char* key)
{
    for (const auto& theme : builtInThemes())
        if (key != nullptr && theme.key == key) {
            state.themeKey = theme.key;
            usePalette(state, theme.palette);
            return true;
        }
    return false;
}

void useDensity(ThemeState& state, const Density value)
{
    state.density = value;
    state.metrics = value == Density::compact ? kCompact : kComfortable;
}
}  // namespace

void setPalette(const Palette& value)
{
    change([&](ThemeState& state) { usePalette(state, value); });
}

const std::vector<Theme>& builtInThemes()
{
    static const std::vector<Theme> themes {
        { "mid-grey", "Mid grey", kMidGrey, false },
        { "light-grey", "Light grey", kLightGrey, true },
        { "amber-night", "Amber night", kAmberNight, false },
        { "violet", "Violet", kViolet, false },
        { "navy", "Navy", kNavy, false },
    };
    return themes;
}

bool setTheme(const char* key)
{
    const auto known = std::any_of(builtInThemes().begin(), builtInThemes().end(),
                                   [key](const Theme& theme) { return key != nullptr && theme.key == key; });
    if (known)
        change([key](ThemeState& state) { useTheme(state, key); });
    return known;
}

const std::string& currentThemeKey()
{
    return themeState().themeKey;
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
    // Twelve hues spread by the golden ratio from the accent's; saturation and
    // brightness follow the grounds, so the set reads on light and dark themes.
    const auto& p = palette();
    const auto accent = vec(p.accent);
    auto h = 0.0f, s = 0.0f, v = 0.0f;
    ImGui::ColorConvertRGBtoHSV(accent.x, accent.y, accent.z, h, s, v);
    const auto n = ((index % 12) + 12) % 12;
    const auto light = luminance(p.bg1) > 0.5f;
    const auto hue = std::fmod(h + static_cast<float>(n) * 0.618034f, 1.0f);
    const auto odd = (n & 1) != 0 ? 1.0f : 0.0f;
    auto r = 0.0f, g = 0.0f, b = 0.0f;
    ImGui::ColorConvertHSVtoRGB(hue, (light ? 0.72f : 0.52f) + 0.08f * odd, (light ? 0.66f : 0.92f) - 0.08f * odd, r, g, b);
    return ImGui::ColorConvertFloat4ToU32(ImVec4(r, g, b, 1.0f));
}

// ---- Metrics -----------------------------------------------------------------

const Metrics& metrics()
{
    return themeState().metrics;
}

void setDensity(const Density value)
{
    change([value](ThemeState& state) {
        useDensity(state, value);
        if (ImGui::GetCurrentContext() != nullptr && (!state.styled || state.styledDensity != value))
            applyStyle(state);
    });
}

Density density()
{
    return themeState().density;
}

// ---- Applying ----------------------------------------------------------------

void apply(const Config& config)
{
    const auto set = [&config](ThemeState& state) {
        state.squareWindows = config.squareWindows;
        state.dpi = std::max(0.5f, config.dpiScale);
        useDensity(state, config.density);
        if (!useTheme(state, config.themeKey))
            useTheme(state, defaultThemeKey);
        if (ImGui::GetCurrentContext() != nullptr)
            applyStyle(state);
    };
    // With no context current: the defaults only (DPI is each window's own).
    if (ImGui::GetCurrentContext() != nullptr) {
        set(themeState());
    } else {
        std::lock_guard<std::mutex> lock(gThemes.mutex);
        set(gDefaults);
    }
}

void applyDpi(const float dpiScale)
{
    if (ImGui::GetCurrentContext() == nullptr) {
        std::lock_guard<std::mutex> lock(gThemes.mutex);
        gDefaults.dpi = std::max(0.5f, dpiScale);
        return;
    }
    auto& state = themeState();
    state.dpi = std::max(0.5f, dpiScale);
    // Rebuilding resets ImGui's style: only when something changed.
    if (!state.styled || state.styledDpi != state.dpi || state.styledDensity != state.density)
        applyStyle(state);
}

float dpi()
{
    return themeState().dpi;
}

// ---- Fonts -------------------------------------------------------------------

void loadFonts(const FontConfig& config)
{
    auto& io = ImGui::GetIO();
    auto& state = themeState();
    io.Fonts->Clear();
    state.icons = false;
    state.atlas = io.Fonts;
    const auto size = config.size > 0.0f ? config.size : state.metrics.fontSize;
    const auto* folder = config.fallbackFolder;
    state.fonts[0] = addFont({ windowsFont("segoeui.ttf"), inFolder(folder, "Inter-Regular.ttf"),
                               inFolder(folder, "Roboto-Regular.ttf"), inFolder(folder, "Roboto-Medium.ttf") },
                             size);
    mergeIcons(state, size, config.icons);
    state.fonts[1] = addFont({ windowsFont("seguisb.ttf"), inFolder(folder, "Inter-SemiBold.ttf"),
                               inFolder(folder, "Roboto-Bold.ttf"), inFolder(folder, "Roboto-Medium.ttf") },
                             size);
    mergeIcons(state, size, config.icons);
    state.fonts[2] = addFont({ windowsFont("consola.ttf"), inFolder(folder, "JetBrainsMono-Regular.ttf"),
                               inFolder(folder, "Cousine-Regular.ttf") },
                             size - 1.0f);
    io.FontDefault = state.fonts[0];
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
    return fontSet().icons;
}

}  // namespace cyui
