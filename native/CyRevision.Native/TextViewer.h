#pragma once
#include <cyui/Core.h>
// Windows' calling-convention macro collides with the shared syntax catalogue.
#pragma push_macro("pascal")
#undef pascal
#include <cyui/modules/Code.h>
#include <cyui/modules/Diff.h>
#include <cyui/modules/CodeEditor.h>
#pragma pop_macro("pascal")
#include <string>
#include <vector>
#include <optional>

namespace cyrevision {
// Repository metadata and host navigation only. Parsing, syntax and drawing belong to CyUI.
struct TextSummary {
    std::string path;
    bool diff=false,truncated=false;
    int added=0,removed=0;
};
class TextViewer {
    std::string source_,hint_,query_;
    cyui::CodeDocument code_;
    cyui::DiffDocument diff_;
    cyui::CodeViewState codeState_;
    cyui::DiffState diffState_{false};
    TextSummary summary_;
    std::vector<int> matches_;
    int match_=-1,hunk_=-1;
    std::uint64_t syntaxRevision_=0;
    bool prepared_=false;
    unsigned int gutterColumns_=11;
    void findMatches();
public:
    void prepare(const std::string& content,const std::string& path="");
    void search(const std::string& query);
    void nextMatch(int direction);
    void nextHunk(int direction);
    int matchCount()const{return static_cast<int>(matches_.size());}
    int matchNumber()const{return match_+1;}
    int hunkNumber()const{return hunk_+1;}
    const TextSummary& document()const{return summary_;}
    const cyui::DiffDocument& diffDocument()const{return diff_;}
    const cyui::CodeDocument& codeDocument()const{return code_;}
    const std::string& query()const{return query_;}
    void setSplit(bool split){diffState_.split=split;}
    bool split()const{return diffState_.split;}
    void drawPrepared(const char* id,std::optional<bool> split=std::nullopt);
    void draw(const char* id,const std::string& content){prepare(content);drawPrepared(id);}
};

// The only editor-specific adapter translates selection coordinates to the managed UTF-16 API.
class SourceEditor {
    std::string identity_,source_,path_;
    cyui::CodeEditor editor_;
public:
    void prepare(const std::string& identity,const std::string& text,const std::string& path,bool readOnly);
    bool draw(const char* id,ImVec2 size={}){return editor_.draw(id,size);}
    cyui::CodeEditor& editor(){return editor_;}
    std::pair<size_t,size_t> selectionUtf16();
};
}
