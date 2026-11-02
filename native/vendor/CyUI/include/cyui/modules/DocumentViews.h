#pragma once
#include "cyui/modules/Code.h"
#include <unordered_map>

namespace cyui {
enum class DocumentBlockKind { paragraph, heading, quote, list, code, rule, table };
enum class DocumentInlineKind { text, code, strong, emphasis, link };
// One inline run, parsed once by setMarkdown. `text` is shown verbatim; `target` is a link's URL.
struct DocumentInline {
    DocumentInlineKind kind = DocumentInlineKind::text;
    std::string text, target;
    bool spaced = false; // whitespace precedes it in the source
};
struct DocumentBlock {
    DocumentBlockKind kind = DocumentBlockKind::paragraph;
    int level = 0;
    std::string text, language;
    CodeDocument code;
    std::vector<std::vector<std::string>> rows;
    std::vector<DocumentInline> inlines;                        // paragraph and list text
    std::vector<std::vector<std::vector<DocumentInline>>> cells; // table rows, parsed
};
class CYUI_API Document {
  public:
    bool setMarkdown(const std::string &);
    const std::vector<DocumentBlock> &blocks() const {
        return blocks_;
    }

  private:
    std::string text_;
    std::vector<DocumentBlock> blocks_;
};
struct DocumentState {
    std::vector<CodeViewState> codeViews;
};
struct DocumentResult {
    std::string link;
    int copiedBlock = -1;
};
CYUI_API DocumentResult documentView(const char *id, DocumentState &, const Document &, ImVec2 size = {});
struct TranscriptMessage {
    ItemId id = 0;
    std::string role, content, time;
};
struct TranscriptRowCache {
    float height = 0;
    std::size_t signature = 0; // role and time
};
struct TranscriptState {
    std::unordered_map<ItemId, Document> documents;
    std::unordered_map<ItemId, DocumentState> views;
    bool follow = true;
    std::size_t count = 0;
    // Measured message heights: offscreen messages only reserve them. Remeasured when the layout
    // (width, font size, frame height, item spacing) or a message's role/time/content changes.
    std::unordered_map<ItemId, TranscriptRowCache> heights;
    ImVec4 layout{};
};
struct TranscriptResult {
    ItemId copy = 0; // message to copy, 0 for none: give messages nonzero IDs
    std::string link;
};
CYUI_API TranscriptResult transcript(const char *id, TranscriptState &, ArrayView<TranscriptMessage>,
                            ImVec2 size = {});
} // namespace cyui
