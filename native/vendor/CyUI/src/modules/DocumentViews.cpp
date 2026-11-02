#include "cyui/modules/DocumentViews.h"
#include <sstream>
#include <unordered_set>
#include <regex>
#include <stdexcept>

namespace cyui {
namespace {
std::vector<std::string> cells(std::string line) {
    if (!line.empty() && line.front() == '|')
        line.erase(0, 1);
    if (!line.empty() && line.back() == '|')
        line.pop_back();
    std::vector<std::string> out;
    std::istringstream stream(line);
    std::string cell;
    while (std::getline(stream, cell, '|')) {
        const auto a = cell.find_first_not_of(" \t"), b = cell.find_last_not_of(" \t");
        out.push_back(a == std::string::npos ? "" : cell.substr(a, b - a + 1));
    }
    return out;
}
CodeLanguage languageOf(const std::string &name) {
    return name == "python"                       ? CodeLanguage::python
           : name == "json"                       ? CodeLanguage::json
           : name == "js" || name == "javascript" ? CodeLanguage::javascript
           : name == "ts" || name == "typescript" ? CodeLanguage::typescript
                                                  : CodeLanguage::cpp;
}
// Delimited runs need a non-space just inside each delimiter. Any other token ("**kwargs", "*ptr",
// "a * b") falls through to the last alternative and is shown verbatim.
std::vector<DocumentInline> parseInline(const std::string &text) {
    static const std::regex tokens(R"(\[([^\]]+)\]\(([^)]+)\)|`([^`]+)`|\*\*([^*\s](?:[^*]*[^*\s])?)\*\*|)"
                                   R"(\*([^*\s](?:[^*]*[^*\s])?)\*|\S+\s*)");
    std::vector<DocumentInline> out;
    std::size_t previousEnd = 0;
    for (std::sregex_iterator it(text.begin(), text.end(), tokens), end; it != end; ++it) {
        const auto &m = *it;
        DocumentInline run;
        run.spaced = std::size_t(m.position()) > previousEnd;
        previousEnd = std::size_t(m.position() + m.length());
        if (m[1].matched) {
            run.kind = DocumentInlineKind::link;
            run.text = m[1];
            run.target = m[2];
        } else if (m[3].matched) {
            run.kind = DocumentInlineKind::code;
            run.text = m[3];
        } else if (m[4].matched) {
            run.kind = DocumentInlineKind::strong;
            run.text = m[4];
        } else if (m[5].matched) {
            run.kind = DocumentInlineKind::emphasis;
            run.text = m[5];
        } else
            run.text = m.str();
        out.push_back(std::move(run));
    }
    return out;
}
} // namespace
bool Document::setMarkdown(const std::string &text) {
    if (text == text_)
        return false;
    if (text.size() > 32 * 1024 * 1024)
        throw std::length_error("Document exceeds 32 MiB");
    text_ = text;
    blocks_.clear();
    std::istringstream stream(text);
    std::vector<std::string> lines;
    std::string line;
    while (std::getline(stream, line)) {
        if (!line.empty() && line.back() == '\r')
            line.pop_back();
        lines.push_back(line);
    }
    static const std::regex list(R"(^\s*(?:[-*+] |\d+[.)] )(.*)$)"),
        headingPattern(R"(^(#{1,6})\s+(.*)$)"), separator(R"(^[\s|:\-]+$)");
    for (std::size_t at = 0; at < lines.size();) {
        line = lines[at++];
        DocumentBlock block;
        block.text = line;
        std::smatch match;
        if (line.rfind("```", 0) == 0 || line.rfind("~~~", 0) == 0) {
            block.kind = DocumentBlockKind::code;
            block.language = line.substr(3);
            block.text.clear();
            const auto fence = line.substr(0, 3);
            while (at < lines.size() && lines[at].rfind(fence, 0) != 0)
                block.text += lines[at++] + '\n';
            if (at < lines.size())
                ++at;
            block.code.setText(block.text, languageOf(block.language));
        } else if (line.empty())
            continue;
        else if (at < lines.size() && line.find('|') != std::string::npos &&
                 lines[at].find('-') != std::string::npos && std::regex_match(lines[at], separator)) {
            block.kind = DocumentBlockKind::table;
            block.rows.push_back(cells(line));
            ++at;
            while (at < lines.size() && lines[at].find('|') != std::string::npos)
                block.rows.push_back(cells(lines[at++]));
        } else if (line == "---" || line == "***" || line == "___")
            block.kind = DocumentBlockKind::rule;
        else if (std::regex_match(line, match, headingPattern)) {
            block.kind = DocumentBlockKind::heading;
            block.level = int(match[1].length());
            block.text = match[2];
        } else if (line.rfind("> ", 0) == 0) {
            block.kind = DocumentBlockKind::quote;
            block.text = line.substr(2);
        } else if (std::regex_match(line, match, list)) {
            block.kind = DocumentBlockKind::list;
            block.text = match[1];
        }
        if (block.kind == DocumentBlockKind::paragraph || block.kind == DocumentBlockKind::list)
            block.inlines = parseInline(block.text);
        for (const auto &row : block.rows) {
            block.cells.emplace_back();
            for (const auto &cell : row)
                block.cells.back().push_back(parseInline(cell));
        }
        blocks_.push_back(std::move(block));
    }
    return true;
}
namespace {
void inlineText(const std::vector<DocumentInline> &runs, DocumentResult &result) {
    const float right = ImGui::GetCursorScreenPos().x + ImGui::GetContentRegionAvail().x;
    bool first = true;
    int index = 0;
    for (const auto &run : runs) {
        ScopedID tokenId{index++};
        const float gap = run.spaced ? ImGui::CalcTextSize(" ").x : 0;
        const bool linked = run.kind == DocumentInlineKind::link, mono = run.kind == DocumentInlineKind::code,
                   strong = run.kind == DocumentInlineKind::strong,
                   emphasis = run.kind == DocumentInlineKind::emphasis;
        const auto &label = run.text;
        ImFont *font = mono ? monoFont() : strong ? boldFont() : nullptr;
        if (font)
            ImGui::PushFont(font, 0);
        const float width = ImGui::CalcTextSize(label.c_str()).x;
        if (!first && ImGui::GetItemRectMax().x + width + gap < right)
            ImGui::SameLine(0, gap);
        if (linked) {
            if (linkText(label.c_str()))
                result.link = run.target;
        } else if (mono)
            textColoured(palette().accent, tr("%s").c_str(), label.c_str());
        else if (emphasis)
            textDim(tr("%s").c_str(), label.c_str());
        else
            ImGui::TextWrapped("%s", label.c_str());
        if (font)
            ImGui::PopFont();
        first = false;
    }
}
DocumentResult content(DocumentState &s, const Document &doc) {
    DocumentResult result;
    s.codeViews.resize(doc.blocks().size());
    for (std::size_t n = 0; n < doc.blocks().size(); ++n) {
        const auto &b = doc.blocks()[n];
        ScopedID key{int(n)};
        switch (b.kind) {
        case DocumentBlockKind::heading:
            heading(b.text.c_str(), std::min(3, b.level));
            break;
        case DocumentBlockKind::rule:
            divider();
            break;
        case DocumentBlockKind::quote:
            callout(Tone::info, b.text.c_str());
            break;
        case DocumentBlockKind::list:
            ImGui::Bullet();
            ImGui::SameLine();
            inlineText(b.inlines, result);
            break;
        case DocumentBlockKind::code: {
            textFaint(tr("%s").c_str(), b.language.c_str());
            ImGui::SameLine();
            if (ghostButton(trLabel("Copy").c_str())) {
                ImGui::SetClipboardText(b.text.c_str());
                result.copiedBlock = int(n);
            }
            codeViewer("##block", s.codeViews[n], b.code,
                       {0, dp(std::clamp(float(b.code.lines().size()) * 20 + 48, 90.f, 320.f))});
            break;
        }
        case DocumentBlockKind::table: {
            std::size_t columns = 0;
            for (const auto &row : b.rows)
                columns = std::max(columns, row.size());
            columns = std::min<std::size_t>(32, columns);
            if (columns && ImGui::BeginTable("##table", int(columns),
                                             ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg |
                                                 ImGuiTableFlags_SizingStretchSame)) {
                for (std::size_t r = 0; r < b.rows.size(); ++r) {
                    ScopedID rowId{int(r)};
                    ImGui::TableNextRow(r == 0 ? ImGuiTableRowFlags_Headers : 0);
                    for (std::size_t col = 0; col < std::min(columns, b.rows[r].size()); ++col) {
                        ScopedID cellId{int(col)};
                        ImGui::TableNextColumn();
                        inlineText(b.cells[r][col], result);
                    }
                }
                ImGui::EndTable();
            }
            break;
        }
        default:
            inlineText(b.inlines, result);
            break;
        }
        space(3);
    }
    return result;
}
} // namespace
DocumentResult documentView(const char *id, DocumentState &s, const Document &doc, ImVec2 size) {
    ScopedID scope(id);
    ImGui::BeginChild("##document", size);
    auto result = content(s, doc);
    ImGui::EndChild();
    return result;
}
TranscriptResult transcript(const char *id, TranscriptState &s, ArrayView<TranscriptMessage> messages,
                            ImVec2 size) {
    ScopedID scope(id);
    TranscriptResult result;
    std::unordered_set<ItemId> present;
    ImGui::BeginChild("##transcript", size);
    const bool bottom = ImGui::GetScrollY() >= ImGui::GetScrollMaxY() - dp(3);
    bool changed = false;
    const float width = ImGui::GetContentRegionAvail().x, spacing = ImGui::GetStyle().ItemSpacing.y;
    const ImVec4 layout{width, ImGui::GetFontSize(), ImGui::GetFrameHeight(), spacing};
    if (layout.x != s.layout.x || layout.y != s.layout.y || layout.z != s.layout.z || layout.w != s.layout.w) {
        s.heights.clear(); // wrapping, fonts or spacing change every height
        s.layout = layout;
    }
    for (std::size_t n = 0; n < messages.count; ++n) {
        const auto &m = messages[n];
        present.insert(m.id);
        auto &doc = s.documents[m.id];
        const bool edited = doc.setMarkdown(m.content);
        changed = changed || edited;
        ItemScope key(m.id);
        // Offscreen messages measured before, with the same role/time, only reserve their height.
        const std::hash<std::string> hash;
        const std::size_t signature = hash(m.role) * 31 ^ hash(m.time);
        const auto known = s.heights.find(m.id);
        if (!edited && known != s.heights.end() && known->second.signature == signature &&
            !ImGui::IsRectVisible({std::max(1.f, width), known->second.height})) {
            ImGui::Dummy({1, std::max(0.f, known->second.height - spacing)});
            continue;
        }
        const float top = ImGui::GetCursorPosY();
        heading(m.role.c_str(), 3);
        ImGui::SameLine();
        textFaint(tr("%s").c_str(), m.time.c_str());
        if (ghostButton(trLabel("Copy message").c_str())) {
            ImGui::SetClipboardText(m.content.c_str());
            result.copy = m.id;
        }
        auto r = content(s.views[m.id], doc);
        if (!r.link.empty())
            result.link = r.link;
        divider();
        s.heights[m.id] = {ImGui::GetCursorPosY() - top, signature};
    }
    if (s.follow && bottom && (changed || s.count != messages.count))
        ImGui::SetScrollHereY(1);
    s.count = messages.count;
    ImGui::EndChild();
    for (auto it = s.documents.begin(); it != s.documents.end();)
        if (!present.count(it->first)) {
            s.views.erase(it->first);
            s.heights.erase(it->first);
            it = s.documents.erase(it);
        } else
            ++it;
    return result;
}
} // namespace cyui
