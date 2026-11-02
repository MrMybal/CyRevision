#include "cyui/modules/Assets.h"
#include "ModuleInternal.h"

namespace cyui {
AssetResult assetBrowser(const char *id, AssetState &s, ArrayView<AssetItem> items, ImVec2 size) {
    ScopedID scope(id);
    AssetResult result;
    ImGui::SetNextItemWidth(-dp(140));
    result.queryChanged = detail::inputText("##search", s.query);
    ImGui::SameLine();
    ImGui::Checkbox(trLabel("Grid").c_str(), &s.grid);
    ImGui::SameLine();
    if (ghostButton(trLabel("Up").c_str())) {
        for (std::size_t i = 0; i < items.count; ++i)
            if (items[i].id == s.folder) {
                s.folder = items[i].parent;
                break;
            }
    }
    if (size.y > 0)
        size.y = std::max(dp(1), size.y - ImGui::GetFrameHeightWithSpacing());
    ImGui::BeginChild("##assets", size, ImGuiChildFlags_Borders);
    std::vector<std::size_t> filtered;
    const auto query = detail::folded(s.query); // folded once, matched without allocating per item
    for (std::size_t i = 0; i < items.count; ++i)
        if ((query.empty() ? items[i].parent == s.folder
                           : detail::matchesPair(items[i].label, items[i].type, query)))
            filtered.push_back(i);
    const float cell = s.grid ? dp(120) : ImGui::GetContentRegionAvail().x;
    const int columns = s.grid ? std::max(1, int(ImGui::GetContentRegionAvail().x / cell)) : 1;
    const float height = s.grid ? dp(116) : ImGui::GetFrameHeightWithSpacing();
    ImGuiListClipper clip;
    clip.Begin(int((filtered.size() + std::size_t(columns) - 1) / std::size_t(columns)), height);
    while (clip.Step())
        for (int row = clip.DisplayStart; row < clip.DisplayEnd; ++row) {
            for (int col = 0; col < columns; ++col) {
                const auto at = std::size_t(row) * columns + col;
                if (at >= filtered.size())
                    break;
                const auto &item = items[filtered[at]];
                ItemScope key(item.id);
                if (col)
                    ImGui::SameLine(0, 0);
                const auto p = ImGui::GetCursorScreenPos();
                detail::listRow("##asset", {cell, height});
                auto *d = ImGui::GetWindowDrawList();
                if (selected(s.selection, item.id))
                    d->AddRectFilled(p, {p.x + cell - dp(4), p.y + height - dp(4)}, palette().selection,
                                     dp(3));
                if (s.grid) {
                    if (item.thumbnail)
                        d->AddImage(item.thumbnail, {p.x + dp(8), p.y + dp(4)},
                                    {p.x + cell - dp(12), p.y + dp(78)});
                    else {
                        d->AddRectFilled({p.x + dp(8), p.y + dp(4)}, {p.x + cell - dp(12), p.y + dp(78)},
                                         palette().bg3, dp(3));
                        d->AddText({p.x + dp(20), p.y + dp(26)}, palette().textFaint,
                                   item.folder ? tr("Folder").c_str() : item.type.c_str());
                    }
                    clippedText(d, {p.x + dp(8), p.y + dp(84)}, cell - dp(16), palette().text,
                                item.label.c_str());
                } else
                    clippedText(d, {p.x + dp(6), p.y + dp(2)}, cell - dp(12), palette().text,
                                item.label.c_str());
                if (ImGui::IsItemClicked())
                    selectItem(s.selection, item.id, ImGui::GetIO().KeyCtrl || ImGui::GetIO().KeyShift);
                if (ImGui::IsItemHovered() && ImGui::IsMouseDoubleClicked(0)) {
                    if (item.folder) {
                        s.folder = item.id;
                        s.selection.clear();
                    } else {
                        result.id = item.id;
                        result.action = AssetAction::open;
                    }
                }
                if (ImGui::BeginDragDropSource()) {
                    ImGui::SetDragDropPayload("CYUI_ASSET", &item.id, sizeof(item.id));
                    ImGui::TextUnformatted(item.label.c_str());
                    ImGui::EndDragDropSource();
                }
                if (ImGui::BeginPopupContextItem()) {
                    result.id = item.id;
                    const std::pair<const char *, AssetAction> actions[] = {
                        {"Open", AssetAction::open},
                        {item.favorite ? "Remove favorite" : "Favorite", AssetAction::favorite},
                        {"Rename", AssetAction::rename},
                        {"Remove", AssetAction::remove}};
                    for (const auto &action : actions)
                        if (ImGui::MenuItem(trLabel(action.first).c_str()))
                            result.action = action.second;
                    ImGui::EndPopup();
                }
            }
        }
    ImGui::EndChild();
    return result;
}
} // namespace cyui
