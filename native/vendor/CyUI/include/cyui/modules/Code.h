#pragma once
#include "cyui/Core.h"
#include "cyui/Interaction.h"
#include <string>
#include <string_view>
#include <functional>
#include <optional>

namespace cyui {
// Values 0..13 remain source/ABI compatible; plugin IDs use the high bit.
enum class CodeLanguage : std::uint32_t {
#include "cyui/detail/CodeLanguageList.inc"
};
enum class TokenRole {
    text, keyword, type, number, string, comment, preprocessor, function,
    added, removed, heading, warning, error,
    ansiBlack, ansiRed, ansiGreen, ansiYellow, ansiBlue, ansiMagenta, ansiCyan, ansiWhite,
    count
};
struct CodeToken { std::size_t start = 0, length = 0; TokenRole role = TokenRole::text; };
struct CodeLanguageInfo {
    CodeLanguage language = CodeLanguage::plain;
    std::string id, name;
    std::vector<std::string> extensions, filenames;
    bool builtin = true;
};
struct CodeSyntaxRules {
    std::vector<std::string> keywords, types, functions;
    std::string lineComment, blockCommentStart, blockCommentEnd;
    std::string quotes = "\"'";
    char escape = '\\';
    bool caseSensitive = true;
};
struct CodeLanguageDefinition {
    std::string id, name;
    std::vector<std::string> extensions, filenames;
    CodeLanguage base = CodeLanguage::plain;
    // Replaces selected Lexilla word sets (0-based). Empty entries inherit the base.
    std::vector<std::string> wordLists;
    std::vector<std::pair<std::string,std::string>> properties;
    std::optional<CodeSyntaxRules> rules;
    // Raw UTF-8 byte offsets. The host owns/pins any plugin code captured here.
    std::function<std::vector<CodeToken>(std::string_view)> highlight;
};
// Snapshots own their strings. Registration is independent of ImGui contexts and file loading.
CYUI_API std::vector<CodeLanguageInfo> codeLanguages();
CYUI_API CodeLanguageInfo codeLanguageInfo(CodeLanguage);
CYUI_API CodeLanguage codeLanguageForPath(std::string_view path);
CYUI_API CodeLanguage codeLanguageFromName(std::string_view idOrName);
CYUI_API std::uint64_t codeLanguageRevision();
CYUI_API CodeLanguage registerCodeLanguage(const CodeLanguageDefinition &);
CYUI_API bool unregisterCodeLanguage(std::string_view id);
// Throws on invalid plugin spans; does not modify source bytes. Maximum source size: 32 MiB.
CYUI_API std::vector<CodeToken> highlightCode(std::string_view, CodeLanguage);
struct CodeSpan {
    std::size_t start = 0, length = 0;
    TokenRole role = TokenRole::text;
};
struct CodeLine {
    std::string text;
    std::vector<CodeSpan> spans, overlays;
};
struct SourceSpan {
    std::size_t line = 0, startByte = 0, endByte = 0;
    TokenRole role = TokenRole::text;
};
// Byte offset in expandTabs(raw) matching byteOffset in raw (tabs widened to the next stop).
CYUI_API std::size_t displayOffset(std::string_view raw, std::size_t byteOffset, unsigned tabSize = 4);
// Replaces each tab by spaces up to the next multiple of tabSize columns (UTF-8 aware).
CYUI_API std::string expandTabs(std::string_view, unsigned tabSize = 4);
class CYUI_API CodeDocument {
  public:
    bool setText(const std::string &, CodeLanguage language = CodeLanguage::plain,
                 std::uint64_t revision = 0);
    bool setOverlays(ArrayView<SourceSpan>, std::uint64_t revision);
    const std::string &text() const {
        return text_;
    }
    const std::vector<CodeLine> &lines() const {
        return lines_;
    }
    std::uint64_t revision() const {
        return revision_;
    }
    CodeLanguage language() const {
        return language_;
    }
    std::size_t columns() const {
        return columns_;
    }

  private:
    std::string text_;
    std::vector<CodeLine> lines_;
    std::uint64_t revision_ = 0;
    CodeLanguage language_ = CodeLanguage::plain;
    std::size_t columns_ = 0;
    std::uint64_t syntaxRevision_ = 0;
};
CYUI_API ImU32 tokenColor(TokenRole, ImU32 background = 0);
CYUI_API void paintCodeLine(ImDrawList *, ImVec2, const CodeLine &, ImU32 background = 0);
struct CodeAnnotation {
    ItemId id = 0;
    std::size_t line = 0;
    Tone tone = Tone::error;
    std::string message;
};
struct CodeViewState {
    std::string query;
    int match = -1, focusLine = -1, selectedLine = -1;
    std::vector<int> matches;
    std::uint64_t searchedRevision = 0;
    std::string searchedQuery;
};
struct CodeViewResult {
    int clickedLine = -1;
    ItemId annotation = 0; // clicked annotation, 0 for none: give annotations nonzero IDs
};
CYUI_API CodeViewResult codeViewer(const char *id, CodeViewState &, const CodeDocument &, ImVec2 size = {},
                          ArrayView<CodeAnnotation> annotations = {});
} // namespace cyui
