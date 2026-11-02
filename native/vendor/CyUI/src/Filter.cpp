#include "cyui/Filter.h"
#include "cyui/Core.h"
#include <imgui_internal.h>
#include <algorithm>
#include <sstream>

namespace cyui {
std::string foldFilterText(std::string_view text) {
    std::string out;
    out.reserve(text.size());
    const auto *cursor = text.data();
    if (text.empty())
        return out;
    const auto *end = cursor + text.size();
    while (cursor < end) {
        unsigned int cp = 0;
        const int used = ImTextCharFromUtf8(&cp, cursor, end);
        cursor += std::max(1, used);
        if (cp >= 0x300 && cp <= 0x36f)
            continue;
        if (cp == 0xa0 || (cp >= 0x2000 && cp <= 0x200a))
            cp = ' ';
        const char *folded = nullptr;
        if ((cp >= 0xc0 && cp <= 0xc5) || (cp >= 0xe0 && cp <= 0xe5) || (cp >= 0x100 && cp <= 0x105))
            folded = "a";
        else if (cp == 0xc7 || cp == 0xe7 || (cp >= 0x106 && cp <= 0x10d))
            folded = "c";
        else if (cp >= 0x10e && cp <= 0x111)
            folded = "d";
        else if ((cp >= 0xc8 && cp <= 0xcb) || (cp >= 0xe8 && cp <= 0xeb) || (cp >= 0x112 && cp <= 0x11b))
            folded = "e";
        else if (cp >= 0x11c && cp <= 0x123)
            folded = "g";
        else if (cp >= 0x124 && cp <= 0x127)
            folded = "h";
        else if ((cp >= 0xcc && cp <= 0xcf) || (cp >= 0xec && cp <= 0xef) || (cp >= 0x128 && cp <= 0x131))
            folded = "i";
        else if (cp >= 0x134 && cp <= 0x135)
            folded = "j";
        else if (cp >= 0x136 && cp <= 0x138)
            folded = "k";
        else if (cp >= 0x139 && cp <= 0x142)
            folded = "l";
        else if (cp == 0xd1 || cp == 0xf1 || (cp >= 0x143 && cp <= 0x14b))
            folded = "n";
        else if ((cp >= 0xd2 && cp <= 0xd6) || (cp >= 0xf2 && cp <= 0xf6) || cp == 0xd8 || cp == 0xf8 ||
                 (cp >= 0x14c && cp <= 0x151))
            folded = "o";
        else if (cp >= 0x154 && cp <= 0x159)
            folded = "r";
        else if ((cp >= 0x15a && cp <= 0x161) || cp == 0x17f)
            folded = "s";
        else if (cp >= 0x162 && cp <= 0x167)
            folded = "t";
        else if ((cp >= 0xd9 && cp <= 0xdc) || (cp >= 0xf9 && cp <= 0xfc) || (cp >= 0x168 && cp <= 0x173))
            folded = "u";
        else if (cp >= 0x174 && cp <= 0x175)
            folded = "w";
        else if (cp == 0xdd || cp == 0xfd || cp == 0xff || (cp >= 0x176 && cp <= 0x178))
            folded = "y";
        else if (cp >= 0x179 && cp <= 0x17e)
            folded = "z";
        else if (cp == 0xc6 || cp == 0xe6)
            folded = "ae";
        else if (cp == 0x152 || cp == 0x153)
            folded = "oe";
        else if (cp == 0xdf || cp == 0x1e9e)
            folded = "ss";
        else if (cp == 0x132 || cp == 0x133)
            folded = "ij";
        if (folded)
            out += folded;
        else {
            if (cp >= 'A' && cp <= 'Z')
                cp += 'a' - 'A';
            char bytes[5]{};
            ImTextCharToUtf8(bytes, cp);
            out += bytes;
        }
    }
    return out;
}
void TextFilter::compile() const {
    if (compiled_ == query)
        return;
    compiled_ = query;
    words_.clear();
    std::istringstream stream(foldFilterText(query));
    for (std::string word; stream >> word;)
        words_.push_back(std::move(word));
}
bool TextFilter::active() const {
    compile();
    return !words_.empty();
}
void TextFilter::clear() {
    query.clear();
    compiled_.clear();
    words_.clear();
}
bool TextFilter::matches(std::string_view label, std::string_view keywords) const {
    compile();
    if (words_.empty())
        return true;
    const std::string visible(label.substr(0, label.find("##")));
    const auto text = foldFilterText(visible + " " + tr(visible.c_str()) + " " + std::string(keywords));
    return std::all_of(words_.begin(), words_.end(),
                       [&](const auto &word) { return text.find(word) != std::string::npos; });
}
namespace {
int resizeFilter(ImGuiInputTextCallbackData *data) {
    if (data->EventFlag == ImGuiInputTextFlags_CallbackResize) {
        auto &text = *static_cast<std::string *>(data->UserData);
        text.resize(std::size_t(data->BufTextLen));
        data->Buf = text.data();
    }
    return 0;
}
std::string visibleLabel(std::string_view label) {
    return std::string(label.substr(0, label.find("##")));
}
std::string contextText(const std::string &context) {
    const auto visible = visibleLabel(context);
    return visible + " " + tr(visible.c_str());
}
void menuMatches(const std::vector<FilterMenuItem> &entries, const TextFilter &filter,
                 const std::string &aliases, const std::string &path, bool enabled,
                 std::vector<FilterMenuMatch> &out) {
    for (const auto &entry : entries) {
        const auto keywords = aliases + " " + entry.keywords;
        const auto label = tr(visibleLabel(entry.label).c_str());
        if (!entry.children.empty()) {
            const auto next = path.empty() ? label : path + " / " + label;
            menuMatches(entry.children, filter, keywords + " " + contextText(entry.label), next,
                        enabled && entry.enabled, out);
        } else if (entry.id && filter.matches(entry.label, keywords))
            out.push_back({entry.id, label, entry.shortcut, path, enabled && entry.enabled, entry.checked});
    }
}
std::uint64_t drawMenu(const std::vector<FilterMenuItem> &entries) {
    std::uint64_t request = 0;
    for (std::size_t n = 0; n < entries.size(); ++n) {
        const auto &entry = entries[n];
        ScopedID scope{int(n)};
        if (!entry.children.empty()) {
            if (ImGui::BeginMenu(trLabel(entry.label.c_str()).c_str(), entry.enabled)) {
                const auto child = drawMenu(entry.children);
                if (child)
                    request = child;
                ImGui::EndMenu();
            }
        } else if (entry.id) {
            if (ImGui::MenuItem(trLabel(entry.label.c_str()).c_str(), entry.shortcut.c_str(), entry.checked,
                                entry.enabled))
                request = entry.id;
        } else if (entry.label.empty())
            ImGui::Separator();
    }
    return request;
}
} // namespace
bool filterBox(const char *id, TextFilter &filter, const FilterOptions &options) {
    ScopedID scope(id);
    const auto before = filter.query;
    const bool appearing = ImGui::IsWindowAppearing();
    if (appearing && options.resetOnAppearing)
        filter.clear();
    const auto &io = ImGui::GetIO();
    // Dear ImGui >= 1.92 normalizes macOS Command to KeyCtrl at input ingestion.
    const bool modifier = io.KeyCtrl;
    const bool shortcut = options.shortcut && modifier && !io.KeyShift && !io.KeyAlt && !io.WantTextInput &&
                          ImGui::IsWindowFocused(ImGuiFocusedFlags_ChildWindows) &&
                          ImGui::IsKeyPressed(ImGuiKey_F, false);
    if (filter.focusNext_ || shortcut || (appearing && options.focusOnAppearing))
        ImGui::SetKeyboardFocusHere();
    filter.focusNext_ = false;
    const auto height = ImGui::GetFrameHeight();
    const float width = options.width > 0 ? options.width : ImGui::GetContentRegionAvail().x + options.width;
    const float clear = height + ImGui::GetStyle().ItemSpacing.x;
    ImGui::SetNextItemWidth(std::max(dp(32), width - clear));
    ImGui::InputTextWithHint("##query", tr(options.hint).c_str(), filter.query.data(),
                             filter.query.capacity() + 1,
                             ImGuiInputTextFlags_CallbackResize | ImGuiInputTextFlags_EscapeClearsAll,
                             resizeFilter, &filter.query);
    ImGui::SameLine();
    {
        ScopedDisabled disabled(filter.query.empty());
        if (ghostButton("x##clear", {height, height})) {
            filter.clear();
            filter.focus();
        }
    }
    tooltip("Clear filter (Escape)");
    return before != filter.query;
}
bool beginFilteredSection(const char *label, const TextFilter &filter, bool hasMatchingChildren,
                          bool defaultOpen) {
    const auto title = trLabel(label);
    if (!filter.active())
        return beginSection(title.c_str(), defaultOpen);
    if (!hasMatchingChildren && !filter.matches(label))
        return false;
    auto *storage = ImGui::GetStateStorage();
    const auto id = ImGui::GetID(title.c_str());
    const auto previous = storage->GetInt(id, defaultOpen ? 1 : 0);
    ImGui::SetNextItemOpen(true, ImGuiCond_Always);
    const auto shown = beginSection(title.c_str(), defaultOpen);
    storage->SetInt(id, previous);
    return shown;
}
std::vector<FilterMenuMatch> filterMenuMatches(const std::vector<FilterMenuItem> &entries,
                                               const TextFilter &filter) {
    std::vector<FilterMenuMatch> out;
    menuMatches(entries, filter, {}, {}, true, out);
    return out;
}
std::uint64_t filterMenu(const char *id, TextFilter &filter, const std::vector<FilterMenuItem> &entries,
                         const FilterMenuOptions &options) {
    ScopedID scope(id);
    FilterOptions field;
    field.hint = "Filter menu...";
    field.width = dp(options.width);
    field.focusOnAppearing = options.focusOnOpen;
    field.resetOnAppearing = options.resetOnOpen;
    filterBox("##filter", filter, field);
    ImGui::Separator();
    if (!filter.active())
        return drawMenu(entries);
    const auto matches = filterMenuMatches(entries, filter);
    if (matches.empty())
        textDim("%s", tr("No matching items").c_str());
    std::string path;
    std::uint64_t request = 0;
    for (const auto &match : matches) {
        if (match.breadcrumb != path) {
            path = match.breadcrumb;
            if (!path.empty())
                sectionTitle(path.c_str());
        }
        ScopedID high{int(match.id >> 32)}, low{int(match.id & 0xffffffffu)};
        if (ImGui::MenuItem((match.label + "###action").c_str(), match.shortcut.c_str(), match.checked,
                            match.enabled))
            request = match.id;
    }
    return request;
}
} // namespace cyui
