#include "cyui/modules/Diagnostics.h"
#include "ModuleInternal.h"
#include <atomic>

namespace cyui {
void LogBuffer::append(LogLevel level, const std::string &text, const std::string &source) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::size_t start = 0;
    do {
        const auto end = text.find('\n', start);
        std::string line = text.substr(start, end == std::string::npos ? end : end - start);
        if (line.size() > maxBytes_) {
            std::size_t endByte = maxBytes_;
            while (endByte && (static_cast<unsigned char>(line[endByte]) & 0xc0) == 0x80)
                --endByte;
            line.resize(endByte);
        }
        std::size_t sourceEnd = std::min(source.size(), maxBytes_ - line.size());
        while (sourceEnd < source.size() && sourceEnd &&
               (static_cast<unsigned char>(source[sourceEnd]) & 0xc0) == 0x80)
            --sourceEnd;
        std::string src = source.substr(0, sourceEnd);
        bytes_ += line.size() + src.size();
        rows_.push_back({next_++, level, std::move(line), std::move(src)});
        while (rows_.size() > capacity_ || bytes_ > maxBytes_) {
            bytes_ -= rows_.front().text.size() + rows_.front().source.size();
            rows_.pop_front();
        }
        if (end == std::string::npos)
            break;
        start = end + 1;
    } while (start < text.size());
    ++revision_;
}
std::uint64_t LogBuffer::firstRevision() {
    static std::atomic<std::uint64_t> instances{0};
    return ((instances.fetch_add(1) + 1) << 40) | 1;
}
void LogBuffer::clear() {
    std::lock_guard<std::mutex> lock(mutex_);
    rows_.clear();
    bytes_ = 0;
    ++revision_;
}
std::uint64_t LogBuffer::snapshot(std::vector<LogEntry> &out, std::uint64_t known) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (known == revision_)
        return revision_;
    if ((known >> 40) != (revision_ >> 40)) { // never read from this buffer
        out.assign(rows_.begin(), rows_.end());
        return revision_;
    }
    // Rows are immutable and their IDs consecutive, so a previous copy only loses rows at the front
    // and gains rows at the back: trim and append instead of copying everything again.
    std::size_t drop = 0;
    const ItemId first = rows_.empty() ? next_ : rows_.front().id;
    while (drop < out.size() && out[drop].id < first)
        ++drop;
    const std::size_t kept = out.size() - drop;
    const bool reuse = kept && kept <= rows_.size() && out[drop].id == first &&
                       out.back().id == rows_[kept - 1].id && out.back().text == rows_[kept - 1].text;
    if (reuse) {
        out.erase(out.begin(), out.begin() + std::ptrdiff_t(drop));
        out.insert(out.end(), rows_.begin() + std::ptrdiff_t(kept), rows_.end());
    } else
        out.assign(rows_.begin(), rows_.end());
    return revision_;
}
namespace {
ImU32 logColor(LogLevel level) {
    const auto &p = palette();
    return level == LogLevel::error     ? p.error
           : level == LogLevel::warning ? p.warning
           : level == LogLevel::debug   ? p.textFaint
                                        : p.text;
}
} // namespace
ItemId logView(const char *id, LogState &s, const LogBuffer &buffer, ImVec2 size) {
    ScopedID scope(id);
    const auto previous = s.revision;
    s.revision = buffer.snapshot(s.rows, s.revision);
    ImGui::SetNextItemWidth(-dp(90));
    detail::inputText("##filter", s.query);
    ImGui::SameLine();
    ImGui::Checkbox(trLabel("Follow").c_str(), &s.follow);
    ItemId request = 0;
    ScopedFont font(monoFont());
    ImGui::BeginChild("##log", size, ImGuiChildFlags_Borders, ImGuiWindowFlags_HorizontalScrollbar);
    const bool atBottom = ImGui::GetScrollY() >= ImGui::GetScrollMaxY() - dp(3);
    // No query and every level shown: rows are listed directly. Otherwise the matching indices are
    // cached until the rows, the query or the level mask change.
    const bool everything = s.query.empty() && (s.levels & 15u) == 15u;
    if (!everything &&
        (s.filterRevision != s.revision || s.filterQuery != s.query || s.filterLevels != s.levels)) {
        const auto query = detail::folded(s.query);
        s.filtered.clear();
        for (std::size_t i = 0; i < s.rows.size(); ++i)
            if ((s.levels & (1u << unsigned(s.rows[i].level))) &&
                (query.empty() || detail::matchesPair(s.rows[i].text, s.rows[i].source, query)))
                s.filtered.push_back(i);
        s.filterRevision = s.revision;
        s.filterQuery = s.query;
        s.filterLevels = s.levels;
    }
    const std::size_t count = everything ? s.rows.size() : s.filtered.size();
    ImGuiListClipper clip;
    clip.Begin(int(count));
    while (clip.Step())
        for (int i = clip.DisplayStart; i < clip.DisplayEnd; ++i) {
            const auto &row = s.rows[everything ? std::size_t(i) : s.filtered[std::size_t(i)]];
            ItemScope key(row.id);
            ScopedColor color(ImGuiCol_Text, logColor(row.level));
            const auto pos = ImGui::GetCursorScreenPos();
            const float width =
                std::max(ImGui::GetContentRegionAvail().x, ImGui::CalcTextSize(row.text.c_str()).x);
            const bool clicked = ImGui::Selectable("##row", s.selected == row.id, 0,
                                                   {width, ImGui::GetTextLineHeightWithSpacing()});
            ImGui::GetWindowDrawList()->AddText(pos, logColor(row.level), row.text.c_str());
            if (clicked) {
                s.selected = row.id;
                request = row.id;
            }
            if (ImGui::BeginPopupContextItem()) {
                if (ImGui::MenuItem(trLabel("Copy").c_str()))
                    ImGui::SetClipboardText(row.text.c_str());
                ImGui::EndPopup();
            }
        }
    if (s.follow && atBottom && previous != s.revision)
        ImGui::SetScrollHereY(1);
    ImGui::EndChild();
    return request;
}
ItemId diagnosticList(const char *id, ArrayView<Diagnostic> rows, ImVec2 size) {
    ItemId request = 0;
    if (ImGui::BeginTable(id, 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_Resizable | ImGuiTableFlags_ScrollY,
                          size)) {
        ImGui::TableSetupColumn(trLabel("Source").c_str());
        ImGui::TableSetupColumn(trLabel("Position").c_str());
        ImGui::TableSetupColumn(trLabel("Message").c_str());
        ImGui::TableHeadersRow();
        ImGuiListClipper clip;
        clip.Begin(int(rows.count));
        while (clip.Step())
            for (int i = clip.DisplayStart; i < clip.DisplayEnd; ++i) {
                const auto &row = rows[std::size_t(i)];
                ItemScope key(row.id);
                ImGui::TableNextRow();
                ImGui::TableNextColumn();
                const auto pos = ImGui::GetCursorScreenPos();
                if (ImGui::Selectable("##source", false, ImGuiSelectableFlags_SpanAllColumns))
                    request = row.id;
                ImGui::GetWindowDrawList()->AddText(pos, palette().text, row.source.c_str());
                ImGui::TableNextColumn();
                ImGui::Text(tr("%d:%d").c_str(), row.line, row.column);
                ImGui::TableNextColumn();
                textColoured(logColor(row.level), tr("%s").c_str(), row.message.c_str());
            }
        ImGui::EndTable();
    }
    return request;
}
} // namespace cyui
