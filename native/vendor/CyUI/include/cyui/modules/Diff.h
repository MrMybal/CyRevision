#pragma once
#include "cyui/modules/Code.h"

namespace cyui {
enum class DiffKind { context, added, removed, hunk, notice };
struct DiffRow {
    DiffKind kind = DiffKind::context;
    int oldLine = 0, newLine = 0;
    CodeLine code;
    std::size_t inlineStart = 0, inlineEnd = 0;
};
struct DiffPair {
    int left = -1, right = -1;
};
class CYUI_API DiffDocument {
  public:
    void setTexts(const std::string &before, const std::string &after, CodeLanguage = CodeLanguage::plain,
                  std::size_t workBudget = 4000000);
    void setUnified(const std::string &, CodeLanguage = CodeLanguage::plain);
    const std::vector<DiffRow> &rows() const {
        return rows_;
    }
    const std::vector<DiffPair> &pairs() const {
        return pairs_;
    }
    const std::vector<int> &hunks() const {
        return hunks_;
    }
    bool approximate() const {
        return approximate_;
    }
    std::uint64_t revision() const {
        return revision_;
    }
    std::size_t columns() const {
        return columns_;
    }

  private:
    void prepare(CodeLanguage);
    std::vector<DiffRow> rows_;
    std::vector<DiffPair> pairs_;
    std::vector<int> hunks_;
    std::size_t columns_ = 0;
    bool approximate_ = false;
    std::uint64_t revision_ = 0;
};
struct DiffState {
    bool split = true;
    float scroll = 0;
    int selected = -1, focus = -1, hunk = -1;
    std::string query;
};
struct DiffResult {
    int row = -1, oldLine = 0, newLine = 0;
};
CYUI_API DiffResult diffViewer(const char *id, DiffState &, const DiffDocument &, ImVec2 size = {});
} // namespace cyui
