#include "TextViewer.h"
#include <TextEditor.h>
#include <algorithm>
#include <cctype>

namespace cyrevision {
namespace {
std::string fold(std::string value){
    std::transform(value.begin(),value.end(),value.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});return value;
}
std::size_t utf16Position(const std::string& text,std::size_t line,std::size_t glyph){
    std::size_t byte=0,units=0,row=0,column=0;
    while(byte<text.size() && (row<line || column<glyph)){
        auto c=static_cast<unsigned char>(text[byte++]);
        if(c=='\n'){++row;column=0;++units;if(row>line)break;}
        else if((c&0xc0)!=0x80){units+=c>=0xf0?2:1;if(c!='\r')++column;}
    }
    return units;
}
}
void TextViewer::prepare(const std::string& content,const std::string& path){
    auto syntax=cyui::codeLanguageRevision();
    if(prepared_ && source_==content && hint_==path && syntaxRevision_==syntax)return;
    const bool changed=!prepared_ || source_!=content || hint_!=path;
    prepared_=true;source_=content;hint_=path;syntaxRevision_=syntax;
    summary_={};summary_.path=path;
    auto hunk=content.rfind("@@ -",0)==0?0:content.find("\n@@ -");
    if(hunk!=std::string::npos && content[hunk]=='\n')++hunk;
    summary_.diff=hunk!=std::string::npos && (content.rfind("diff --git ",0)==0 || content.rfind("--- ",0)==0 || content.rfind("@@ -",0)==0);
    if(summary_.path.empty()){
        auto header=content.rfind("+++ b/",0)==0?0:content.find("\n+++ b/");
        if(header!=std::string::npos){if(content[header]=='\n')++header;header+=6;summary_.path=content.substr(header,content.find_first_of("\r\n",header)-header);}
    }
    summary_.truncated=summary_.diff && content.find("\n[Preview truncated:")!=std::string::npos;
    const auto language=cyui::codeLanguageForPath(summary_.path);
    if(summary_.diff){
        // Git's file envelope is already represented by the host header. Preserve multi-file boundaries.
        const bool multi=content.find("\ndiff --git ")!=std::string::npos;
        diff_.setUnified(multi?content:content.substr(hunk),language);
        int maxOld=0,maxNew=0;
        for(const auto& row:diff_.rows()){summary_.added+=row.kind==cyui::DiffKind::added;summary_.removed+=row.kind==cyui::DiffKind::removed;maxOld=std::max(maxOld,row.oldLine);maxNew=std::max(maxNew,row.newLine);}
        gutterColumns_=static_cast<unsigned int>(std::max<size_t>(5,std::to_string(maxOld).size())+1+std::max<size_t>(5,std::to_string(maxNew).size()));
    }else code_.setText(content,language);
    if(changed){const bool split=diffState_.split;codeState_={};diffState_={};diffState_.split=split;codeState_.focusLine=0;diffState_.focus=0;hunk_=-1;}
    findMatches();
}
void TextViewer::findMatches(){
    matches_.clear();match_=-1;auto needle=fold(query_);
    if(!needle.empty()){
        if(summary_.diff){for(size_t i=0;i<diff_.rows().size();++i)if(fold(diff_.rows()[i].code.text).find(needle)!=std::string::npos)matches_.push_back(static_cast<int>(i));}
        else for(size_t i=0;i<code_.lines().size();++i)if(fold(code_.lines()[i].text).find(needle)!=std::string::npos)matches_.push_back(static_cast<int>(i));
    }
    if(!matches_.empty()){match_=0;if(summary_.diff)diffState_.focus=diffState_.selected=matches_[0];else codeState_.focusLine=matches_[0];}
}
void TextViewer::search(const std::string& query){if(query_!=query){query_=query;findMatches();}}
void TextViewer::nextMatch(int direction){
    if(matches_.empty())return;match_=(match_+direction+static_cast<int>(matches_.size()))%static_cast<int>(matches_.size());
    if(summary_.diff)diffState_.focus=diffState_.selected=matches_[match_];else codeState_.focusLine=matches_[match_];
}
void TextViewer::nextHunk(int direction){
    auto count=static_cast<int>(diff_.hunks().size());if(!summary_.diff || !count)return;
    hunk_=hunk_<0?(direction>0?0:count-1):(hunk_+direction+count)%count;
    diffState_.focus=diff_.hunks()[hunk_];diffState_.hunk=hunk_;
}
void TextViewer::drawPrepared(const char* id,std::optional<bool> split){
    if(source_.empty()){cyui::emptyState(cyui::tr("No preview").c_str(),cyui::tr("Select a file or revision to inspect its contents.").c_str(),cyui::icon::document);return;}
    if(summary_.diff){
        if(split)diffState_.split=*split;
        // CyUI 1.2 exposes a fixed 88dp gutter, not a gutter-width option. Fit its
        // number format through the supported scoped-font API; never copy its renderer.
        cyui::ScopedFont mono(cyui::monoFont());
        const float columns=static_cast<float>(diffState_.split?std::max(6u,gutterColumns_/2):gutterColumns_);
        const float factor=std::min(1.0f,cyui::dp(80)/(std::max(1.0f,ImGui::CalcTextSize("M").x)*columns));
        cyui::ScopedFont fit(nullptr,factor);
        diffState_.query=query_;cyui::diffViewer(id,diffState_,diff_);search(diffState_.query);hunk_=diffState_.hunk;
        auto selected=std::find(matches_.begin(),matches_.end(),diffState_.selected);if(selected!=matches_.end())match_=static_cast<int>(selected-matches_.begin());
    }else{
        codeState_.query=query_;codeState_.matches=matches_;codeState_.match=match_;
        codeState_.searchedQuery=query_;codeState_.searchedRevision=code_.revision();
        cyui::codeViewer(id,codeState_,code_);if(codeState_.query!=query_)search(codeState_.query);else match_=codeState_.match;
    }
    // CyUI's builtin next-search is case sensitive. Keep the host's existing case-insensitive
    // Enter/Shift+Enter and previous-match/hunk commands without duplicating its renderer.
    if(ImGui::IsWindowFocused(ImGuiFocusedFlags_ChildWindows) && ImGui::IsKeyPressed(ImGuiKey_Enter,false))nextMatch(ImGui::GetIO().KeyShift?-1:1);
}
void SourceEditor::prepare(const std::string& identity,const std::string& text,const std::string& path,bool readOnly){
    if(identity_!=identity || source_!=text){if(identity_!=identity)editor_.native().SetText(text);identity_=identity;source_=text;editor_.setText(text);}
    if(path_!=path){path_=path;editor_.setLanguageForPath(path);}
    editor_.setReadOnly(readOnly);
}
std::pair<size_t,size_t> SourceEditor::selectionUtf16(){
    const auto selection=editor_.native().GetMainCursorSelection();
    auto begin=utf16Position(source_,selection.start.line,selection.start.index),end=utf16Position(source_,selection.end.line,selection.end.index);
    return std::minmax(begin,end);
}
}
