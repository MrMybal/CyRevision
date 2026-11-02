#include "cyui/Layout.h"
#include "cyui/Locale.h"
#include "cyui/Filter.h"

#include "cyui/Icons.h"

#include <imgui_internal.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>

namespace cyui {
namespace {

constexpr ImGuiWindowFlags kBarFlags = ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoSavedSettings
                                       | ImGuiWindowFlags_NoScrollWithMouse;

// A one-pixel line at 96 dpi, whole pixels above (as ImGui scales borders).
float hairline()
{
    return std::max(1.0f, std::floor(dp(1.0f)));
}

bool sideBar(const char* id, const ImGuiDir dir, const float size, const ImU32 ground, const ImVec2 padding)
{
    // The bar's own look, popped once its window began: popups and tooltips
    // opened from the bar keep the theme's.
    bool shown = false;
    {
        ScopedColor colour(ImGuiCol_WindowBg, ground);
        ScopedVar vars(ImGuiStyleVar_WindowPadding, padding);
        vars.push(ImGuiStyleVar_WindowBorderSize, 0.0f);
        shown = ImGui::BeginViewportSideBar(id, ImGui::GetMainViewport(), dir, size, kBarFlags);
    }
    // The seam between the bar and the rest.
    const auto* window = ImGui::GetCurrentWindow();
    const auto colour = ImGui::GetColorU32(palette().lineStrong);
    const auto t = hairline();
    auto* list = ImGui::GetWindowDrawList();
    const auto min = window->Pos;
    const ImVec2 max(window->Pos.x + window->Size.x, window->Pos.y + window->Size.y);
    if (dir == ImGuiDir_Up)
        list->AddLine(ImVec2(min.x, max.y - t * 0.5f), ImVec2(max.x, max.y - t * 0.5f), colour, t);
    else if (dir == ImGuiDir_Down)
        list->AddLine(ImVec2(min.x, min.y + t * 0.5f), ImVec2(max.x, min.y + t * 0.5f), colour, t);
    else if (dir == ImGuiDir_Left)
        list->AddLine(ImVec2(max.x - t * 0.5f, min.y), ImVec2(max.x - t * 0.5f, max.y), colour, t);
    return shown;
}

void endSideBar()
{
    ImGui::End();
}

}  // namespace

bool beginTopBar(const char* id, float height)
{
    if (height <= 0.0f)
        height = ImGui::GetFrameHeight() + dp(metrics().toolbarHeight);
    const auto padY = std::max(0.0f, (height - ImGui::GetFrameHeight()) * 0.5f);
    return sideBar(id, ImGuiDir_Up, height, palette().bg1, ImVec2(dp(8.0f), padY));
}

void endTopBar()
{
    endSideBar();
}

bool beginStatusBar(const char* id)
{
    const auto height = ImGui::GetFontSize() * 0.86f + dp(10.0f);
    return sideBar(id, ImGuiDir_Down, height, palette().bg0, dp(8.0f, 4.0f));
}

void endStatusBar()
{
    endSideBar();
}

void statusText(const Tone tone, const char* fmt, ...)
{
    const auto& p = palette();
    ScopedFont font(nullptr, 0.86f);
    const auto colour = tone == Tone::neutral ? p.textFaint : toneColour(tone);
    va_list args;
    va_start(args, fmt);
    ImGui::TextColoredV(vec(colour), fmt, args);
    va_end(args);
}

bool beginSidebar(const char* id, const float width)
{
    return sideBar(id, ImGuiDir_Left, dp(width), palette().bg0, dp(10.0f, 12.0f));
}

void endSidebar()
{
    endSideBar();
}

void sidebarHeader(const char* title, const char* badgeText)
{
    {
        ScopedFont font(boldFont(), 1.0f);
        ScopedColor colour(ImGuiCol_Text, palette().text);
        ImGui::TextUnformatted(title);
    }
    if (badgeText != nullptr) {
        ImGui::SameLine(0.0f, dp(6.0f));
        badge(badgeText, Tone::warning);
    }
    space(10.0f);
}

void sidebarGroup(const char* label)
{
    space(6.0f);
    ImGui::Indent(dp(4.0f));
    overline(label, palette().textFaint);
    ImGui::Unindent(dp(4.0f));
    space(2.0f);
}

bool sidebarItem(const char* iconText, const char* label, const bool active, const char* badgeText)
{
    const auto& p = palette();
    const auto width = ImGui::GetContentRegionAvail().x;
    const auto height = ImGui::GetFrameHeight() + dp(8.0f);
    const auto origin = ImGui::GetCursorScreenPos();
    ImGui::PushID(label);
    const auto clicked = ImGui::InvisibleButton("##item", ImVec2(width, height));
    const auto hovered = ImGui::IsItemHovered();
    ImGui::PopID();
    auto* list = ImGui::GetWindowDrawList();
    const auto rounding = ImGui::GetStyle().FrameRounding + dp(2.0f);
    const ImVec2 max(origin.x + width, origin.y + height);
    if (active) {
        list->AddRectFilled(origin, max, ImGui::GetColorU32(alpha(p.accent, 0.16f)), rounding);
        list->AddRect(origin, max, ImGui::GetColorU32(alpha(p.accent, 0.45f)), rounding, 0, hairline());
    } else if (hovered) {
        list->AddRectFilled(origin, max, ImGui::GetColorU32(p.bg3), rounding);
    }
    const auto textColour = active ? p.text : (hovered ? p.text : p.textDim);
    auto x = origin.x + dp(10.0f);
    const auto y = origin.y + (height - ImGui::GetFontSize()) * 0.5f;
    if (iconText != nullptr && iconsAvailable()) {
        list->AddText(ImVec2(x, y), ImGui::GetColorU32(active ? p.accent : textColour), iconText);
        x += ImGui::GetFontSize() + dp(10.0f);
    }
    auto room = max.x - x - dp(8.0f);
    const std::string visible(label, ImGui::FindRenderedTextEnd(label));
    if (badgeText != nullptr) {
        ScopedFont font(boldFont(), 0.74f);
        const auto size = ImGui::CalcTextSize(badgeText);
        const ImVec2 bMax(max.x - dp(8.0f), origin.y + (height + size.y) * 0.5f + dp(2.0f));
        const ImVec2 bMin(bMax.x - size.x - dp(10.0f), bMax.y - size.y - dp(4.0f));
        list->AddRectFilled(bMin, bMax, ImGui::GetColorU32(active ? p.accent : p.bg4), height);
        list->AddText(ImVec2(bMin.x + dp(5.0f), bMin.y + dp(2.0f)), ImGui::GetColorU32(active ? p.accentText : p.textDim),
                      badgeText);
        room = bMin.x - x - dp(6.0f);
    }
    if (active) {
        ScopedFont font(boldFont(), 1.0f);
        clippedText(list, ImVec2(x, y), room, textColour, visible.c_str());
    } else {
        clippedText(list, ImVec2(x, y), room, textColour, visible.c_str());
    }
    ImGui::Dummy(ImVec2(0.0f, dp(1.0f)));
    return clicked;
}

void panelHeader(const char* title, const char* subtitle)
{
    heading(title, 2);
    if (subtitle != nullptr) {
        ScopedFont font(nullptr, 0.88f);
        textFaint("%s", subtitle);
    }
}

void panelHeader(const char* title, const char* subtitle, TextFilter* filter)
{
    panelHeader(title, subtitle);
    if (filter) filterBox("##panelFilter", *filter);
}

bool beginToolbar(const char* id)
{
    const auto& p = palette();
    const auto pad = dp(6.0f, 4.0f);
    ScopedColor colour(ImGuiCol_ChildBg, p.bg2);
    ScopedVar vars(ImGuiStyleVar_WindowPadding, pad);
    vars.push(ImGuiStyleVar_ChildRounding, 0.0f).push(ImGuiStyleVar_ChildBorderSize, 0.0f);
    const auto shown = ImGui::BeginChild(id, ImVec2(0.0f, ImGui::GetFrameHeight() + pad.y * 2.0f),
                                         ImGuiChildFlags_AlwaysUseWindowPadding,
                                         ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    const auto* window = ImGui::GetCurrentWindow();
    const auto t = hairline();
    window->DrawList->AddLine(ImVec2(window->Pos.x, window->Pos.y + window->Size.y - t * 0.5f),
                              ImVec2(window->Pos.x + window->Size.x, window->Pos.y + window->Size.y - t * 0.5f),
                              ImGui::GetColorU32(p.line), t);
    return shown;
}

void endToolbar()
{
    ImGui::EndChild();
}

namespace {
// The open flag of a modal lives in the modal window's own storage: nested
// modals and several contexts each keep theirs.
const ImGuiID kModalOpenKey = ImHashStr("cyui.modal.open");
}

bool beginModal(const char* title, bool* open, const float width)
{
    if (open != nullptr && *open && !ImGui::IsPopupOpen(title))
        ImGui::OpenPopup(title);
    const auto* viewport = ImGui::GetMainViewport();
    ImGui::SetNextWindowPos(viewport->GetCenter(), ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
    ImGui::SetNextWindowSize(ImVec2(dp(width), 0.0f), ImGuiCond_Always);
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, dp(18.0f, 16.0f));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, dp(metrics().roundingLarge + 4.0f));
    ImGui::PushStyleColor(ImGuiCol_PopupBg, palette().bg2);
    ImGui::PushStyleColor(ImGuiCol_Border, palette().lineStrong);
    const auto shown = ImGui::BeginPopupModal(title, nullptr, ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoSavedSettings);
    ImGui::PopStyleColor(2);
    ImGui::PopStyleVar(2);
    if (!shown) {
        if (open != nullptr)
            *open = false;
        return false;
    }
    ImGui::GetCurrentWindow()->StateStorage.SetVoidPtr(kModalOpenKey, open);
    const auto* end = ImGui::FindRenderedTextEnd(title);
    {
        ScopedFont font(boldFont(), 1.15f);
        ImGui::TextUnformatted(title, end);
    }
    ImGui::SameLine();
    alignRight(ImGui::GetFrameHeight());
    if (iconButton(iconsAvailable() ? icon::close : "x", nullptr)) {
        ImGui::CloseCurrentPopup();
        if (open != nullptr)
            *open = false;
    }
    space(6.0f);
    return true;
}

void endModal()
{
    ImGui::EndPopup();
}

int modalButtons(const char* confirm, const char* cancel, const bool danger)
{
    const auto translatedConfirm = trLabel(confirm);
    confirm = translatedConfirm.c_str();
    const auto translatedCancel = cancel ? trLabel(cancel) : std::string{};
    if (cancel) cancel = translatedCancel.c_str();
    space(10.0f);
    const auto& style = ImGui::GetStyle();
    const auto confirmWidth = std::max(dp(84.0f), buttonWidth(confirm));
    const auto cancelWidth = cancel != nullptr ? std::max(dp(84.0f), buttonWidth(cancel)) : 0.0f;
    alignRight(confirmWidth + (cancel != nullptr ? cancelWidth + style.ItemSpacing.x : 0.0f));
    auto result = 0;
    if (cancel != nullptr) {
        if (button(cancel, Variant::secondary, ImVec2(cancelWidth, 0.0f)) || ImGui::IsKeyPressed(ImGuiKey_Escape, false))
            result = 2;
        ImGui::SameLine();
    }
    if (button(confirm, danger ? Variant::danger : Variant::primary, ImVec2(confirmWidth, 0.0f))
        || (ImGui::IsKeyPressed(ImGuiKey_Enter, false) && !ImGui::IsAnyItemActive()))
        result = 1;
    if (result != 0) {
        ImGui::CloseCurrentPopup();
        if (auto* open = static_cast<bool*>(ImGui::GetCurrentWindow()->RootWindow->StateStorage.GetVoidPtr(kModalOpenKey)))
            *open = false;
    }
    return result;
}

void alignRight(const float width)
{
    const auto right = ImGui::GetCursorPosX() + ImGui::GetContentRegionAvail().x;
    ImGui::SetCursorPosX(std::max(ImGui::GetCursorPosX(), right - width));
}

float buttonWidth(const char* label)
{
    return ImGui::CalcTextSize(label, nullptr, true).x + ImGui::GetStyle().FramePadding.x * 2.0f;
}

#ifdef IMGUI_HAS_DOCK
ImGuiID dockSpace(const char* id, const ImGuiDockNodeFlags flags)
{
    return ImGui::DockSpaceOverViewport(ImGui::GetID(id), ImGui::GetMainViewport(), flags);
}
#endif

}  // namespace cyui
