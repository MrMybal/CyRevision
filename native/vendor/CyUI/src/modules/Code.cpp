#include "cyui/modules/Code.h"
#include "ModuleInternal.h"
#include <set>
#include <cstdio>
#include <stdexcept>

namespace cyui {
std::size_t displayOffset(std::string_view text, std::size_t end, unsigned tabs) {
    tabs = std::max(1u, tabs);
    end = std::min(end, text.size());
    std::size_t bytes = 0, column = 0;
    for (std::size_t i = 0; i < end; ++i) {
        const auto c = static_cast<unsigned char>(text[i]);
        if (c == '\t') {
            const auto n = tabs - column % tabs;
            bytes += n;
            column += n;
        } else {
            ++bytes;
            if ((c & 0xc0) != 0x80)
                ++column;
        }
    }
    return bytes;
}
std::string expandTabs(std::string_view text, unsigned tabs) {
    tabs = std::max(1u, tabs);
    std::string out;
    std::size_t column = 0;
    for (unsigned char c : text) {
        if (c == '\t') {
            const auto n = tabs - column % tabs;
            out.append(n, ' ');
            column += n;
        } else {
            out += char(c);
            if ((c & 0xc0) != 0x80)
                ++column;
        }
    }
    return out;
}
namespace {
double luminance(ImU32 color) {
    const auto v = vec(color);
    auto l = [](float c) { return c <= .04045f ? c / 12.92 : std::pow((c + .055) / 1.055, 2.4); };
    return .2126 * l(v.x) + .7152 * l(v.y) + .0722 * l(v.z);
}
} // namespace
bool CodeDocument::setText(const std::string &text, CodeLanguage language, std::uint64_t revision) {
    if (text.size() > 32 * 1024 * 1024)
        throw std::length_error("CodeDocument exceeds 32 MiB; supply a paged document");
    const auto syntaxRevision=codeLanguageRevision();
    if (text_ == text && language_ == language && syntaxRevision_==syntaxRevision && !lines_.empty()) {
        if (revision && revision != revision_) {
            revision_ = revision;
            for (auto &l : lines_) l.overlays.clear();
        }
        return false;
    }
    // Prepare before replacing the cache: a failing plugin must leave the old document intact.
    const auto tokens=highlightCode(text,language);
    std::vector<CodeLine> lines;
    std::size_t columns=0,start=0,tokenIndex=0;
    do {
        const auto newline=text.find('\n',start);
        auto end=newline==std::string::npos ? text.size() : newline;
        if (end>start && text[end-1]=='\r') --end;
        const std::string_view raw(text.data()+start,end-start);
        auto display=expandTabs(raw);columns=std::max(columns,display.size());
        std::vector<CodeSpan> spans;
        std::size_t rawAt=0,displayAt=0,column=0;
        auto offset=[&](std::size_t target) {
            while (rawAt<target) {
                const auto c=static_cast<unsigned char>(raw[rawAt++]);
                if (c=='\t') { const auto n=4-column%4;displayAt+=n;column+=n; }
                else { ++displayAt;if ((c&0xc0)!=0x80) ++column; }
            }return displayAt;
        };
        while (tokenIndex<tokens.size() && tokens[tokenIndex].start+tokens[tokenIndex].length<=start) ++tokenIndex;
        for (auto i=tokenIndex;i<tokens.size() && tokens[i].start<end;++i) {
            const auto &token=tokens[i];
            const auto from=std::max(start,token.start),to=std::min(end,token.start+token.length);
            if (to>from) { const auto a=offset(from-start),b=offset(to-start);spans.push_back({a,b-a,token.role}); }
        }
        lines.push_back({std::move(display),std::move(spans),{}});
        if (newline==std::string::npos) break;
        start=newline+1;
    } while (start<=text.size());
    text_=text;language_=language;revision_=revision ? revision : revision_+1;
    syntaxRevision_=syntaxRevision;columns_=columns;lines_=std::move(lines);return true;
}
bool CodeDocument::setOverlays(ArrayView<SourceSpan> spans, std::uint64_t revision) {
    if (revision != revision_)
        return false;
    for (auto &l : lines_)
        l.overlays.clear();
    std::vector<std::string_view> raw;
    std::size_t start = 0;
    do {
        const auto end = text_.find('\n', start);
        raw.emplace_back(text_.data() + start, (end == std::string::npos ? text_.size() : end) - start);
        if (end == std::string::npos)
            break;
        start = end + 1;
    } while (start <= text_.size());
    for (std::size_t i = 0; i < spans.count; ++i) {
        const auto &s = spans[i];
        if (s.line >= lines_.size() || s.endByte < s.startByte || s.endByte > raw[s.line].size())
            continue;
        const auto boundary = [&](std::size_t at) {
            return at == raw[s.line].size() || (static_cast<unsigned char>(raw[s.line][at]) & 0xc0) != 0x80;
        };
        if (!boundary(s.startByte) || !boundary(s.endByte))
            continue;
        const auto a = displayOffset(raw[s.line], s.startByte), b = displayOffset(raw[s.line], s.endByte);
        lines_[s.line].overlays.push_back({a, b - a, s.role});
    }
    return true;
}
ImU32 tokenColor(TokenRole role, ImU32 background) {
    const auto &p = palette();
    ImU32 c = p.text;
    switch (role) {
    case TokenRole::keyword:
        c = p.accent;
        break;
    case TokenRole::type:
        c = p.info;
        break;
    case TokenRole::number:
        c = p.warning;
        break;
    case TokenRole::string:
        c = p.success;
        break;
    case TokenRole::comment:
        c = p.textFaint;
        break;
    case TokenRole::preprocessor:
        c = mix(p.info, p.accent, .5f);
        break;
    case TokenRole::function:
        c = mix(p.text, p.warning, .4f);
        break;
    case TokenRole::added: case TokenRole::ansiGreen: c=p.success;break;
    case TokenRole::removed: case TokenRole::error: case TokenRole::ansiRed: c=p.error;break;
    case TokenRole::heading: case TokenRole::ansiBlue: c=p.info;break;
    case TokenRole::warning: case TokenRole::ansiYellow: c=p.warning;break;
    case TokenRole::ansiMagenta: c=categorical(4);break;
    case TokenRole::ansiCyan: c=mix(p.info,p.success,.5f);break;
    case TokenRole::ansiBlack: c=p.textFaint;break;
    case TokenRole::ansiWhite: c=p.text;break;
    default:
        break;
    }
    if (!background)
        background = p.bg0;
    const double ground = luminance(background);
    for (int i = 0; i <= 10; ++i) {
        const auto candidate = mix(c, p.text, i / 10.f);
        const double light = luminance(candidate);
        if ((std::max(ground, light) + .05) / (std::min(ground, light) + .05) >= 4.5)
            return candidate;
    }
    return p.text;
}
void paintCodeLine(ImDrawList *d, ImVec2 p, const CodeLine &line, ImU32 background) {
    std::vector<std::size_t> cuts{0, line.text.size()};
    for (const auto &span : line.spans) {
        cuts.push_back(std::min(span.start, line.text.size()));
        cuts.push_back(std::min(span.start + span.length, line.text.size()));
    }
    for (const auto &span : line.overlays) {
        cuts.push_back(std::min(span.start, line.text.size()));
        cuts.push_back(std::min(span.start + span.length, line.text.size()));
    }
    std::sort(cuts.begin(), cuts.end());
    cuts.erase(std::unique(cuts.begin(), cuts.end()), cuts.end());
    std::size_t lexical = 0;
    for (std::size_t i = 1; i < cuts.size(); ++i) {
        const auto a = cuts[i - 1], b = cuts[i];
        if (a == b)
            continue;
        TokenRole role = TokenRole::text;
        while (lexical < line.spans.size() && line.spans[lexical].start + line.spans[lexical].length <= a)
            ++lexical;
        if (lexical < line.spans.size() && line.spans[lexical].start <= a)
            role = line.spans[lexical].role;
        for (const auto &s : line.overlays)
            if (a >= s.start && a < s.start + s.length)
                role = s.role;
        const char *begin = line.text.data() + a, *end = line.text.data() + b;
        d->AddText(p, tokenColor(role, background), begin, end);
        p.x += ImGui::CalcTextSize(begin, end).x;
    }
}
CodeViewResult codeViewer(const char *id, CodeViewState &s, const CodeDocument &doc, ImVec2 size,
                          ArrayView<CodeAnnotation> annotations) {
    ScopedID scope(id);
    CodeViewResult result;
    if (s.searchedRevision != doc.revision() || s.searchedQuery != s.query) {
        s.matches.clear();
        if (!s.query.empty())
            for (std::size_t i = 0; i < doc.lines().size(); ++i)
                if (doc.lines()[i].text.find(s.query) != std::string::npos)
                    s.matches.push_back(int(i));
        s.searchedRevision = doc.revision();
        s.searchedQuery = s.query;
        s.match = s.matches.empty() ? -1 : 0;
    }
    ImGui::SetNextItemWidth(-dp(100));
    detail::inputText("##find", s.query);
    ImGui::SameLine();
    if (ghostButton(trLabel("Next").c_str()) && !s.matches.empty()) {
        s.match = (s.match + 1) % int(s.matches.size());
        s.focusLine = s.matches[std::size_t(s.match)];
    }
    if (size.y > 0)
        size.y = std::max(dp(1), size.y - ImGui::GetFrameHeightWithSpacing());
    ScopedFont font(monoFont());
    ScopedColor ground(ImGuiCol_ChildBg, palette().bg0);
    ImGui::BeginChild("##source", size, ImGuiChildFlags_Borders, ImGuiWindowFlags_HorizontalScrollbar);
    const float height = ImGui::GetTextLineHeightWithSpacing(), gutter = dp(56);
    const float width = std::max(ImGui::GetContentRegionAvail().x,
                                 gutter + float(doc.columns()) * ImGui::CalcTextSize("M").x);
    if (s.focusLine >= 0) {
        ImGui::SetScrollY(std::max(0.f, height * s.focusLine - ImGui::GetWindowHeight() * .25f));
        s.focusLine = -1;
    }
    ImGuiListClipper clip;
    clip.Begin(int(doc.lines().size()), height);
    while (clip.Step())
        for (int i = clip.DisplayStart; i < clip.DisplayEnd; ++i) {
            ScopedID row(i);
            const auto p = ImGui::GetCursorScreenPos();
            detail::listRow("##line", {width, height});
            auto *d = ImGui::GetWindowDrawList();
            if (i == s.selectedLine)
                d->AddRectFilled(p, {p.x + width, p.y + height}, palette().selection);
            if (ImGui::IsItemClicked()) {
                s.selectedLine = i;
                result.clickedLine = i;
            }
            char number[32];
            std::snprintf(number, sizeof(number), "%d", i + 1);
            d->AddText({p.x + dp(5), p.y}, palette().textFaint, number);
            paintCodeLine(d, {p.x + gutter, p.y}, doc.lines()[std::size_t(i)]);
            if (!s.query.empty() && doc.lines()[std::size_t(i)].text.find(s.query) != std::string::npos)
                d->AddRect({p.x + gutter, p.y}, {p.x + width, p.y + height}, palette().warning, 0, 0, dp(1));
            for (std::size_t j = 0; j < annotations.count; ++j)
                if (annotations[j].line == std::size_t(i)) {
                    d->AddCircleFilled({p.x + gutter - dp(8), p.y + height * .4f}, dp(3),
                                       toneColour(annotations[j].tone));
                    if (ImGui::IsItemHovered())
                        ImGui::SetTooltip("%s", annotations[j].message.c_str());
                    if (ImGui::IsItemClicked())
                        result.annotation = annotations[j].id;
                }
            if (ImGui::BeginPopupContextItem()) {
                if (ImGui::MenuItem(trLabel("Copy line").c_str()))
                    ImGui::SetClipboardText(doc.lines()[std::size_t(i)].text.c_str());
                ImGui::EndPopup();
            }
        }
    ImGui::EndChild();
    return result;
}
} // namespace cyui
