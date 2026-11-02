#include "TextViewer.h"
#include "UiIdentity.h"
#include <TextEditor.h>
#include <iostream>
#include <stdexcept>
using namespace cyrevision;
static void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
int main(){try{
    TextViewer plain;plain.prepare("+ordinary log line\n-other text\n");
    require(!plain.document().diff && plain.codeDocument().text()=="+ordinary log line\n-other text\n","Plain log text became a diff.");
    TextViewer diff;diff.prepare("diff --git a/Config/DefaultEngine.ini b/Config/DefaultEngine.ini\n--- a/Config/DefaultEngine.ini\n+++ b/Config/DefaultEngine.ini\n@@ -7,2 +7,3 @@\n [Engine]\n-Enabled=False\n+Enabled=True\n+Workers=8\n");
    require(diff.document().diff && diff.document().path=="Config/DefaultEngine.ini","Git metadata was not adapted.");
    require(diff.document().added==2 && diff.document().removed==1,"Diff change counts were lost.");
    const auto count=diff.diffDocument().rows().size();diff.nextHunk(1);require(diff.hunkNumber()==1 && diff.diffDocument().rows().size()==count,"Hunk navigation changed the document.");
    diff.search("enabled");require(diff.matchCount()==2,"Host case-insensitive navigation was lost.");diff.nextMatch(1);require(diff.matchNumber()==2,"Next match did not advance.");
    require(diff.diffDocument().rows()[1].oldLine==7,"The CyUI diff lost original line numbers.");
    TextViewer large;large.prepare("--- /dev/null\n+++ b/large.json\n@@ -0,0 +1,1 @@\n+{\"value\":\""+std::string(1024*1024,'x')+"\"}\n[Preview truncated: host budget]\n");
    require(large.document().truncated && large.document().added==1,"Bounded preview metadata was not retained.");
    require(cyui::codeLanguageForPath("settings.JSON")==cyui::CodeLanguage::json && cyui::codeLanguageForPath("DefaultEngine.ini")==cyui::CodeLanguage::ini,"Shared syntax catalogue is not used.");
    require(cyui::codeLanguages().size()>=101,"Extended shared syntax catalogue was not linked.");
    auto* context=ImGui::CreateContext();
    {
        SourceEditor editor;const std::string text="\xc3\xa9\xf0\x9f\x98\x80\r\n\tX\r\nlast";
        editor.prepare("file-a",text,"sample.cpp",true);
        editor.editor().native().SelectRegion({0,1},{1,1});auto selected=editor.selectionUtf16();
        require(selected.first==1 && selected.second==6,"Selection did not preserve UTF-16 / UTF-8 / CRLF coordinates.");
        editor.prepare("file-b","Enabled=False","DefaultEngine.ini",false);
        editor.editor().native().ReplaceSectionText({0,8},{0,13},"True");
        require(editor.editor().text()=="Enabled=True" && editor.editor().native().CanUndo(),"Editor transaction did not commit.");
        auto undo=editor.editor().native().GetUndoIndex();editor.prepare("file-b",editor.editor().text(),"DefaultEngine.ini",false);
        require(editor.editor().native().GetUndoIndex()==undo,"A server echo cleared local Undo.");
        editor.editor().native().Undo();require(editor.editor().text()=="Enabled=False","Editor Undo failed.");
        editor.editor().native().Redo();require(editor.editor().text()=="Enabled=True","Editor Redo failed.");
        editor.prepare("file-identical","Enabled=True","DefaultEngine.ini",false);require(!editor.editor().native().CanUndo(),"An identical but distinct document inherited Undo.");
        editor.prepare("file-c","another","other.json",false);require(editor.editor().text()=="another" && !editor.editor().native().CanUndo(),"A new document retained stale Undo.");
    }
    ImGui::DestroyContext(context);
    require(uiIdentity("property-a")!=0 && uiIdentity("property-a")!=uiIdentity("property-b"),"UI IDs are not stable and distinct.");
    std::cout<<"CyUI adapter tests passed.\n";return 0;
}catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}}
