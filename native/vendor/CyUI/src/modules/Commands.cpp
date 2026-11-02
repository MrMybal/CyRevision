#include "cyui/modules/Commands.h"
#include "ModuleInternal.h"
#include <sstream>

namespace cyui {
std::vector<std::size_t> rankCommands(ArrayView<Command> commands, const std::string &query) {
    std::vector<std::pair<int, std::size_t>> ranked;
    std::istringstream stream(detail::folded(query));
    std::vector<std::string> words;
    std::string word;
    while (stream >> word)
        words.push_back(word);
    for (std::size_t i = 0; i < commands.count; ++i) {
        const auto text =
            detail::folded(commands[i].label + " " + commands[i].category + " " + commands[i].shortcut);
        int score = 0;
        bool match = true;
        for (const auto &w : words) {
            const auto at = text.find(w);
            if (at == std::string::npos) {
                match = false;
                break;
            }
            score += int(std::min<std::size_t>(at, 10000));
        }
        if (match)
            ranked.push_back({score, i});
    }
    std::stable_sort(ranked.begin(), ranked.end(),
                     [](const auto &a, const auto &b) { return a.first < b.first; });
    std::vector<std::size_t> result;
    for (auto r : ranked)
        result.push_back(r.second);
    return result;
}
ItemId commandPalette(const char *id, bool &open, CommandState &s, ArrayView<Command> commands, ImVec2 size) {
    const auto title = trLabel(id);
    id = title.c_str();
    std::vector<Command> translated;
    translated.reserve(commands.count);
    for (std::size_t i = 0; i < commands.count; ++i) {
        auto command = commands[i];
        command.label = tr(command.label.c_str());
        command.category = tr(command.category.c_str());
        translated.push_back(std::move(command));
    }
    commands = {translated.data(), translated.size()};
    ItemId request = 0;
    if (open && !s.wasOpen) {
        ImGui::OpenPopup(id);
        s.query.clear();
        s.selected = 0;
    }
    s.wasOpen = open;
    if (!open) {
        if (ImGui::IsPopupOpen(id) && ImGui::BeginPopupModal(id, nullptr, ImGuiWindowFlags_NoResize)) {
            ImGui::CloseCurrentPopup();
            ImGui::EndPopup();
        }
        return 0;
    }
    if (size.x <= 0)
        size.x = dp(560);
    if (size.y <= 0)
        size.y = dp(360);
    ImGui::SetNextWindowSize(size, ImGuiCond_Appearing);
    if (ImGui::BeginPopupModal(id, &open, ImGuiWindowFlags_NoResize)) {
        if (ImGui::IsWindowAppearing())
            ImGui::SetKeyboardFocusHere();
        ImGui::SetNextItemWidth(-1);
        // CallbackHistory makes the field own Up/Down, so keyboard navigation does not move the
        // focus out of it; the list selection below reads them instead.
        if (detail::inputText("##query", s.query, ImGuiInputTextFlags_CallbackHistory))
            s.selected = 0;
        const auto rows = rankCommands(commands, s.query);
        s.selected = std::clamp(s.selected, 0, std::max(0, int(rows.size()) - 1));
        const bool moving = ImGui::IsKeyPressed(ImGuiKey_DownArrow) || ImGui::IsKeyPressed(ImGuiKey_UpArrow);
        if (ImGui::IsKeyPressed(ImGuiKey_DownArrow))
            s.selected = std::min(s.selected + 1, int(rows.size()) - 1);
        if (ImGui::IsKeyPressed(ImGuiKey_UpArrow))
            s.selected = std::max(0, s.selected - 1);
        const bool enter = ImGui::IsKeyPressed(ImGuiKey_Enter);
        if (enter && s.selected >= 0 && s.selected < int(rows.size())) {
            const auto &c = commands[rows[std::size_t(s.selected)]];
            if (c.enabled) {
                request = c.id;
                open = false;
            }
        }
        ImGui::BeginChild("##commands", {0, 0});
        ImGuiListClipper clip;
        const float height = ImGui::GetFrameHeightWithSpacing();
        clip.Begin(int(rows.size()), height);
        if (moving && s.selected >= 0)
            clip.IncludeItemByIndex(s.selected);
        while (clip.Step())
            for (int row = clip.DisplayStart; row < clip.DisplayEnd; ++row) {
                const auto &c = commands[rows[std::size_t(row)]];
                ItemScope key(c.id);
                ScopedDisabled disabled(!c.enabled);
                const auto p = ImGui::GetCursorScreenPos();
                const float width = ImGui::GetContentRegionAvail().x;
                bool pressed = false;
                {
                    // Rows are exactly `height` apart, matching the clipper and the scroll target.
                    ScopedVar tight(ImGuiStyleVar_ItemSpacing, ImVec2(ImGui::GetStyle().ItemSpacing.x, 0));
                    pressed = ImGui::Selectable("##command", row == s.selected, 0, {0, height});
                }
                if (pressed && c.enabled) {
                    request = c.id;
                    open = false;
                }
                clippedText(ImGui::GetWindowDrawList(), p, std::max(0.f, width - dp(120)), palette().text,
                            c.label.c_str());
                if (!c.shortcut.empty())
                    ImGui::GetWindowDrawList()->AddText({p.x + std::max(0.f, width - dp(110)), p.y},
                                                        palette().textFaint, c.shortcut.c_str());
                if (row == s.selected && moving)
                    ImGui::SetScrollHereY(.5f);
            }
        ImGui::EndChild();
        if (ImGui::IsKeyPressed(ImGuiKey_Escape))
            open = false;
        if (!open)
            ImGui::CloseCurrentPopup();
        ImGui::EndPopup();
    } else if (!ImGui::IsPopupOpen(id))
        open = false;
    return request;
}
} // namespace cyui
