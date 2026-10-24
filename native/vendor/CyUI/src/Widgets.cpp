#include "cyui/Widgets.h"

#include "cyui/Icons.h"

#include <imgui_internal.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <optional>
#include <string>
#include <vector>

namespace cyui {
namespace {

constexpr float kPi = 3.14159265358979f;
constexpr float kArcStart = kPi * 0.75f;
constexpr float kArcSweep = kPi * 1.5f;

// Dark or light text, whichever reads on `ground`.
ImU32 contrastText(const ImU32 ground)
{
    const auto r = static_cast<float>(ground & 0xffu) / 255.0f;
    const auto g = static_cast<float>((ground >> 8) & 0xffu) / 255.0f;
    const auto b = static_cast<float>((ground >> 16) & 0xffu) / 255.0f;
    const auto luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
    return luminance > 0.55f ? rgb(0x141414) : rgb(0xffffff);
}

float toNormalised(const float value, const float minimum, const float maximum, const bool logarithmic)
{
    if (maximum <= minimum)
        return 0.0f;
    if (logarithmic && minimum > 0.0f)
        return std::clamp(std::log(value / minimum) / std::log(maximum / minimum), 0.0f, 1.0f);
    return std::clamp((value - minimum) / (maximum - minimum), 0.0f, 1.0f);
}

float fromNormalised(const float t, const float minimum, const float maximum, const bool logarithmic)
{
    const auto clamped = std::clamp(t, 0.0f, 1.0f);
    if (logarithmic && minimum > 0.0f)
        return minimum * std::pow(maximum / minimum, clamped);
    return minimum + (maximum - minimum) * clamped;
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

std::string formatValue(const float value, const KnobStyle& style)
{
    char text[64];
    std::snprintf(text, sizeof(text), style.format != nullptr ? style.format : "%.2f",
                  (style.flags & knobInteger) != 0 ? std::round(value) : value);
    std::string out(text);
    if (style.unit != nullptr && style.unit[0] != 0) {
        out += ' ';
        out += style.unit;
    }
    return out;
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
    list->AddCircle(c, r, colour, 16, std::max(1.2f, size * 0.1f));
    list->AddLine(ImVec2(c.x + r * 0.7f, c.y + r * 0.7f), ImVec2(centre.x + size * 0.42f, centre.y + size * 0.42f), colour,
                  std::max(1.4f, size * 0.12f));
}

void drawCross(ImDrawList* list, const ImVec2 centre, const float size, const ImU32 colour)
{
    const auto h = size * 0.5f;
    const auto t = std::max(1.2f, size * 0.12f);
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
                        p.lineStrong, rounding);
    list->AddRectFilled(origin, ImVec2(origin.x + box.x, origin.y + box.y), p.bg3, rounding);
    list->AddRect(origin, ImVec2(origin.x + box.x, origin.y + box.y), p.line, rounding);
    list->AddText(ImVec2(origin.x + pad.x, origin.y + pad.y), p.textDim, keys);
    ImGui::Dummy(ImVec2(box.x, box.y + dp(1.0f)));
}

void tooltip(const char* text)
{
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
    if (text == nullptr || maxWidth <= 4.0f)
        return;
    const auto size = ImGui::CalcTextSize(text);
    if (size.x <= maxWidth) {
        list->AddText(position, colour, text);
        return;
    }
    static constexpr const char* ellipsis = "...";
    const auto room = maxWidth - ImGui::CalcTextSize(ellipsis).x;
    std::string shown(text);
    while (!shown.empty() && ImGui::CalcTextSize(shown.c_str()).x > room) {
        shown.pop_back();
        // Never cut a UTF-8 character in two.
        while (!shown.empty() && (static_cast<unsigned char>(shown.back()) & 0xc0u) == 0x80u)
            shown.pop_back();
        if (!shown.empty() && (static_cast<unsigned char>(shown.back()) & 0xc0u) == 0xc0u)
            shown.pop_back();
    }
    shown += ellipsis;
    list->AddText(position, colour, shown.c_str());
}

void centredText(ImDrawList* list, const ImVec2 min, const ImVec2 max, const ImU32 colour, const char* text)
{
    const auto size = ImGui::CalcTextSize(text);
    list->AddText(ImVec2(min.x + (max.x - min.x - size.x) * 0.5f, min.y + (max.y - min.y - size.y) * 0.5f), colour, text);
}

// ---- Buttons -----------------------------------------------------------------

bool button(const char* label, const Variant variant, const ImVec2 size)
{
    const auto c = variantColours(variant);
    ScopedColor colours(ImGuiCol_Button, c.ground);
    colours.push(ImGuiCol_ButtonHovered, c.hovered).push(ImGuiCol_ButtonActive, c.active).push(ImGuiCol_Text, c.text);
    if (c.border != 0) {
        colours.push(ImGuiCol_Border, c.border);
        ScopedVar border(ImGuiStyleVar_FrameBorderSize, 1.0f);
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
    auto* list = ImGui::GetWindowDrawList();
    const auto colour = activeColour != 0 ? activeColour : p.accent;
    const auto rounding = ImGui::GetStyle().FrameRounding;
    const ImVec2 max(origin.x + size.x, origin.y + size.y);
    if (active)
        list->AddRectFilled(origin, max, alpha(colour, held ? 0.28f : 0.18f), rounding);
    else if (hovered)
        list->AddRectFilled(origin, max, held ? p.bg5 : p.bg4, rounding);
    centredText(list, origin, max, active ? colour : (hovered ? p.text : p.textDim), icon);
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
    if (clicked)
        *value = !*value;
    const auto hovered = ImGui::IsItemHovered();
    auto* list = ImGui::GetWindowDrawList();
    const auto on = *value;
    const auto fill = on ? (hovered ? brighter(onColour, 0.1f) : onColour) : (hovered ? p.bg4 : p.bg3);
    list->AddRectFilled(origin, ImVec2(origin.x + size.x, origin.y + size.y), fill, ImGui::GetStyle().FrameRounding);
    const auto textColour = on ? contrastText(onColour) : (hovered ? p.text : p.textDim);
    list->AddText(ImVec2(origin.x + (size.x - textSize.x) * 0.5f, origin.y + (size.y - textSize.y) * 0.5f), textColour, label,
                  ImGui::FindRenderedTextEnd(label));
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
    if (clicked)
        *value = !*value;
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
    list->AddRectFilled(trackMin, trackMax, mix(off, on, t), trackHeight * 0.5f);
    const auto radius = trackHeight * 0.5f - dp(2.5f);
    const ImVec2 knobCentre(trackMin.x + trackHeight * 0.5f + (trackWidth - trackHeight) * t, trackMin.y + trackHeight * 0.5f);
    // The knob: light on a dark theme's track, white on a light theme's.
    const auto offKnob = contrastText(p.bg1) == rgb(0xffffff) ? p.text : rgb(0xffffff);
    list->AddCircleFilled(ImVec2(knobCentre.x, knobCentre.y + dp(0.5f)), radius + dp(0.5f), rgb(0x000000, 0.18f), 20);
    list->AddCircleFilled(knobCentre, radius, mix(offKnob, contrastText(p.accent), t), 20);
    if (hasText)
        list->AddText(ImVec2(trackMax.x + style.ItemInnerSpacing.x, origin.y + (height - textSize.y) * 0.5f), p.text, label,
                      end);
    return clicked;
}

bool segmented(const char* id, const char* const labels[], const int count, int* current, const float width)
{
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
    list->AddRectFilled(origin, ImVec2(origin.x + total, origin.y + height), p.bg0, style.FrameRounding + inset);
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
        }
        const auto hovered = ImGui::IsItemHovered();
        ImGui::PopID();
        const auto selected = *current == i;
        const ImVec2 min(x, origin.y + inset);
        const ImVec2 max(x + w, origin.y + height - inset);
        if (selected)
            list->AddRectFilled(min, max, p.accentDim, style.FrameRounding);
        else if (hovered)
            list->AddRectFilled(min, max, p.bg3, style.FrameRounding);
        const auto* end = ImGui::FindRenderedTextEnd(labels[i]);
        const auto textSize = ImGui::CalcTextSize(labels[i], end);
        list->AddText(ImVec2(min.x + (w - textSize.x) * 0.5f, min.y + (size.y - textSize.y) * 0.5f),
                      selected ? contrastText(p.accentDim) : (hovered ? p.text : p.textDim), labels[i], end);
        x += w;
    }
    ImGui::SetCursorScreenPos(origin);
    ImGui::Dummy(ImVec2(total, height));
    ImGui::PopID();
    return changed;
}

bool linkText(const char* label)
{
    return ImGui::TextLink(label);
}

// ---- Value controls ------------------------------------------------------------

namespace {
int gWheelFrame = -1;
ImGuiID gEntryId {};
char gEntryText[64] {};
bool gEntryFresh {};

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
    gWheelFrame = ImGui::GetFrameCount();
}

bool wheelTaken()
{
    return gWheelFrame == ImGui::GetFrameCount();
}

float valueSpeed()
{
    const auto& io = ImGui::GetIO();
    return io.KeyCtrl ? 0.15f : io.KeyShift ? 4.0f : 1.0f;
}

bool valueEntryOpen()
{
    return gEntryId != 0 && ImGui::IsPopupOpen(gEntryId, ImGuiPopupFlags_None);
}

bool valueEntry(const ImGuiID id, const ImVec2 min, const ImVec2 max, double& value, const double minimum,
                const double maximum, const bool open, const double scale)
{
    if (open) {
        gEntryId = id;
        std::snprintf(gEntryText, sizeof(gEntryText), "%.6g", value * scale);
        gEntryFresh = true;
        ImGui::OpenPopupEx(id, ImGuiPopupFlags_None);
    }
    if (gEntryId != id)
        return false;
    const auto width = std::max(max.x - min.x, dp(72.0f));
    ImGui::SetNextWindowPos(ImVec2((min.x + max.x - width) * 0.5f, min.y - dp(3.0f)));
    ImGui::SetNextWindowSize(ImVec2(width, 0.0f));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, dp(2.0f, 2.0f));
    const auto shown = ImGui::BeginPopupEx(id, ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove
                                                   | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_AlwaysAutoResize);
    ImGui::PopStyleVar();
    if (!shown) {
        gEntryId = 0;
        return false;
    }
    auto set = false;
    if (gEntryFresh) {
        ImGui::SetKeyboardFocusHere();
        gEntryFresh = false;
    }
    ImGui::SetNextItemWidth(-1.0f);
    if (ImGui::InputText("##typed", gEntryText, sizeof(gEntryText),
                         ImGuiInputTextFlags_EnterReturnsTrue | ImGuiInputTextFlags_AutoSelectAll)) {
        if (const auto typed = typedNumber(gEntryText)) {
            value = std::clamp(*typed / (scale != 0.0 ? scale : 1.0), std::min(minimum, maximum), std::max(minimum, maximum));
            set = true;
        }
        ImGui::CloseCurrentPopup();
        gEntryId = 0;
    } else if (ImGui::IsKeyPressed(ImGuiKey_Escape)) {
        ImGui::CloseCurrentPopup();
        gEntryId = 0;
    }
    ImGui::EndPopup();
    return set;
}

bool knob(const char* id, float* value, const float minimum, const float maximum, const float defaultValue,
          const KnobStyle& style)
{
    const auto& p = palette();
    auto& io = ImGui::GetIO();
    const auto diameter = dp(style.diameter);
    const auto labelHeight = (style.flags & knobNoLabel) != 0 ? 0.0f : ImGui::GetFontSize() * 0.86f;
    const auto width = dp(style.diameter + 12.0f);
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::PushID(id);
    ImGui::InvisibleButton("##knob", ImVec2(width, diameter + labelHeight));
    const auto knobId = ImGui::GetItemID();
    const auto hovered = ImGui::IsItemHovered();
    const auto active = ImGui::IsItemActive();
    const auto logarithmic = (style.flags & knobLogarithmic) != 0;
    const auto steps = std::max(0, style.steps);
    const auto onStep = [steps](const float position) {
        return steps > 0 ? std::round(position * static_cast<float>(steps)) / static_cast<float>(steps) : position;
    };
    auto changed = false;
    auto t = toNormalised(*value, minimum, maximum, logarithmic);

    // The unrounded position a drag has reached, so small moves add up to a step.
    auto* storage = ImGui::GetStateStorage();
    const auto heldKey = ImGui::GetID("##held");
    const auto movedKey = ImGui::GetID("##moved");
    if (ImGui::IsItemActivated()) {
        storage->SetFloat(heldKey, t);
        storage->SetBool(movedKey, false);
    }
    if (active && (io.MouseDelta.x != 0.0f || io.MouseDelta.y != 0.0f)) {
        storage->SetBool(movedKey, true);
        const auto speed = 0.0055f * valueSpeed() / std::max(0.5f, ImGui::GetStyle().FontScaleDpi);
        const auto held = std::clamp(storage->GetFloat(heldKey, t) + (-io.MouseDelta.y + io.MouseDelta.x * 0.35f) * speed, 0.0f, 1.0f);
        storage->SetFloat(heldKey, held);
        t = onStep(held);
        changed = true;
    }
    if (hovered && io.MouseWheel != 0.0f) {
        const auto notch = steps > 0 ? 1.0f / static_cast<float>(steps) : 0.02f * valueSpeed();
        t = onStep(std::clamp(t + io.MouseWheel * notch, 0.0f, 1.0f));
        changed = true;
        takeWheel();
    }
    if (hovered && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left)) {
        t = toNormalised(defaultValue, minimum, maximum, logarithmic);
        storage->SetFloat(heldKey, t);
        changed = true;
    }
    // Its number clicked without a drag: typed in.
    const auto onNumber = labelHeight > 0.0f && io.MouseClickedPos[0].y >= origin.y + diameter;
    const auto openEntry = ImGui::IsItemDeactivated() && !storage->GetBool(movedKey, false) && onNumber
                           && !ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left);
    auto typedIn = false;
    if (labelHeight > 0.0f) {
        auto typed = static_cast<double>(*value);
        if (valueEntry(knobId, ImVec2(origin.x, origin.y + diameter), ImVec2(origin.x + width, origin.y + diameter + labelHeight),
                       typed, minimum, maximum, openEntry)) {
            *value = (style.flags & knobInteger) != 0 ? std::round(static_cast<float>(typed)) : static_cast<float>(typed);
            t = toNormalised(*value, minimum, maximum, logarithmic);
            typedIn = true;
        }
    }
    if (changed) {
        auto next = fromNormalised(t, minimum, maximum, logarithmic);
        if ((style.flags & knobInteger) != 0)
            next = std::round(next);
        changed = next != *value;
        *value = next;
        t = toNormalised(*value, minimum, maximum, logarithmic);
    }
    changed = changed || typedIn;

    auto* list = ImGui::GetWindowDrawList();
    const auto colour = style.colour != 0 ? style.colour : p.accent;
    const ImVec2 centre(origin.x + width * 0.5f, origin.y + diameter * 0.5f);
    const auto radius = diameter * 0.5f - dp(2.0f);
    const auto thickness = std::max(2.0f, diameter * 0.095f);
    arc(list, centre, radius, kArcStart, kArcStart + kArcSweep, p.bg5, thickness);
    const auto from = (style.flags & knobBipolar) != 0 ? kArcStart + kArcSweep * 0.5f : kArcStart;
    arc(list, centre, radius, from, kArcStart + kArcSweep * t, active || hovered ? brighter(colour, 0.15f) : colour, thickness);
    list->AddCircleFilled(centre, radius - thickness * 0.9f, hovered || active ? p.bg5 : p.bg4, 24);
    const auto angle = kArcStart + kArcSweep * t;
    const ImVec2 tip(centre.x + std::cos(angle) * (radius - thickness * 1.1f), centre.y + std::sin(angle) * (radius - thickness * 1.1f));
    const ImVec2 inner(centre.x + std::cos(angle) * radius * 0.25f, centre.y + std::sin(angle) * radius * 0.25f);
    list->AddLine(inner, tip, p.text, std::max(1.5f, thickness * 0.55f));
    if (style.modulation >= 0.0f) {
        const auto m = kArcStart + kArcSweep * std::clamp(style.modulation, 0.0f, 1.0f);
        list->AddCircleFilled(ImVec2(centre.x + std::cos(m) * radius, centre.y + std::sin(m) * radius), thickness * 0.7f, p.info, 12);
    }
    if (labelHeight > 0.0f) {
        const auto showValue = hovered || active;
        const auto text = showValue ? formatValue(*value, style) : std::string(style.label != nullptr ? style.label : "");
        ScopedFont font(nullptr, 0.82f);
        const auto room = width + dp(12.0f);
        const auto size = ImGui::CalcTextSize(text.c_str());
        clippedText(list, ImVec2(origin.x + (width - std::min(size.x, room)) * 0.5f, origin.y + diameter + dp(1.0f)), room,
                    showValue ? p.text : p.textDim, text.c_str());
    } else if (hovered || active) {
        ImGui::SetTooltip("%s", formatValue(*value, style).c_str());
    }
    ImGui::PopID();
    return changed;
}

float meterPosition(const float gainDb)
{
    if (gainDb <= -70.0f)
        return 0.0f;
    return std::clamp(std::exp((gainDb - 6.0f) / 26.0f), 0.0f, 1.0f);
}

bool fader(const char* id, float* gainDb, const float minimumDb, const float maximumDb, const ImVec2 size, const ImU32 colour)
{
    const auto& p = palette();
    auto& io = ImGui::GetIO();
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::PushID(id);
    ImGui::InvisibleButton("##fader", size);
    const auto hovered = ImGui::IsItemHovered();
    const auto active = ImGui::IsItemActive();
    auto changed = false;
    const auto travel = size.y - dp(14.0f);
    auto position = meterPosition(*gainDb);
    if (active && io.MouseDelta.y != 0.0f) {
        position = std::clamp(position - io.MouseDelta.y / travel * valueSpeed(), 0.0f, 1.0f);
        *gainDb = position <= 0.0f ? minimumDb : std::clamp(6.0f + 26.0f * std::log(position), minimumDb, maximumDb);
        changed = true;
    }
    if (hovered && io.MouseWheel != 0.0f) {
        takeWheel();
        *gainDb = std::clamp(*gainDb + io.MouseWheel * valueSpeed(), minimumDb, maximumDb);
        changed = true;
    }
    if (hovered && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left)) {
        *gainDb = std::clamp(0.0f, minimumDb, maximumDb);
        changed = true;
    }
    auto* list = ImGui::GetWindowDrawList();
    const auto centreX = origin.x + size.x * 0.5f;
    const auto top = origin.y + dp(7.0f);
    const auto bottom = top + travel;
    for (const auto tick : { 6.0f, 0.0f, -6.0f, -12.0f, -24.0f, -48.0f }) {
        const auto y = bottom - meterPosition(tick) * travel;
        list->AddLine(ImVec2(origin.x, y), ImVec2(origin.x + dp(4.0f), y), p.textFaint);
    }
    list->AddRectFilled(ImVec2(centreX - dp(2.0f), top), ImVec2(centreX + dp(2.0f), bottom), p.bg0, dp(2.0f));
    const auto handleY = bottom - meterPosition(*gainDb) * travel;
    const auto fill = colour != 0 ? colour : p.accent;
    list->AddRectFilled(ImVec2(centreX - dp(2.0f), handleY), ImVec2(centreX + dp(2.0f), bottom), alpha(fill, 0.55f), dp(2.0f));
    const auto handleWidth = std::min(size.x - dp(4.0f), dp(22.0f));
    const ImVec2 handleMin(centreX - handleWidth * 0.5f, handleY - dp(6.0f));
    const ImVec2 handleMax(centreX + handleWidth * 0.5f, handleY + dp(6.0f));
    list->AddRectFilled(handleMin, handleMax, hovered || active ? brighter(p.bg5, 0.25f) : p.bg5, dp(3.0f));
    list->AddLine(ImVec2(handleMin.x + dp(3.0f), handleY), ImVec2(handleMax.x - dp(3.0f), handleY), p.text, 1.5f);
    if (hovered || active)
        ImGui::SetTooltip("%.1f dB", *gainDb);
    ImGui::PopID();
    return changed;
}

void meter(const char* id, const ImVec2 size, const float peakLeft, const float peakRight, const float rmsLeft,
           const float rmsRight, const bool vertical)
{
    const auto& p = palette();
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::PushID(id);
    ImGui::Dummy(size);
    auto* storage = ImGui::GetStateStorage();
    const auto holdId = ImGui::GetID("hold");
    const auto holdTimeId = ImGui::GetID("holdTime");
    auto* list = ImGui::GetWindowDrawList();
    const auto now = static_cast<float>(ImGui::GetTime());
    const auto toDb = [](const float gain) { return gain > 1.0e-5f ? 20.0f * std::log10(gain) : -100.0f; };
    const auto peakDb = std::max(toDb(peakLeft), toDb(peakRight));
    auto hold = storage->GetFloat(holdId, -100.0f);
    auto holdTime = storage->GetFloat(holdTimeId, 0.0f);
    if (peakDb >= hold || now - holdTime > 1.5f) {
        hold = peakDb;
        holdTime = now;
    }
    storage->SetFloat(holdId, hold);
    storage->SetFloat(holdTimeId, holdTime);

    list->AddRectFilled(origin, ImVec2(origin.x + size.x, origin.y + size.y), p.bg0, dp(2.0f));
    const auto gap = dp(1.0f);
    const auto low = p.success;
    const auto midColour = p.warning;
    const auto high = p.error;
    const auto drawBar = [&](const int index, const float peak, const float rms) {
        const auto segment = [&](const float from, const float to, const ImU32 colour) {
            if (vertical) {
                const auto barWidth = (size.x - gap) * 0.5f;
                const auto x0 = origin.x + static_cast<float>(index) * (barWidth + gap);
                list->AddRectFilled(ImVec2(x0, origin.y + size.y * (1.0f - to)), ImVec2(x0 + barWidth, origin.y + size.y * (1.0f - from)),
                                    colour);
            } else {
                const auto barHeight = (size.y - gap) * 0.5f;
                const auto y0 = origin.y + static_cast<float>(index) * (barHeight + gap);
                list->AddRectFilled(ImVec2(origin.x + size.x * from, y0), ImVec2(origin.x + size.x * to, y0 + barHeight), colour);
            }
        };
        const auto mid = meterPosition(-12.0f);
        const auto top = meterPosition(-3.0f);
        const auto draw = [&](const float level, const float alphaValue) {
            segment(0.0f, std::min(level, mid), alpha(low, alphaValue));
            if (level > mid)
                segment(mid, std::min(level, top), alpha(midColour, alphaValue));
            if (level > top)
                segment(top, level, alpha(high, alphaValue));
        };
        draw(meterPosition(toDb(peak)), 0.45f);
        draw(meterPosition(toDb(rms)), 1.0f);
    };
    drawBar(0, peakLeft, rmsLeft);
    drawBar(1, peakRight, rmsRight);
    const auto holdPos = meterPosition(hold);
    if (holdPos > 0.0f) {
        const auto colour = alpha(hold > -0.1f ? high : p.text, 0.8f);
        if (vertical) {
            const auto y = origin.y + size.y * (1.0f - holdPos);
            list->AddLine(ImVec2(origin.x, y), ImVec2(origin.x + size.x, y), colour, 1.0f);
        } else {
            const auto x = origin.x + size.x * holdPos;
            list->AddLine(ImVec2(x, origin.y), ImVec2(x, origin.y + size.y), colour, 1.0f);
        }
    }
    ImGui::PopID();
}

bool dragValue(const char* id, float* value, const float speed, const float minimum, const float maximum, const char* format,
               const float width)
{
    if (width != 0.0f)
        ImGui::SetNextItemWidth(width);
    return ImGui::DragFloat(id, value, speed * valueSpeed(), minimum, maximum, format, ImGuiSliderFlags_AlwaysClamp);
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
    auto changed = false;
    auto& io = ImGui::GetIO();
    const auto range = maximum - minimum;
    if (active && !ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left) && range > 0.0f) {
        const auto t = std::clamp((io.MousePos.x - origin.x) / std::max(1.0f, width), 0.0f, 1.0f);
        const auto next = minimum + range * t;
        if (next != *value) {
            *value = next;
            changed = true;
        }
    }
    if (hovered && io.MouseWheel != 0.0f) {
        *value = std::clamp(*value + io.MouseWheel * range * 0.01f * valueSpeed(), minimum, maximum);
        changed = true;
        takeWheel();
    }
    auto typed = static_cast<double>(*value);
    if (valueEntry(itemId, origin, ImVec2(origin.x + width, origin.y + height), typed, minimum, maximum,
                   hovered && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left))) {
        *value = static_cast<float>(typed);
        changed = true;
    }
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = ImGui::GetStyle().FrameRounding;
    const ImVec2 max(origin.x + width, origin.y + height);
    list->AddRectFilled(origin, max, active ? p.bg5 : hovered ? p.bg4 : p.bg3, rounding);
    const auto t = range > 0.0f ? std::clamp((*value - minimum) / range, 0.0f, 1.0f) : 0.0f;
    if (t > 0.0f) {
        list->PushClipRect(origin, ImVec2(origin.x + width * t, max.y), true);
        list->AddRectFilled(origin, max, alpha(colour != 0 ? colour : p.accent, hovered || active ? 0.55f : 0.42f), rounding);
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
                  focused ? p.accent : p.textFaint);
    if (buffer[0] != 0) {
        const auto clearSize = height;
        ImGui::SetCursorScreenPos(ImVec2(origin.x + std::max(dp(40.0f), fieldWidth) - clearSize, origin.y));
        if (ImGui::InvisibleButton("##clear", ImVec2(clearSize, clearSize))) {
            buffer[0] = 0;
            changed = true;
        }
        const auto hovered = ImGui::IsItemHovered();
        drawCross(list, ImVec2(origin.x + std::max(dp(40.0f), fieldWidth) - clearSize * 0.5f, origin.y + height * 0.5f),
                  height * 0.26f, hovered ? p.text : p.textFaint);
        ImGui::SetCursorScreenPos(end);
    }
    ImGui::PopID();
    return changed;
}

bool textField(const char* id, char* buffer, const int bufferSize, const char* hint, const float width)
{
    if (width != 0.0f)
        ImGui::SetNextItemWidth(width);
    return ImGui::InputTextWithHint(id, hint != nullptr ? hint : "", buffer, static_cast<std::size_t>(bufferSize));
}

bool combo(const char* id, int* current, const char* const items[], const int count, const float width)
{
    if (width != 0.0f)
        ImGui::SetNextItemWidth(width);
    return ImGui::Combo(id, current, items, count);
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
        return ImGui::BeginChild(id, ImVec2(0.0f, height == 0.0f ? 0.0f : (height > 0.0f ? dp(height) : height)), flags);
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
                                        palette().lineStrong);
    ImGui::Dummy(ImVec2(dp(1.0f), height));
    ImGui::SameLine(0.0f, dp(6.0f));
}

void divider(const float spacing)
{
    ImGui::Dummy(ImVec2(0.0f, dp(spacing)));
    const auto origin = ImGui::GetCursorScreenPos();
    const auto width = ImGui::GetContentRegionAvail().x;
    ImGui::GetWindowDrawList()->AddLine(ImVec2(origin.x, origin.y), ImVec2(origin.x + width, origin.y), palette().line);
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
    list->AddRectFilled(origin, ImVec2(origin.x + box.x, origin.y + box.y), tone == Tone::neutral ? p.bg4 : alpha(colour, 0.16f),
                        rounding);
    list->AddRect(origin, ImVec2(origin.x + box.x, origin.y + box.y), alpha(colour, 0.35f), rounding);
    list->AddText(ImVec2(origin.x + pad.x, origin.y + pad.y), tone == Tone::neutral ? p.text : mix(colour, p.text, 0.3f), text);
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
    list->AddCircleFilled(centre, radius * 1.8f, alpha(colour, 0.18f), 16);
    list->AddCircleFilled(centre, radius, colour, 16);
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
    list->AddRectFilled(origin, max, p.bg3, rounding);
    if (fraction >= 0.0f) {
        const auto t = std::clamp(fraction, 0.0f, 1.0f);
        if (t > 0.0f)
            list->AddRectFilled(origin, ImVec2(origin.x + std::max(rounding * 2.0f, size.x * t), max.y), colour, rounding);
    } else {
        // Indeterminate: a segment sweeping across.
        const auto phase = static_cast<float>(std::fmod(ImGui::GetTime() * 0.8, 1.0));
        const auto segment = size.x * 0.3f;
        const auto x0 = origin.x - segment + (size.x + segment) * phase;
        list->PushClipRect(origin, max, true);
        list->AddRectFilled(ImVec2(x0, origin.y), ImVec2(x0 + segment, max.y), colour, rounding);
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
    const auto thickness = std::max(1.5f, r * 0.28f);
    list->PathClear();
    list->PathArcTo(centre, r - thickness * 0.5f, 0.0f, kPi * 2.0f, 24);
    list->PathStroke(alpha(palette().textFaint, 0.35f), 0, thickness);
    arc(list, centre, r - thickness * 0.5f, start, start + kPi * 1.3f, colour != 0 ? colour : palette().accent, thickness);
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
    list->AddRectFilled(origin, max, mix(p.bg2, colour, 0.10f), rounding);
    list->AddRectFilled(origin, ImVec2(origin.x + bar, max.y), colour, rounding, ImDrawFlags_RoundCornersLeft);
    list->AddText(nullptr, 0.0f, ImVec2(origin.x + bar + pad.x, origin.y + pad.y), p.text, text, nullptr, wrap);
    ImGui::Dummy(ImVec2(width, max.y - origin.y));
}

bool colourChip(const char* id, const ImU32 colour, const ImVec2 size)
{
    const auto origin = ImGui::GetCursorScreenPos();
    const auto clicked = ImGui::InvisibleButton(id, size);
    auto* list = ImGui::GetWindowDrawList();
    const ImVec2 max(origin.x + size.x, origin.y + size.y);
    list->AddRectFilled(origin, max, colour, dp(2.0f));
    if (ImGui::IsItemHovered())
        list->AddRect(origin, max, palette().text, dp(2.0f), 0, 1.5f);
    return clicked;
}

// ---- Notifications ---------------------------------------------------------------

namespace {
struct Toast {
    std::string text;
    Tone tone;
    double until;
    int id;
};
std::vector<Toast> gToasts;
int gToastIds {};
}  // namespace

void notify(const char* text, const Tone tone, const float seconds)
{
    if (text == nullptr)
        return;
    const auto now = ImGui::GetCurrentContext() != nullptr ? ImGui::GetTime() : 0.0;
    gToasts.push_back({ text, tone, now + seconds, ++gToastIds });
    if (gToasts.size() > 6)
        gToasts.erase(gToasts.begin());
}

void renderNotifications()
{
    const auto now = ImGui::GetTime();
    gToasts.erase(std::remove_if(gToasts.begin(), gToasts.end(), [now](const Toast& toast) { return toast.until < now; }),
                  gToasts.end());
    if (gToasts.empty())
        return;
    const auto& p = palette();
    const auto* viewport = ImGui::GetMainViewport();
    auto y = viewport->WorkPos.y + viewport->WorkSize.y - dp(16.0f);
    const auto x = viewport->WorkPos.x + viewport->WorkSize.x - dp(16.0f);
    for (auto i = static_cast<int>(gToasts.size()) - 1; i >= 0; --i) {
        auto& toast = gToasts[static_cast<std::size_t>(i)];
        const auto left = static_cast<float>(toast.until - now);
        const auto fade = std::clamp(left / 0.3f, 0.0f, 1.0f);
        ImGui::SetNextWindowPos(ImVec2(x, y), ImGuiCond_Always, ImVec2(1.0f, 1.0f));
        ImGui::SetNextWindowBgAlpha(0.98f * fade);
#ifdef IMGUI_HAS_DOCK
        ImGui::SetNextWindowViewport(viewport->ID);
#endif
        char name[48];
        std::snprintf(name, sizeof(name), "##cyuiToast%d", toast.id);
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
            ImGui::GetWindowDrawList()->AddRectFilled(window->Pos, ImVec2(window->Pos.x + dp(3.0f), window->Pos.y + window->Size.y), colour,
                                                      dp(metrics().roundingLarge + 2.0f), ImDrawFlags_RoundCornersLeft);
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
