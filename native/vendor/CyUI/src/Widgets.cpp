#include "cyui/CoreWidgets.h"
#include "cyui/Locale.h"

#include "cyui/Icons.h"

#include <imgui_internal.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <atomic>
#include <cstring>
#include <mutex>
#include <optional>
#include <string>
#include <vector>
#include <unordered_map>

namespace cyui {
namespace {

constexpr float kPi = 3.14159265358979f;

float luminance(const ImU32 colour)
{
    return 0.2126f * static_cast<float>(colour & 0xffu) / 255.0f + 0.7152f * static_cast<float>((colour >> 8) & 0xffu) / 255.0f
           + 0.0722f * static_cast<float>((colour >> 16) & 0xffu) / 255.0f;
}

// The palette's text, deepest or most raised ground: whichever reads best on `ground`.
ImU32 contrastText(const ImU32 ground)
{
    const auto& p = palette();
    const auto level = luminance(ground);
    auto best = p.text;
    for (const auto candidate : { p.bg0, p.bg5 })
        if (std::abs(luminance(candidate) - level) > std::abs(luminance(best) - level))
            best = candidate;
    return best;
}

// Hand-drawn colours fade like ImGui's own (style.Alpha: BeginDisabled, fades).
ImU32 faded(const ImU32 colour)
{
    return ImGui::GetColorU32(colour);
}

// A one-pixel line at 96 dpi, whole pixels above (as ImGui scales borders).
float hairline()
{
    return std::max(1.0f, std::floor(dp(1.0f)));
}

// Whether the current font draws the first character of `text` (a host may
// merge its own icon font, so iconsAvailable() alone does not tell).
bool fontHasGlyph(const char* text)
{
    unsigned int c = 0;
    ImTextCharFromUtf8(&c, text, nullptr);
    return c != 0 && c <= IM_UNICODE_CODEPOINT_MAX && ImGui::GetFont()->IsGlyphInFont(static_cast<ImWchar>(c));
}

void arc(ImDrawList* list, const ImVec2 centre, const float radius, const float from, const float to, const ImU32 colour,
         const float thickness)
{
    if (std::abs(to - from) < 0.001f)
        return;
    list->PathClear();
    list->PathArcTo(centre, radius, std::min(from, to), std::max(from, to), 32);
    list->PathStroke(colour, 0, thickness);
}

void textV(const ImU32 colour, const char* fmt, va_list args)
{
    ImGui::TextColoredV(vec(colour), fmt, args);
}

// Vector glyphs the widgets need, so they never depend on the icon font.
void drawMagnifier(ImDrawList* list, const ImVec2 centre, const float size, const ImU32 colour)
{
    const auto r = size * 0.32f;
    const ImVec2 c(centre.x - size * 0.08f, centre.y - size * 0.08f);
    list->AddCircle(c, r, colour, 16, std::max(dp(1.2f), size * 0.1f));
    list->AddLine(ImVec2(c.x + r * 0.7f, c.y + r * 0.7f), ImVec2(centre.x + size * 0.42f, centre.y + size * 0.42f), colour,
                  std::max(dp(1.4f), size * 0.12f));
}

void drawCross(ImDrawList* list, const ImVec2 centre, const float size, const ImU32 colour)
{
    const auto h = size * 0.5f;
    const auto t = std::max(dp(1.2f), size * 0.12f);
    list->AddLine(ImVec2(centre.x - h, centre.y - h), ImVec2(centre.x + h, centre.y + h), colour, t);
    list->AddLine(ImVec2(centre.x - h, centre.y + h), ImVec2(centre.x + h, centre.y - h), colour, t);
}

struct ButtonColours {
    ImU32 ground, hovered, active, text, border;
};

ButtonColours variantColours(const Variant variant)
{
    const auto& p = palette();
    switch (variant) {
    case Variant::primary:
        return { p.accent, p.accentHover, brighter(p.accent, 0.2f), p.accentText, 0 };
    case Variant::ghost:
        return { alpha(p.bg3, 0.0f), p.bg4, p.bg5, p.text, 0 };
    case Variant::outline:
        return { alpha(p.bg3, 0.0f), alpha(p.accent, 0.12f), alpha(p.accent, 0.22f), p.text, p.lineStrong };
    case Variant::danger: {
        const auto ground = mix(p.error, p.bg1, 0.35f);
        return { ground, p.error, brighter(p.error, 0.15f), contrastText(ground), 0 };
    }
    case Variant::success: {
        const auto ground = mix(p.success, p.bg1, 0.25f);
        return { ground, p.success, brighter(p.success, 0.15f), contrastText(ground), 0 };
    }
    case Variant::secondary:
    default:
        return { p.bg3, p.bg4, p.bg5, p.text, 0 };
    }
}

}  // namespace

// ---- Tones -------------------------------------------------------------------

ImU32 toneColour(const Tone tone)
{
    const auto& p = palette();
    switch (tone) {
    case Tone::accent: return p.accent;
    case Tone::success: return p.success;
    case Tone::warning: return p.warning;
    case Tone::error: return p.error;
    case Tone::info: return p.info;
    case Tone::neutral:
    default: return p.textDim;
    }
}

// ---- Text --------------------------------------------------------------------

void heading(const char* text, const int level)
{
    const auto factor = level <= 1 ? 1.5f : level == 2 ? 1.2f : 1.0f;
    ScopedFont font(boldFont(), factor);
    ScopedColor colour(ImGuiCol_Text, palette().text);
    ImGui::TextUnformatted(text);
}

void sectionTitle(const char* text)
{
    ScopedFont font(boldFont(), 0.85f);
    ScopedColor colour(ImGuiCol_Text, palette().textDim);
    ImGui::TextUnformatted(text);
}

void overline(const char* text, const ImU32 colour)
{
    const auto& p = palette();
    ScopedFont font(boldFont(), 0.78f);
    ScopedColor tint(ImGuiCol_Text, colour != 0 ? colour : mix(p.accent, p.text, 0.35f));
    ImGui::TextUnformatted(text);
}

void textDim(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    textV(palette().textDim, fmt, args);
    va_end(args);
}

void textFaint(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    textV(palette().textFaint, fmt, args);
    va_end(args);
}

void textColoured(const ImU32 colour, const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    textV(colour, fmt, args);
    va_end(args);
}

void textMono(const char* fmt, ...)
{
    ImGui::PushFont(monoFont(), 0.0f);
    va_list args;
    va_start(args, fmt);
    ImGui::TextV(fmt, args);
    va_end(args);
    ImGui::PopFont();
}

void kbd(const char* keys)
{
    const auto& p = palette();
    ScopedFont font(nullptr, 0.82f);
    const auto size = ImGui::CalcTextSize(keys);
    const auto pad = dp(5.0f, 1.0f);
    const auto origin = ImGui::GetCursorScreenPos();
    const ImVec2 box(size.x + pad.x * 2.0f, size.y + pad.y * 2.0f);
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = dp(3.0f);
    list->AddRectFilled(ImVec2(origin.x, origin.y + dp(1.0f)), ImVec2(origin.x + box.x, origin.y + box.y + dp(1.0f)),
                        faded(p.lineStrong), rounding);
    list->AddRectFilled(origin, ImVec2(origin.x + box.x, origin.y + box.y), faded(p.bg3), rounding);
    list->AddRect(origin, ImVec2(origin.x + box.x, origin.y + box.y), faded(p.line), rounding, 0, hairline());
    list->AddText(ImVec2(origin.x + pad.x, origin.y + pad.y), faded(p.textDim), keys);
    ImGui::Dummy(ImVec2(box.x, box.y + dp(1.0f)));
}

void tooltip(const char* text)
{
    const auto translated = tr(text);
    text = translated.c_str();
    if (text == nullptr || text[0] == 0)
        return;
    if (!ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort | ImGuiHoveredFlags_NoSharedDelay))
        return;
    if (ImGui::BeginTooltip()) {
        ImGui::PushTextWrapPos(ImGui::GetFontSize() * 32.0f);
        ImGui::TextUnformatted(text);
        ImGui::PopTextWrapPos();
        ImGui::EndTooltip();
    }
}

void helpMarker(const char* text)
{
    ImGui::SameLine(0.0f, dp(4.0f));
    ImGui::TextDisabled("%s", iconsAvailable() ? icon::info : "(?)");
    if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayNone) && ImGui::BeginTooltip()) {
        ImGui::PushTextWrapPos(ImGui::GetFontSize() * 32.0f);
        ImGui::TextUnformatted(text);
        ImGui::PopTextWrapPos();
        ImGui::EndTooltip();
    }
}

void clippedText(ImDrawList* list, const ImVec2 position, const float maxWidth, const ImU32 colour, const char* text)
{
    if (text == nullptr || maxWidth <= dp(4.0f))
        return;
    auto* font = ImGui::GetFont();
    const auto size = ImGui::GetFontSize();
    const auto* end = text + std::strlen(text);
    if (font->CalcTextSizeA(size, FLT_MAX, 0.0f, text, end).x <= maxWidth) {
        list->AddText(position, faded(colour), text, end);
        return;
    }
    // One pass: what fits before the ellipsis (whole UTF-8 characters).
    static constexpr const char* ellipsis = "...";
    const char* cut = text;
    const auto width = font->CalcTextSizeA(size, std::max(0.0f, maxWidth - font->CalcTextSizeA(size, FLT_MAX, 0.0f, ellipsis).x),
                                           0.0f, text, end, &cut).x;
    list->AddText(position, faded(colour), text, cut);
    list->AddText(ImVec2(position.x + width, position.y), faded(colour), ellipsis);
}

void centredText(ImDrawList* list, const ImVec2 min, const ImVec2 max, const ImU32 colour, const char* text)
{
    const auto size = ImGui::CalcTextSize(text);
    list->AddText(ImVec2(min.x + (max.x - min.x - size.x) * 0.5f, min.y + (max.y - min.y - size.y) * 0.5f), faded(colour), text);
}

// ---- Buttons -----------------------------------------------------------------

bool button(const char* label, const Variant variant, const ImVec2 size)
{
    const auto c = variantColours(variant);
    ScopedColor colours(ImGuiCol_Button, c.ground);
    colours.push(ImGuiCol_ButtonHovered, c.hovered).push(ImGuiCol_ButtonActive, c.active).push(ImGuiCol_Text, c.text);
    if (c.border != 0) {
        colours.push(ImGuiCol_Border, c.border);
        ScopedVar border(ImGuiStyleVar_FrameBorderSize, hairline());
        return ImGui::Button(label, size);
    }
    return ImGui::Button(label, size);
}

bool primaryButton(const char* label, const ImVec2 size)
{
    return button(label, Variant::primary, size);
}

bool dangerButton(const char* label, const ImVec2 size)
{
    return button(label, Variant::danger, size);
}

bool ghostButton(const char* label, const ImVec2 size)
{
    return button(label, Variant::ghost, size);
}

bool iconTextButton(const char* icon, const char* label, const Variant variant, const ImVec2 size)
{
    std::string text;
    if (iconsAvailable() && icon != nullptr) {
        text = icon;
        text += "  ";
    }
    text += label;
    return button(text.c_str(), variant, size);
}

bool iconButton(const char* icon, const char* tip, const bool active, const ImU32 activeColour, ImVec2 size)
{
    const auto& p = palette();
    if (size.x <= 0.0f)
        size.x = ImGui::GetFrameHeight() + dp(2.0f);
    if (size.y <= 0.0f)
        size.y = ImGui::GetFrameHeight();
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::PushID(icon);
    ImGui::PushID(tip != nullptr ? tip : "");
    const auto clicked = ImGui::InvisibleButton("##icon", size);
    const auto hovered = ImGui::IsItemHovered();
    const auto held = ImGui::IsItemActive();
    ImGui::PopID();
    ImGui::PopID();
    const auto translatedTip = tr(tip);
    tip = translatedTip.c_str();
    auto* list = ImGui::GetWindowDrawList();
    const auto colour = activeColour != 0 ? activeColour : p.accent;
    const auto rounding = ImGui::GetStyle().FrameRounding;
    const ImVec2 max(origin.x + size.x, origin.y + size.y);
    if (active)
        list->AddRectFilled(origin, max, faded(alpha(colour, held ? 0.28f : 0.18f)), rounding);
    else if (hovered)
        list->AddRectFilled(origin, max, faded(held ? p.bg5 : p.bg4), rounding);
    const auto ink = active ? colour : (hovered ? p.text : p.textDim);
    if (icon == nullptr || fontHasGlyph(icon)) {
        centredText(list, origin, max, ink, icon);
    } else if (tip != nullptr && tip[0] != 0) {
        // No icon font: the tooltip's first letter stands in for the glyph.
        unsigned int letter = 0;
        const auto bytes = ImTextCharFromUtf8(&letter, tip, nullptr);
        const std::string initial(tip, static_cast<std::size_t>(bytes));
        centredText(list, origin, max, ink, initial.c_str());
    } else {
        list->AddCircleFilled(ImVec2((origin.x + max.x) * 0.5f, (origin.y + max.y) * 0.5f), size.y * 0.12f, faded(ink), 12);
    }
    tooltip(tip);
    return clicked;
}

bool toggleButton(const char* label, bool* value, ImU32 onColour, ImVec2 size, const char* tip)
{
    const auto& p = palette();
    if (onColour == 0)
        onColour = p.accent;
    const auto textSize = ImGui::CalcTextSize(label, nullptr, true);
    if (size.x <= 0.0f)
        size.x = textSize.x + ImGui::GetStyle().FramePadding.x * 2.0f;
    if (size.y <= 0.0f)
        size.y = ImGui::GetFrameHeight();
    const auto origin = ImGui::GetCursorScreenPos();
    const auto clicked = ImGui::InvisibleButton(label, size);
    if (clicked) {
        *value = !*value;
        markEdited(ImGui::GetItemID());
    }
    const auto hovered = ImGui::IsItemHovered();
    auto* list = ImGui::GetWindowDrawList();
    const auto on = *value;
    const auto fill = on ? (hovered ? brighter(onColour, 0.1f) : onColour) : (hovered ? p.bg4 : p.bg3);
    list->AddRectFilled(origin, ImVec2(origin.x + size.x, origin.y + size.y), faded(fill), ImGui::GetStyle().FrameRounding);
    const auto textColour = on ? contrastText(onColour) : (hovered ? p.text : p.textDim);
    list->AddText(ImVec2(origin.x + (size.x - textSize.x) * 0.5f, origin.y + (size.y - textSize.y) * 0.5f), faded(textColour),
                  label, ImGui::FindRenderedTextEnd(label));
    tooltip(tip);
    return clicked;
}

bool toggleSwitch(const char* label, bool* value)
{
    const auto& p = palette();
    const auto& style = ImGui::GetStyle();
    const auto height = ImGui::GetFrameHeight();
    const auto trackHeight = std::round(height * 0.72f);
    const auto trackWidth = std::round(trackHeight * 1.8f);
    const auto* end = ImGui::FindRenderedTextEnd(label);
    const auto hasText = end != label;
    const auto textSize = ImGui::CalcTextSize(label, end);
    const auto origin = ImGui::GetCursorScreenPos();
    const ImVec2 size(trackWidth + (hasText ? style.ItemInnerSpacing.x + textSize.x : 0.0f), height);
    const auto clicked = ImGui::InvisibleButton(label, size);
    if (clicked) {
        *value = !*value;
        markEdited(ImGui::GetItemID());
    }
    const auto hovered = ImGui::IsItemHovered();

    // The knob slides over a few frames.
    auto* storage = ImGui::GetStateStorage();
    const auto key = ImGui::GetItemID();
    const auto target = *value ? 1.0f : 0.0f;
    auto t = storage->GetFloat(key, target);
    const auto step = ImGui::GetIO().DeltaTime * 12.0f;
    t = t < target ? std::min(target, t + step) : std::max(target, t - step);
    storage->SetFloat(key, t);

    auto* list = ImGui::GetWindowDrawList();
    const ImVec2 trackMin(origin.x, origin.y + (height - trackHeight) * 0.5f);
    const ImVec2 trackMax(trackMin.x + trackWidth, trackMin.y + trackHeight);
    const auto off = hovered ? p.bg5 : p.bg4;
    const auto on = hovered ? p.accentHover : p.accent;
    list->AddRectFilled(trackMin, trackMax, faded(mix(off, on, t)), trackHeight * 0.5f);
    const auto radius = trackHeight * 0.5f - dp(2.5f);
    const ImVec2 knobCentre(trackMin.x + trackHeight * 0.5f + (trackWidth - trackHeight) * t, trackMin.y + trackHeight * 0.5f);
    // Flat: the knob takes whichever palette colour reads on its track.
    list->AddCircleFilled(knobCentre, radius, faded(mix(contrastText(p.bg4), contrastText(p.accent), t)), 20);
    if (hasText)
        list->AddText(ImVec2(trackMax.x + style.ItemInnerSpacing.x, origin.y + (height - textSize.y) * 0.5f), faded(p.text),
                      label, end);
    return clicked;
}

bool segmented(const char* id, const char* const labels[], const int count, int* current, const float width)
{
    std::vector<std::string> translated;
    std::vector<const char*> translatedLabels;
    translated.reserve(std::size_t(std::max(0, count)));
    translatedLabels.reserve(std::size_t(std::max(0, count)));
    for (int i = 0; i < count; ++i) translated.push_back(tr(labels[i]));
    for (const auto& label : translated) translatedLabels.push_back(label.c_str());
    labels = translatedLabels.data();
    const auto& p = palette();
    const auto& style = ImGui::GetStyle();
    ImGui::PushID(id);
    const auto height = ImGui::GetFrameHeight();
    const auto inset = dp(2.0f);
    std::vector<float> widths(static_cast<std::size_t>(std::max(0, count)));
    auto natural = 0.0f;
    for (auto i = 0; i < count; ++i) {
        widths[static_cast<std::size_t>(i)] = ImGui::CalcTextSize(labels[i], nullptr, true).x + style.FramePadding.x * 2.6f;
        natural += widths[static_cast<std::size_t>(i)];
    }
    const auto total = width > 0.0f ? width : width < 0.0f ? ImGui::GetContentRegionAvail().x : natural + inset * 2.0f;
    const auto scale = natural > 0.0f ? (total - inset * 2.0f) / natural : 1.0f;
    const auto origin = ImGui::GetCursorScreenPos();
    auto* list = ImGui::GetWindowDrawList();
    list->AddRectFilled(origin, ImVec2(origin.x + total, origin.y + height), faded(p.bg0), style.FrameRounding + inset);
    // One group, so IsItemHovered / IsItemEdited / IsItemDeactivatedAfterEdit
    // after segmented() speak for the whole control.
    ImGui::BeginGroup();
    auto changed = false;
    auto x = origin.x + inset;
    for (auto i = 0; i < count; ++i) {
        const auto w = widths[static_cast<std::size_t>(i)] * scale;
        ImGui::SetCursorScreenPos(ImVec2(x, origin.y + inset));
        ImGui::PushID(i);
        const ImVec2 size(w, height - inset * 2.0f);
        if (ImGui::InvisibleButton("##segment", size) && *current != i) {
            *current = i;
            changed = true;
            markEdited(ImGui::GetItemID());
        }
        const auto hovered = ImGui::IsItemHovered();
        ImGui::PopID();
        const auto selected = *current == i;
        const ImVec2 min(x, origin.y + inset);
        const ImVec2 max(x + w, origin.y + height - inset);
        if (selected)
            list->AddRectFilled(min, max, faded(p.accentDim), style.FrameRounding);
        else if (hovered)
            list->AddRectFilled(min, max, faded(p.bg3), style.FrameRounding);
        const auto* end = ImGui::FindRenderedTextEnd(labels[i]);
        const auto textSize = ImGui::CalcTextSize(labels[i], end);
        list->AddText(ImVec2(min.x + (w - textSize.x) * 0.5f, min.y + (size.y - textSize.y) * 0.5f),
                      faded(selected ? contrastText(p.accentDim) : (hovered ? p.text : p.textDim)), labels[i], end);
        x += w;
    }
    ImGui::SetCursorScreenPos(origin);
    ImGui::Dummy(ImVec2(total, height));
    ImGui::EndGroup();
    ImGui::PopID();
    return changed;
}

bool linkText(const char* label)
{
    return ImGui::TextLink(label);
}

// ---- Value controls ------------------------------------------------------------

namespace {
constexpr int kMaxToasts = 6;
struct Toast {
    std::string text;
    Tone tone;
    double until;
    int slot;  // its window, ##cyuiToast0..5, kept for the toast's life
};
struct PendingToast {
    std::string text;
    Tone tone;
    float seconds;
};
struct WidgetContextState {
    int wheelFrame = -1;
    ImGuiID entryId {};
    char entryText[64] {};
    bool entryFresh = false;
    ImGuiID pressId {};  // sliderBar: the value before the press, undone by a double click
    float pressValue {};
    std::vector<Toast> toasts;
    std::vector<PendingToast> posted;  // notify(), any thread: taken by renderNotifications
    bool hooked = false;               // released with its context
};
// One per ImGui context, released with it. Contexts may live on different
// threads: the map is locked, each thread caches its last lookup.
bool gWidgetsAlive = false;  // false again once static destruction removed the map
struct WidgetRegistry {
    std::mutex mutex;
    std::unordered_map<ImGuiContext*, WidgetContextState> states;
    std::atomic<unsigned> generation { 0 };
    WidgetRegistry() { gWidgetsAlive = true; }
    ~WidgetRegistry() { gWidgetsAlive = false; }
};
WidgetRegistry gWidgets;
WidgetContextState gNoContext;
thread_local ImGuiContext* tWidgetContext {};
thread_local WidgetContextState* tWidgetState {};
thread_local unsigned tWidgetGeneration {};

void releaseWidgets(ImGuiContext* closing, ImGuiContextHook*)
{
    if (!gWidgetsAlive)
        return;
    std::lock_guard<std::mutex> lock(gWidgets.mutex);
    gWidgets.states.erase(closing);
    ++gWidgets.generation;
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

WidgetContextState& widgetState()
{
    auto* context = ImGui::GetCurrentContext();
    if (context == nullptr)
        return gNoContext;
    if (context == tWidgetContext && tWidgetGeneration == gWidgets.generation.load())
        return *tWidgetState;
    std::lock_guard<std::mutex> lock(gWidgets.mutex);
    auto& state = gWidgets.states[context];
    if (!state.hooked)
        state.hooked = hookShutdown(context, releaseWidgets);
    tWidgetContext = state.hooked ? context : nullptr;
    tWidgetState = &state;
    tWidgetGeneration = gWidgets.generation.load();
    return state;
}

std::optional<double> typedNumber(std::string text)
{
    std::replace(text.begin(), text.end(), ',', '.');
    const auto* start = text.c_str();
    while (*start == ' ')
        ++start;
    char* end = nullptr;
    const auto number = std::strtod(start, &end);
    if (end == start || !std::isfinite(number))
        return std::nullopt;
    while (end != nullptr && *end == ' ')
        ++end;
    if (end != nullptr && (*end == 'k' || *end == 'K'))
        return number * 1000.0;
    return number;
}
}  // namespace

void takeWheel()
{
    widgetState().wheelFrame = ImGui::GetFrameCount();
    ImGui::SetItemKeyOwner(ImGuiKey_MouseWheelY);
}

bool wheelTaken()
{
    return widgetState().wheelFrame == ImGui::GetFrameCount();
}

void markEdited(const ImGuiID id)
{
    const auto& g = *GImGui;
    if (g.ActiveId == 0 || g.ActiveId == id || g.ActiveIdPreviousFrame == id || g.DeactivatedItemData.ID == id)
        ImGui::MarkItemEdited(id);
}

float itemWheel()
{
    // ImGui gives the wheel to a window at the start of a frame: claim it on
    // every hovered frame so the window under the item never scrolls as well,
    // and use only a wheel the item owned since the frame before.
    if (!ImGui::IsItemHovered())
        return 0.0f;
    const auto& g = *GImGui;
    const auto id = ImGui::GetItemID();
    const auto owned = id != 0 && ImGui::TestKeyOwner(ImGuiKey_MouseWheelY, id) && g.WheelingWindowScrolledFrame != g.FrameCount
                       && !wheelTaken();
    ImGui::SetItemKeyOwner(ImGuiKey_MouseWheelY);
    const auto wheel = owned ? g.IO.MouseWheel : 0.0f;
    if (wheel != 0.0f)
        widgetState().wheelFrame = g.FrameCount;
    return wheel;
}

float valueSpeed()
{
    const auto& io = ImGui::GetIO();
    return io.KeyCtrl ? 0.15f : io.KeyShift ? 4.0f : 1.0f;
}

bool valueEntryOpen()
{
    return widgetState().entryId != 0 && ImGui::IsPopupOpen(widgetState().entryId, ImGuiPopupFlags_None);
}

bool valueEntry(const ImGuiID id, const ImVec2 min, const ImVec2 max, double& value, const double minimum,
                const double maximum, const bool open, const double scale)
{
    if (open) {
        widgetState().entryId = id;
        std::snprintf(widgetState().entryText, sizeof(widgetState().entryText), "%.6g", value * scale);
        widgetState().entryFresh = true;
        ImGui::OpenPopupEx(id, ImGuiPopupFlags_None);
    }
    if (widgetState().entryId != id)
        return false;
    const auto width = std::max(max.x - min.x, dp(72.0f));
    ImGui::SetNextWindowPos(ImVec2((min.x + max.x - width) * 0.5f, min.y - dp(3.0f)));
    ImGui::SetNextWindowSize(ImVec2(width, 0.0f));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, dp(2.0f, 2.0f));
    const auto shown = ImGui::BeginPopupEx(id, ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove
                                                   | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_AlwaysAutoResize);
    ImGui::PopStyleVar();
    if (!shown) {
        widgetState().entryId = 0;
        return false;
    }
    auto set = false;
    if (widgetState().entryFresh) {
        ImGui::SetKeyboardFocusHere();
        widgetState().entryFresh = false;
    }
    ImGui::SetNextItemWidth(-1.0f);
    if (ImGui::InputText("##typed", widgetState().entryText, sizeof(widgetState().entryText),
                         ImGuiInputTextFlags_EnterReturnsTrue | ImGuiInputTextFlags_AutoSelectAll)) {
        if (const auto typed = typedNumber(widgetState().entryText)) {
            value = std::clamp(*typed / (scale != 0.0 ? scale : 1.0), std::min(minimum, maximum), std::max(minimum, maximum));
            set = true;
        }
        ImGui::CloseCurrentPopup();
        widgetState().entryId = 0;
    } else if (ImGui::IsKeyPressed(ImGuiKey_Escape)) {
        ImGui::CloseCurrentPopup();
        widgetState().entryId = 0;
    }
    ImGui::EndPopup();
    return set;
}

bool dragValue(const char* id, float* value, const float speed, const float minimum, const float maximum, const char* format,
               const float width)
{
    if (width != 0.0f)
        ImGui::SetNextItemWidth(width);
    const auto frameWidth = ImGui::CalcItemWidth();
    const auto bounded = minimum < maximum;
    // CyUI's keys (valueSpeed) instead of ImGui's own, and CyUI's typed entry.
    auto changed = ImGui::DragFloat(id, value, speed * valueSpeed(), minimum, maximum, format,
                                    ImGuiSliderFlags_AlwaysClamp | ImGuiSliderFlags_NoSpeedTweaks | ImGuiSliderFlags_NoInput);
    const auto itemId = ImGui::GetItemID();
    const auto min = ImGui::GetItemRectMin();
    const ImVec2 max(min.x + frameWidth, ImGui::GetItemRectMax().y);
    const auto open = ImGui::IsItemHovered() && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left);
    // The wheel only once the field has the focus (clicked or navigated to):
    // scrolling a panel full of fields must not change them.
    const auto wheel = ImGui::GetFocusID() == itemId ? itemWheel() : 0.0f;
    if (wheel != 0.0f && !ImGui::IsItemActive()) {
        auto next = *value + wheel * speed * 10.0f * valueSpeed();
        if (bounded)
            next = std::clamp(next, minimum, maximum);
        if (next != *value) {
            *value = next;
            changed = true;
            markEdited(itemId);
        }
    }
    auto typed = static_cast<double>(*value);
    if (valueEntry(itemId, min, max, typed, bounded ? minimum : -FLT_MAX, bounded ? maximum : FLT_MAX, open)) {
        *value = static_cast<float>(typed);
        changed = true;
        markEdited(itemId);
    }
    return changed;
}

bool sliderBar(const char* id, float* value, const float minimum, const float maximum, const char* format, float width,
               const ImU32 colour)
{
    const auto& p = palette();
    if (width == 0.0f)
        width = ImGui::CalcItemWidth();
    else if (width < 0.0f)
        width = std::max(dp(20.0f), ImGui::GetContentRegionAvail().x + width);
    const auto height = ImGui::GetFrameHeight();
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::PushID(id);
    ImGui::InvisibleButton("##bar", ImVec2(width, height));
    const auto itemId = ImGui::GetItemID();
    const auto hovered = ImGui::IsItemHovered();
    const auto active = ImGui::IsItemActive();
    auto& state = widgetState();
    auto changed = false;
    const auto& io = ImGui::GetIO();
    const auto range = maximum - minimum;
    const auto set = [&](const float next) {
        if (next != *value) {
            *value = next;
            changed = true;
        }
    };
    // A click sets the value; a double click types it, undoing its first click.
    const auto doubleClicked = hovered && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left);
    if (doubleClicked && state.pressId == itemId)
        set(state.pressValue);
    else if (ImGui::IsItemActivated()) {
        state.pressId = itemId;
        state.pressValue = *value;
    }
    if (active && !doubleClicked && state.entryId != itemId && range > 0.0f)
        set(minimum + range * std::clamp((io.MousePos.x - origin.x) / std::max(1.0f, width), 0.0f, 1.0f));
    if (const auto wheel = itemWheel(); wheel != 0.0f && !active)
        set(std::clamp(*value + wheel * range * 0.01f * valueSpeed(), minimum, maximum));
    auto typed = static_cast<double>(*value);
    if (valueEntry(itemId, origin, ImVec2(origin.x + width, origin.y + height), typed, minimum, maximum, doubleClicked))
        set(static_cast<float>(typed));
    if (changed)
        markEdited(itemId);
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = ImGui::GetStyle().FrameRounding;
    const ImVec2 max(origin.x + width, origin.y + height);
    list->AddRectFilled(origin, max, faded(active ? p.bg5 : hovered ? p.bg4 : p.bg3), rounding);
    const auto t = range > 0.0f ? std::clamp((*value - minimum) / range, 0.0f, 1.0f) : 0.0f;
    if (t > 0.0f) {
        list->PushClipRect(origin, ImVec2(origin.x + width * t, max.y), true);
        list->AddRectFilled(origin, max, faded(alpha(colour != 0 ? colour : p.accent, hovered || active ? 0.55f : 0.42f)), rounding);
        list->PopClipRect();
    }
    char text[64];
    std::snprintf(text, sizeof(text), format != nullptr ? format : "%.2f", *value);
    centredText(list, origin, max, p.text, text);
    ImGui::PopID();
    return changed;
}

// ---- Fields --------------------------------------------------------------------

bool searchField(const char* id, char* buffer, const int bufferSize, const char* hint, const float width)
{
    const auto translatedHint = tr(hint);
    hint = translatedHint.c_str();
    const auto& p = palette();
    const auto& style = ImGui::GetStyle();
    const auto height = ImGui::GetFrameHeight();
    const auto iconRoom = height * 0.9f;
    ImGui::PushID(id);
    const auto origin = ImGui::GetCursorScreenPos();
    const auto fieldWidth = width != 0.0f ? (width > 0.0f ? width : ImGui::GetContentRegionAvail().x + width)
                                          : ImGui::CalcItemWidth();
    ImGui::SetNextItemWidth(std::max(dp(40.0f), fieldWidth));
    ImGui::SetNextItemAllowOverlap();
    auto changed = false;
    {
        ScopedVar vars(ImGuiStyleVar_FramePadding, ImVec2(style.FramePadding.x + iconRoom * 0.75f, style.FramePadding.y));
        vars.push(ImGuiStyleVar_FrameRounding, height * 0.5f);
        changed = ImGui::InputTextWithHint("##search", hint != nullptr ? hint : "", buffer, static_cast<std::size_t>(bufferSize));
    }
    const auto focused = ImGui::IsItemActive();
    const auto end = ImGui::GetCursorScreenPos();
    auto* list = ImGui::GetWindowDrawList();
    drawMagnifier(list, ImVec2(origin.x + style.FramePadding.x + iconRoom * 0.3f, origin.y + height * 0.5f), height * 0.48f,
                  faded(focused ? p.accent : p.textFaint));
    if (buffer[0] != 0) {
        const auto clearSize = height;
        ImGui::SetCursorScreenPos(ImVec2(origin.x + std::max(dp(40.0f), fieldWidth) - clearSize, origin.y));
        if (ImGui::InvisibleButton("##clear", ImVec2(clearSize, clearSize))) {
            buffer[0] = 0;
            changed = true;
        }
        const auto hovered = ImGui::IsItemHovered();
        drawCross(list, ImVec2(origin.x + std::max(dp(40.0f), fieldWidth) - clearSize * 0.5f, origin.y + height * 0.5f),
                  height * 0.26f, faded(hovered ? p.text : p.textFaint));
        ImGui::SetCursorScreenPos(end);
    }
    ImGui::PopID();
    return changed;
}

bool textField(const char* id, char* buffer, const int bufferSize, const char* hint, const float width)
{
    const auto translatedHint = tr(hint);
    hint = translatedHint.c_str();
    if (width != 0.0f)
        ImGui::SetNextItemWidth(width);
    return ImGui::InputTextWithHint(id, hint != nullptr ? hint : "", buffer, static_cast<std::size_t>(bufferSize));
}

bool combo(const char* id, int* current, const char* const items[], const int count, const float width)
{
    std::vector<std::string> translated;
    std::vector<const char*> translatedItems;
    translated.reserve(std::size_t(std::max(0, count)));
    translatedItems.reserve(std::size_t(std::max(0, count)));
    for (int i = 0; i < count; ++i) translated.push_back(tr(items[i]));
    for (const auto& item : translated) translatedItems.push_back(item.c_str());
    if (width != 0.0f)
        ImGui::SetNextItemWidth(width);
    return ImGui::Combo(id, current, translatedItems.data(), count);
}

// ---- Containers ----------------------------------------------------------------

bool beginCard(const char* id, const char* title, const float height, const Tone tone)
{
    const auto& p = palette();
    const auto toned = tone != Tone::neutral;
    const auto ground = toned ? mix(p.bg2, toneColour(tone), 0.08f) : p.bg2;
    const auto border = toned ? mix(p.line, toneColour(tone), 0.55f) : p.line;
    ImGuiChildFlags flags = ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding;
    if (height == 0.0f)
        flags |= ImGuiChildFlags_AutoResizeY;
    const auto shown = [&] {
        ScopedColor colours(ImGuiCol_ChildBg, ground);
        colours.push(ImGuiCol_Border, border);
        ScopedVar vars(ImGuiStyleVar_ChildRounding, dp(metrics().roundingLarge + 2.0f));
        vars.push(ImGuiStyleVar_WindowPadding, dp(metrics().cardPadding, metrics().cardPadding));
        return ImGui::BeginChild(id, ImVec2(0.0f, height == 0.0f ? 0.0f : dp(height)), flags);
    }();
    if (shown && title != nullptr && title[0] != 0) {
        heading(title, 3);
        ImGui::Dummy(ImVec2(0.0f, dp(2.0f)));
    }
    return shown;
}

void endCard()
{
    ImGui::EndChild();
}

bool beginSection(const char* label, const bool defaultOpen)
{
    const auto& p = palette();
    ScopedColor colours(ImGuiCol_Header, p.bg2);
    colours.push(ImGuiCol_HeaderHovered, p.bg3).push(ImGuiCol_HeaderActive, p.bg4);
    ScopedFont font(boldFont(), 0.92f);
    const auto open = ImGui::CollapsingHeader(label, defaultOpen ? ImGuiTreeNodeFlags_DefaultOpen : ImGuiTreeNodeFlags_None);
    return open;
}

void endSection()
{
    ImGui::Dummy(ImVec2(0.0f, dp(4.0f)));
}

bool beginProperties(const char* id, const float labelWidth)
{
    const auto avail = ImGui::GetContentRegionAvail().x;
    const auto width = labelWidth > 0.0f ? dp(labelWidth) : std::max(dp(80.0f), avail * 0.38f);
    if (!ImGui::BeginTable(id, 2, ImGuiTableFlags_None))
        return false;
    ImGui::TableSetupColumn("##label", ImGuiTableColumnFlags_WidthFixed, width);
    ImGui::TableSetupColumn("##value", ImGuiTableColumnFlags_WidthStretch);
    return true;
}

void property(const char* label, const char* help)
{
    ImGui::TableNextRow();
    ImGui::TableSetColumnIndex(0);
    ImGui::AlignTextToFramePadding();
    const auto pos = ImGui::GetCursorScreenPos();
    const auto room = ImGui::GetContentRegionAvail().x - (help != nullptr ? ImGui::GetFontSize() : 0.0f);
    const auto size = ImGui::CalcTextSize(label);
    if (size.x <= room) {
        textDim("%s", label);
    } else {
        clippedText(ImGui::GetWindowDrawList(), ImVec2(pos.x, pos.y + ImGui::GetCurrentWindow()->DC.CurrLineTextBaseOffset), room,
                    palette().textDim, label);
        ImGui::Dummy(ImVec2(room, ImGui::GetTextLineHeight()));
        tooltip(label);
    }
    if (help != nullptr)
        helpMarker(help);
    ImGui::TableSetColumnIndex(1);
    ImGui::SetNextItemWidth(-FLT_MIN);
}

void endProperties()
{
    ImGui::EndTable();
}

void toolbarSeparator()
{
    ImGui::SameLine(0.0f, dp(6.0f));
    const auto origin = ImGui::GetCursorScreenPos();
    const auto height = ImGui::GetFrameHeight();
    ImGui::GetWindowDrawList()->AddLine(ImVec2(origin.x, origin.y + height * 0.18f), ImVec2(origin.x, origin.y + height * 0.82f),
                                        faded(palette().lineStrong), hairline());
    ImGui::Dummy(ImVec2(dp(1.0f), height));
    ImGui::SameLine(0.0f, dp(6.0f));
}

void divider(const float spacing)
{
    ImGui::Dummy(ImVec2(0.0f, dp(spacing)));
    const auto origin = ImGui::GetCursorScreenPos();
    const auto width = ImGui::GetContentRegionAvail().x;
    ImGui::GetWindowDrawList()->AddLine(ImVec2(origin.x, origin.y), ImVec2(origin.x + width, origin.y), faded(palette().line),
                                        hairline());
    ImGui::Dummy(ImVec2(0.0f, dp(spacing)));
}

void space(const float height)
{
    ImGui::Dummy(ImVec2(0.0f, dp(height)));
}

// ---- Status & feedback -----------------------------------------------------------

void badge(const char* text, const Tone tone)
{
    const auto& p = palette();
    const auto colour = tone == Tone::neutral ? p.textDim : toneColour(tone);
    ScopedFont font(boldFont(), 0.74f);
    const auto size = ImGui::CalcTextSize(text);
    const auto pad = dp(6.0f, 2.0f);
    const ImVec2 box(size.x + pad.x * 2.0f, size.y + pad.y * 2.0f);
    const auto line = ImGui::GetTextLineHeight() / 0.74f;
    auto origin = ImGui::GetCursorScreenPos();
    origin.y += std::max(0.0f, (std::max(line, ImGui::GetCurrentWindow()->DC.CurrLineSize.y) - box.y) * 0.5f);
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = dp(metrics().roundingSmall + 1.0f);
    list->AddRectFilled(origin, ImVec2(origin.x + box.x, origin.y + box.y),
                        faded(tone == Tone::neutral ? p.bg4 : alpha(colour, 0.16f)), rounding);
    list->AddRect(origin, ImVec2(origin.x + box.x, origin.y + box.y), faded(alpha(colour, 0.35f)), rounding, 0, hairline());
    list->AddText(ImVec2(origin.x + pad.x, origin.y + pad.y), faded(tone == Tone::neutral ? p.text : mix(colour, p.text, 0.3f)),
                  text);
    ImGui::Dummy(ImVec2(box.x, box.y));
}

void statusDot(const ImU32 colour, const char* tip)
{
    const auto line = ImGui::GetTextLineHeight();
    const auto origin = ImGui::GetCursorScreenPos();
    const auto radius = line * 0.24f;
    ImGui::Dummy(ImVec2(radius * 2.0f + dp(2.0f), line));
    auto* list = ImGui::GetWindowDrawList();
    const ImVec2 centre(origin.x + radius + dp(1.0f), origin.y + line * 0.5f);
    list->AddCircleFilled(centre, radius * 1.8f, faded(alpha(colour, 0.18f)), 16);
    list->AddCircleFilled(centre, radius, faded(colour), 16);
    tooltip(tip);
}

void progressBar(const float fraction, ImVec2 size, const char* overlay, const Tone tone)
{
    const auto& p = palette();
    if (size.x <= 0.0f)
        size.x = std::max(dp(20.0f), ImGui::GetContentRegionAvail().x + (size.x < 0.0f ? size.x + 1.0f : 0.0f));
    if (size.y <= 0.0f)
        size.y = overlay != nullptr ? ImGui::GetFrameHeight() : dp(6.0f);
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::Dummy(size);
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = std::min(size.y * 0.5f, ImGui::GetStyle().FrameRounding + dp(2.0f));
    const ImVec2 max(origin.x + size.x, origin.y + size.y);
    const auto colour = tone == Tone::neutral ? p.textDim : toneColour(tone);
    list->AddRectFilled(origin, max, faded(p.bg3), rounding);
    if (fraction >= 0.0f) {
        const auto t = std::clamp(fraction, 0.0f, 1.0f);
        if (t > 0.0f)
            list->AddRectFilled(origin, ImVec2(origin.x + std::max(rounding * 2.0f, size.x * t), max.y), faded(colour), rounding);
    } else {
        // Indeterminate: a segment sweeping across.
        const auto phase = static_cast<float>(std::fmod(ImGui::GetTime() * 0.8, 1.0));
        const auto segment = size.x * 0.3f;
        const auto x0 = origin.x - segment + (size.x + segment) * phase;
        list->PushClipRect(origin, max, true);
        list->AddRectFilled(ImVec2(x0, origin.y), ImVec2(x0 + segment, max.y), faded(colour), rounding);
        list->PopClipRect();
    }
    if (overlay != nullptr)
        centredText(list, origin, max, p.text, overlay);
}

void spinner(const char* id, const float radius, const ImU32 colour)
{
    ImGui::PushID(id);
    const auto r = dp(radius);
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::Dummy(ImVec2(r * 2.0f + dp(2.0f), r * 2.0f + dp(2.0f)));
    auto* list = ImGui::GetWindowDrawList();
    const ImVec2 centre(origin.x + r + dp(1.0f), origin.y + r + dp(1.0f));
    const auto start = static_cast<float>(ImGui::GetTime()) * 6.0f;
    const auto thickness = std::max(dp(1.5f), r * 0.28f);
    list->PathClear();
    list->PathArcTo(centre, r - thickness * 0.5f, 0.0f, kPi * 2.0f, 24);
    list->PathStroke(faded(alpha(palette().textFaint, 0.35f)), 0, thickness);
    arc(list, centre, r - thickness * 0.5f, start, start + kPi * 1.3f, faded(colour != 0 ? colour : palette().accent), thickness);
    ImGui::PopID();
}

void emptyState(const char* title, const char* message, const char* iconText)
{
    const auto& p = palette();
    const auto area = ImGui::GetContentRegionAvail();
    const auto origin = ImGui::GetCursorScreenPos();
    auto* list = ImGui::GetWindowDrawList();
    const auto middle = origin.y + std::max(dp(60.0f), area.y * 0.45f);
    if (iconText != nullptr && iconsAvailable()) {
        ScopedFont font(uiFont(), 2.4f);
        centredText(list, ImVec2(origin.x, middle - dp(70.0f)), ImVec2(origin.x + area.x, middle - dp(24.0f)), p.textFaint, iconText);
    }
    {
        ScopedFont font(boldFont(), 1.1f);
        centredText(list, ImVec2(origin.x, middle - dp(22.0f)), ImVec2(origin.x + area.x, middle), p.textDim, title);
    }
    if (message != nullptr)
        centredText(list, ImVec2(origin.x, middle), ImVec2(origin.x + area.x, middle + dp(20.0f)), p.textFaint, message);
    ImGui::Dummy(ImVec2(area.x, std::max(0.0f, middle + dp(24.0f) - origin.y)));
}

void callout(const Tone tone, const char* text)
{
    const auto& p = palette();
    const auto colour = tone == Tone::neutral ? p.textDim : toneColour(tone);
    const auto width = ImGui::GetContentRegionAvail().x;
    const auto pad = dp(10.0f, 7.0f);
    const auto bar = dp(3.0f);
    const auto wrap = std::max(dp(40.0f), width - pad.x * 2.0f - bar);
    const auto size = ImGui::CalcTextSize(text, nullptr, false, wrap);
    const auto origin = ImGui::GetCursorScreenPos();
    const ImVec2 max(origin.x + width, origin.y + size.y + pad.y * 2.0f);
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = ImGui::GetStyle().FrameRounding;
    list->AddRectFilled(origin, max, faded(mix(p.bg2, colour, 0.10f)), rounding);
    list->AddRectFilled(origin, ImVec2(origin.x + bar, max.y), faded(colour), rounding, ImDrawFlags_RoundCornersLeft);
    list->AddText(nullptr, 0.0f, ImVec2(origin.x + bar + pad.x, origin.y + pad.y), faded(p.text), text, nullptr, wrap);
    ImGui::Dummy(ImVec2(width, max.y - origin.y));
}

bool colourChip(const char* id, const ImU32 colour, const ImVec2 size)
{
    const auto origin = ImGui::GetCursorScreenPos();
    const auto clicked = ImGui::InvisibleButton(id, size);
    auto* list = ImGui::GetWindowDrawList();
    const ImVec2 max(origin.x + size.x, origin.y + size.y);
    list->AddRectFilled(origin, max, faded(colour), dp(2.0f));
    if (ImGui::IsItemHovered())
        list->AddRect(origin, max, faded(palette().text), dp(2.0f), 0, dp(1.5f));
    return clicked;
}

// ---- Notifications ---------------------------------------------------------------

void notify(ImGuiContext* context, const char* text, const Tone tone, const float seconds)
{
    if (context == nullptr || text == nullptr)
        return;
    std::lock_guard<std::mutex> lock(gWidgets.mutex);
    gWidgets.states[context].posted.push_back({ text, tone, seconds });
}

void notify(const char* text, const Tone tone, const float seconds)
{
    auto* context = ImGui::GetCurrentContext();
    if (context == nullptr) {
        // No context current: the one CyUI context there is, if there is one.
        std::lock_guard<std::mutex> lock(gWidgets.mutex);
        IM_ASSERT(gWidgets.states.size() == 1 && "cyui::notify: no current ImGui context; pass one");
        if (gWidgets.states.size() == 1 && text != nullptr)
            gWidgets.states.begin()->second.posted.push_back({ text, tone, seconds });
        return;
    }
    notify(context, text, tone, seconds);
}

void renderNotifications()
{
    const auto now = ImGui::GetTime();
    auto& state = widgetState();
    auto& toasts = state.toasts;
    toasts.erase(std::remove_if(toasts.begin(), toasts.end(), [now](const Toast& toast) { return toast.until < now; }),
                 toasts.end());
    {
        std::lock_guard<std::mutex> lock(gWidgets.mutex);
        for (auto& posted : state.posted) {
            if (static_cast<int>(toasts.size()) >= kMaxToasts)
                toasts.erase(toasts.begin());
            // The lowest free window slot: a toast keeps its window, and
            // there are never more than kMaxToasts windows.
            auto slot = 0;
            while (std::any_of(toasts.begin(), toasts.end(), [slot](const Toast& toast) { return toast.slot == slot; }))
                ++slot;
            toasts.push_back({ std::move(posted.text), posted.tone, now + posted.seconds, slot });
        }
        state.posted.clear();
    }
    if (toasts.empty())
        return;
    const auto& p = palette();
    const auto* viewport = ImGui::GetMainViewport();
    auto y = viewport->WorkPos.y + viewport->WorkSize.y - dp(16.0f);
    const auto x = viewport->WorkPos.x + viewport->WorkSize.x - dp(16.0f);
    for (auto i = static_cast<int>(toasts.size()) - 1; i >= 0; --i) {
        auto& toast = toasts[static_cast<std::size_t>(i)];
        const auto left = static_cast<float>(toast.until - now);
        const auto fade = std::clamp(left / 0.3f, 0.0f, 1.0f);
        ImGui::SetNextWindowPos(ImVec2(x, y), ImGuiCond_Always, ImVec2(1.0f, 1.0f));
        ImGui::SetNextWindowBgAlpha(0.98f * fade);
#ifdef IMGUI_HAS_DOCK
        ImGui::SetNextWindowViewport(viewport->ID);
#endif
        char name[48];
        std::snprintf(name, sizeof(name), "##cyuiToast%d", toast.slot);
        ScopedVar vars(ImGuiStyleVar_WindowPadding, dp(14.0f, 9.0f));
        vars.push(ImGuiStyleVar_WindowRounding, dp(metrics().roundingLarge + 2.0f)).push(ImGuiStyleVar_Alpha, fade);
        ScopedColor colours(ImGuiCol_WindowBg, p.bg2);
        colours.push(ImGuiCol_Border, p.lineStrong);
        auto flags = ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoSavedSettings
                     | ImGuiWindowFlags_NoFocusOnAppearing | ImGuiWindowFlags_NoNav | ImGuiWindowFlags_NoMove;
#ifdef IMGUI_HAS_DOCK
        flags |= ImGuiWindowFlags_NoDocking;
#endif
        auto height = 0.0f;
        if (ImGui::Begin(name, nullptr, flags)) {
            const auto colour = toast.tone == Tone::neutral ? p.accent : toneColour(toast.tone);
            const auto* window = ImGui::GetCurrentWindow();
            ImGui::GetWindowDrawList()->AddRectFilled(window->Pos, ImVec2(window->Pos.x + dp(3.0f), window->Pos.y + window->Size.y),
                                                      faded(colour), dp(metrics().roundingLarge + 2.0f), ImDrawFlags_RoundCornersLeft);
            ImGui::PushTextWrapPos(ImGui::GetFontSize() * 26.0f);
            ImGui::TextUnformatted(toast.text.c_str());
            ImGui::PopTextWrapPos();
            if (ImGui::IsWindowHovered() && ImGui::IsMouseClicked(ImGuiMouseButton_Left))
                toast.until = now + 0.3;
            height = window->Size.y;
        }
        ImGui::End();
        y -= height + dp(8.0f);
    }
}

}  // namespace cyui
