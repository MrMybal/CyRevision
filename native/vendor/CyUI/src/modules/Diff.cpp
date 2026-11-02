#include "cyui/modules/Diff.h"
#include "ModuleInternal.h"
#include <cstdio>
#include <regex>
#include <charconv>
#include <stdexcept>
#include <unordered_map>

namespace cyui {
namespace {
// A trailing '\r' is cut so CRLF and LF lines compare equal; `cr` records which lines had one.
std::vector<std::string> linesOf(const std::string &text, std::vector<char> *cr = nullptr) {
    std::vector<std::string> out;
    std::size_t a = 0;
    while (a < text.size()) {
        const auto b = text.find('\n', a);
        out.push_back(text.substr(a, b == std::string::npos ? b : b - a));
        const bool carriage = !out.back().empty() && out.back().back() == '\r';
        if (carriage)
            out.back().pop_back();
        if (cr)
            cr->push_back(carriage);
        if (b == std::string::npos)
            break;
        a = b + 1;
    }
    return out;
}
bool endsWithNewline(const std::string &text) {
    return !text.empty() && text.back() == '\n';
}
DiffRow rowOf(DiffKind kind, const std::string &text, int oldLine, int newLine) {
    DiffRow row;
    row.kind = kind;
    row.oldLine = oldLine;
    row.newLine = newLine;
    row.code.text = text;
    return row;
}
} // namespace
void DiffDocument::setTexts(const std::string &before, const std::string &after, CodeLanguage language,
                            std::size_t budget) {
    if (before.size() > 32 * 1024 * 1024 || after.size() > 32 * 1024 * 1024)
        throw std::length_error("DiffDocument exceeds 32 MiB per input");
    rows_.clear();
    approximate_ = false;
    std::vector<char> crA, crB;
    const auto a = linesOf(before, &crA), b = linesOf(after, &crB);
    std::unordered_map<std::string, std::vector<std::size_t>> left, right;
    for (std::size_t i = 0; i < a.size(); ++i)
        left[a[i]].push_back(i);
    for (std::size_t i = 0; i < b.size(); ++i)
        right[b[i]].push_back(i);
    std::vector<std::pair<std::size_t, std::size_t>> candidates;
    for (std::size_t i = 0; i < a.size(); ++i)
        if (left[a[i]].size() == 1 && right[a[i]].size() == 1)
            candidates.push_back({i, right[a[i]][0]});
    std::vector<std::size_t> tails, previous(candidates.size(), std::size_t(-1));
    for (std::size_t i = 0; i < candidates.size(); ++i) {
        auto it = std::lower_bound(
            tails.begin(), tails.end(), candidates[i].second,
            [&](std::size_t index, std::size_t value) { return candidates[index].second < value; });
        const auto n = std::size_t(it - tails.begin());
        if (n)
            previous[i] = tails[n - 1];
        if (it == tails.end())
            tails.push_back(i);
        else
            *it = i;
    }
    std::vector<std::pair<std::size_t, std::size_t>> anchors;
    if (!tails.empty())
        for (auto i = tails.back(); i != std::size_t(-1); i = previous[i])
            anchors.push_back(candidates[i]);
    std::reverse(anchors.begin(), anchors.end());
    anchors.push_back({a.size(), b.size()});
    auto emit = [&](DiffKind kind, std::size_t ai, std::size_t bi) {
        rows_.push_back(rowOf(kind, kind == DiffKind::added ? b[bi] : a[ai],
                              kind == DiffKind::added ? 0 : int(ai + 1),
                              kind == DiffKind::removed ? 0 : int(bi + 1)));
    };
    std::size_t ai = 0, bi = 0;
    for (const auto anchor : anchors) {
        auto endA = anchor.first, endB = anchor.second;
        while (ai < endA && bi < endB && a[ai] == b[bi]) {
            emit(DiffKind::context, ai++, bi++);
        }
        std::size_t suffix = 0;
        while (endA > ai && endB > bi && a[endA - 1] == b[endB - 1]) {
            --endA;
            --endB;
            ++suffix;
        }
        const auto n = endA - ai, m = endB - bi;
        if (n && m && n + 1 <= budget / (m + 1)) {
            const std::size_t cells = (n + 1) * (m + 1);
            budget -= cells;
            std::vector<unsigned> dp(cells);
            for (std::size_t i = n; i-- > 0;)
                for (std::size_t j = m; j-- > 0;)
                    dp[i * (m + 1) + j] = a[ai + i] == b[bi + j]
                                              ? 1 + dp[(i + 1) * (m + 1) + j + 1]
                                              : std::max(dp[(i + 1) * (m + 1) + j], dp[i * (m + 1) + j + 1]);
            std::size_t i = 0, j = 0;
            while (i < n || j < m) {
                if (i < n && j < m && a[ai + i] == b[bi + j]) {
                    emit(DiffKind::context, ai + i, bi + j);
                    ++i;
                    ++j;
                } else if (j < m && (i == n || dp[i * (m + 1) + j + 1] > dp[(i + 1) * (m + 1) + j])) {
                    emit(DiffKind::added, ai + i, bi + j);
                    ++j;
                } else {
                    emit(DiffKind::removed, ai + i, bi + j);
                    ++i;
                }
            }
        } else {
            if (n && m)
                approximate_ = true;
            for (auto i = ai; i < endA; ++i)
                emit(DiffKind::removed, i, bi);
            for (auto j = bi; j < endB; ++j)
                emit(DiffKind::added, endA, j);
        }
        ai = endA;
        bi = endB;
        for (std::size_t i = 0; i < suffix; ++i)
            emit(DiffKind::context, ai++, bi++);
        if (anchor.first < a.size()) {
            emit(DiffKind::context, ai++, bi++);
        }
    }
    if (a == b && endsWithNewline(before) != endsWithNewline(after) && !rows_.empty()) {
        const auto last = rows_.back();
        rows_.pop_back();
        rows_.push_back(rowOf(DiffKind::removed, last.code.text, last.oldLine, 0));
        rows_.push_back(rowOf(DiffKind::added, last.code.text, 0, last.newLine));
    }
    if (!before.empty() && before.back() != '\n')
        rows_.push_back(rowOf(DiffKind::notice, "\\ No newline at end of old document", 0, 0));
    if (!after.empty() && after.back() != '\n')
        rows_.push_back(rowOf(DiffKind::notice, "\\ No newline at end of new document", 0, 0));
    // Lines shown as unchanged may still differ by their '\r': say so once.
    bool endings = false;
    for (const auto &r : rows_)
        endings = endings || (r.kind == DiffKind::context && r.oldLine && r.newLine &&
                              crA[std::size_t(r.oldLine - 1)] != crB[std::size_t(r.newLine - 1)]);
    if (endings)
        rows_.push_back(rowOf(DiffKind::notice, "\\ Line endings differ (CRLF / LF)", 0, 0));
    prepare(language);
}
void DiffDocument::setUnified(const std::string &text, CodeLanguage language) {
    if (text.size() > 32 * 1024 * 1024)
        throw std::length_error("Unified diff exceeds 32 MiB");
    rows_.clear();
    approximate_ = false;
    int oldLine = 0, newLine = 0;
    bool hunk = false;
    const std::regex header(R"(^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@)");
    for (const auto &line : linesOf(text)) {
        std::smatch match;
        if (std::regex_search(line, match, header)) {
            const auto oldNumber = match[1].str(), newNumber = match[2].str();
            const auto oldResult =
                           std::from_chars(oldNumber.data(), oldNumber.data() + oldNumber.size(), oldLine),
                       newResult =
                           std::from_chars(newNumber.data(), newNumber.data() + newNumber.size(), newLine);
            if (oldResult.ec != std::errc() || newResult.ec != std::errc() || oldLine > 1000000000 ||
                newLine > 1000000000) {
                hunk = false;
                rows_.push_back(rowOf(DiffKind::notice, line, 0, 0));
                continue;
            }
            hunk = true;
            rows_.push_back(rowOf(DiffKind::hunk, line, 0, 0));
        } else if (hunk && !line.empty() && line[0] == '-')
            rows_.push_back(rowOf(DiffKind::removed, line.substr(1), oldLine++, 0));
        else if (hunk && !line.empty() && line[0] == '+')
            rows_.push_back(rowOf(DiffKind::added, line.substr(1), 0, newLine++));
        else if (hunk && !line.empty() && line[0] == ' ')
            rows_.push_back(rowOf(DiffKind::context, line.substr(1), oldLine++, newLine++));
        else {
            if (line.rfind("diff ", 0) == 0 || line.rfind("--- ", 0) == 0)
                hunk = false;
            rows_.push_back(rowOf(DiffKind::notice, line, 0, 0));
        }
    }
    prepare(language);
}
void DiffDocument::prepare(CodeLanguage language) {
    columns_ = 0;
    pairs_.clear();
    hunks_.clear();
    std::string oldText, newText;
    std::vector<int> oldIndex(rows_.size(), -1), newIndex(rows_.size(), -1);
    int oldCount = 0, newCount = 0;
    bool changing = false;
    for (std::size_t i = 0; i < rows_.size(); ++i) {
        auto &r = rows_[i];
        const bool changed = r.kind == DiffKind::added || r.kind == DiffKind::removed;
        if (r.kind == DiffKind::hunk || (changed && !changing))
            hunks_.push_back(int(i));
        changing = changed || r.kind == DiffKind::hunk;
        if (r.oldLine) {
            oldIndex[i] = oldCount++;
            oldText += r.code.text + '\n';
        }
        if (r.newLine) {
            newIndex[i] = newCount++;
            newText += r.code.text + '\n';
        }
    }
    CodeDocument oldCode, newCode;
    oldCode.setText(oldText, language);
    newCode.setText(newText, language);
    for (std::size_t i = 0; i < rows_.size(); ++i) {
        if (newIndex[i] >= 0)
            rows_[i].code = newCode.lines()[std::size_t(newIndex[i])];
        else if (oldIndex[i] >= 0)
            rows_[i].code = oldCode.lines()[std::size_t(oldIndex[i])];
        else
            rows_[i].code.text = expandTabs(rows_[i].code.text);
    }
    for (std::size_t i = 0; i < rows_.size();) {
        if (rows_[i].kind == DiffKind::removed || rows_[i].kind == DiffKind::added) {
            std::vector<int> left, right;
            while (i < rows_.size() &&
                   (rows_[i].kind == DiffKind::removed || rows_[i].kind == DiffKind::added)) {
                (rows_[i].kind == DiffKind::removed ? left : right).push_back(int(i));
                ++i;
            }
            for (std::size_t j = 0; j < std::max(left.size(), right.size()); ++j) {
                const int l = j < left.size() ? left[j] : -1, r = j < right.size() ? right[j] : -1;
                pairs_.push_back({l, r});
                if (l >= 0 && r >= 0) {
                    const auto &a = rows_[std::size_t(l)].code.text, &b = rows_[std::size_t(r)].code.text;
                    std::size_t prefix = 0, suffix = 0;
                    while (prefix < a.size() && prefix < b.size() && a[prefix] == b[prefix])
                        ++prefix;
                    while (prefix > 0 && prefix < a.size() &&
                           (static_cast<unsigned char>(a[prefix]) & 0xc0) == 0x80)
                        --prefix;
                    while (suffix < a.size() - prefix && suffix < b.size() - prefix &&
                           a[a.size() - suffix - 1] == b[b.size() - suffix - 1])
                        ++suffix;
                    while (suffix > 0 && ((static_cast<unsigned char>(a[a.size() - suffix]) & 0xc0) == 0x80))
                        --suffix;
                    rows_[std::size_t(l)].inlineStart = rows_[std::size_t(r)].inlineStart = prefix;
                    rows_[std::size_t(l)].inlineEnd = a.size() - suffix;
                    rows_[std::size_t(r)].inlineEnd = b.size() - suffix;
                }
            }
        } else {
            pairs_.push_back({int(i), int(i)});
            ++i;
        }
    }
    for (const auto &r : rows_)
        columns_ = std::max(columns_, r.code.text.size());
    ++revision_;
}
DiffResult diffViewer(const char *id, DiffState &s, const DiffDocument &doc, ImVec2 size) {
    ScopedID scope(id);
    DiffResult result;
    ImGui::Checkbox(trLabel("Side by side").c_str(), &s.split);
    ImGui::SameLine();
    if (ghostButton(trLabel("Next change").c_str()) && !doc.hunks().empty()) {
        s.hunk = (s.hunk + 1) % int(doc.hunks().size());
        s.focus = doc.hunks()[std::size_t(s.hunk)];
    }
    ImGui::SameLine();
    ImGui::SetNextItemWidth(dp(180));
    detail::inputText("##find", s.query);
    ImGui::SameLine();
    if (ghostButton(trLabel("Find next").c_str()) && !s.query.empty()) {
        for (std::size_t n = 1; n <= doc.rows().size(); ++n) {
            const auto i = (std::size_t(std::max(-1, s.selected) + 1) + n - 1) % doc.rows().size();
            if (doc.rows()[i].code.text.find(s.query) != std::string::npos) {
                s.focus = s.selected = int(i);
                break;
            }
        }
    }
    if (size.y > 0)
        size.y = std::max(dp(1), size.y - ImGui::GetFrameHeightWithSpacing());
    ScopedFont font(monoFont());
    const float height = ImGui::GetTextLineHeightWithSpacing();
    auto pane = [&](const char *name, int side, ImVec2 extent) {
        const std::size_t count = side < 0 ? doc.rows().size() : doc.pairs().size();
        const auto columns = doc.columns();
        const float width = std::max(extent.x, dp(90) + float(columns) * ImGui::CalcTextSize("M").x);
        ImGui::SetNextWindowContentSize({width, float(count) * height});
        ScopedColor ground(ImGuiCol_ChildBg, palette().bg0);
        ImGui::BeginChild(name, extent, ImGuiChildFlags_Borders, ImGuiWindowFlags_HorizontalScrollbar);
        if (side >= 0) {
            if (ImGui::IsWindowHovered())
                s.scroll = ImGui::GetScrollY();
            else
                ImGui::SetScrollY(s.scroll);
        }
        if (s.focus >= 0) {
            int row = s.focus;
            if (side >= 0)
                for (std::size_t i = 0; i < doc.pairs().size(); ++i)
                    if (doc.pairs()[i].left == s.focus || doc.pairs()[i].right == s.focus) {
                        row = int(i);
                        break;
                    }
            s.scroll = std::max(0.f, row * height - ImGui::GetWindowHeight() * .25f);
            ImGui::SetScrollY(s.scroll);
        }
        ImGuiListClipper clip;
        clip.Begin(int(count), height);
        while (clip.Step())
            for (int i = clip.DisplayStart; i < clip.DisplayEnd; ++i) {
                const int index = side < 0    ? i
                                  : side == 0 ? doc.pairs()[std::size_t(i)].left
                                              : doc.pairs()[std::size_t(i)].right;
                const auto p = ImGui::GetCursorScreenPos();
                ScopedID row(i);
                detail::listRow("##line", {width, height});
                if (index < 0)
                    continue;
                const auto &r = doc.rows()[std::size_t(index)];
                auto *d = ImGui::GetWindowDrawList();
                const auto tint = r.kind == DiffKind::added     ? palette().success
                                  : r.kind == DiffKind::removed ? palette().error
                                  : r.kind == DiffKind::hunk    ? palette().info
                                                                : palette().bg0;
                const auto bg = mix(palette().bg0, tint, .14f);
                d->AddRectFilled(p, {p.x + width, p.y + height},
                                 s.selected == index ? palette().selection : bg);
                const float gutter = dp(88);
                if (r.inlineEnd > r.inlineStart) {
                    const float
                        a = ImGui::CalcTextSize(r.code.text.data(), r.code.text.data() + r.inlineStart).x,
                        b = ImGui::CalcTextSize(r.code.text.data(), r.code.text.data() + r.inlineEnd).x;
                    d->AddRectFilled({p.x + gutter + a, p.y}, {p.x + gutter + b, p.y + height},
                                     alpha(tint, .25f));
                }
                char number[48];
                if (side < 0)
                    std::snprintf(number, sizeof(number), "%5d %5d", r.oldLine, r.newLine);
                else
                    std::snprintf(number, sizeof(number), "%6d", side == 0 ? r.oldLine : r.newLine);
                d->AddText({p.x + dp(4), p.y}, palette().textFaint, number);
                if (r.kind == DiffKind::notice)
                    d->AddText({p.x + gutter, p.y}, palette().textDim, tr(r.code.text.c_str()).c_str());
                else
                    paintCodeLine(d, {p.x + gutter, p.y}, r.code, bg);
                if (ImGui::IsItemClicked()) {
                    s.selected = index;
                    result = {index, r.oldLine, r.newLine};
                }
                if (!s.query.empty() && r.code.text.find(s.query) != std::string::npos)
                    d->AddRect(p, {p.x + width, p.y + height}, palette().warning, 0, 0, dp(1));
                if (ImGui::BeginPopupContextItem()) {
                    if (ImGui::MenuItem(trLabel("Copy line").c_str()))
                        ImGui::SetClipboardText(r.code.text.c_str());
                    ImGui::EndPopup();
                }
            }
        ImGui::EndChild();
    };
    if (s.split) {
        const float width =
            (size.x > 0 ? size.x : ImGui::GetContentRegionAvail().x) - ImGui::GetStyle().ItemSpacing.x;
        pane("##before", 0, {width * .5f, size.y});
        ImGui::SameLine();
        pane("##after", 1, {width * .5f, size.y});
    } else
        pane("##unified", -1, size);
    s.focus = -1;
    return result;
}
} // namespace cyui
