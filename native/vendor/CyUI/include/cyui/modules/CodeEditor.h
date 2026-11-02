#pragma once
#include "cyui/modules/Code.h"
#include <memory>
class IMGUI_API TextEditor;

namespace cyui {
// Optional MIT-licensed editor backend: multi-cursor, undo/redo, search, wrapping,
// language colorization, completion callbacks and annotations. Never reads a file.
class CYUI_API CodeEditor {
  public:
    CodeEditor();
    ~CodeEditor();
    CodeEditor(const CodeEditor &) = delete;
    CodeEditor &operator=(const CodeEditor &) = delete;
    void setText(const std::string &);
    std::string text() const;
    void setLanguage(CodeLanguage);
    CodeLanguage language() const;
    void setLanguageForPath(std::string_view);
    // Enable/disable common spans when a host supplies richer native overlays.
    void setSyntaxEnabled(bool);
    void refreshSyntax();
    void setReadOnly(bool);
    bool draw(const char *id, ImVec2 size = {}); // true when the document changed
    TextEditor &native();                        // advanced host hooks; include TextEditor.h when used
  private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
} // namespace cyui
