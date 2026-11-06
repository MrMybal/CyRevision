#include "Workspace.h"
#include "UiIdentity.h"
#include "Assets.h"
#include "StringInput.h"
#include <algorithm>

namespace cyrevision {
SourceEditor& Workspace::sourceEditor(const std::string& id){
    auto& view=sourceEditors_[tableKey(id)];if(!view)view=std::make_unique<SourceEditor>();return *view;
}
void Workspace::imageView(const std::string& path,const char* id,ImVec2 size){
    auto texture=nativeTexture(path);
    if(!texture){cyui::emptyState(cyui::tr("No image preview").c_str(),cyui::tr("Choose an image or an asset with a rendered preview.").c_str(),cyui::icon::photo);return;}
    auto& view=imageViews_[tableKey(std::to_string(ImGui::GetID(id)))];
    if(view.path!=path || view.texture!=texture->id){view.path=path;view.texture=texture->id;view.state={};}
    auto available=ImGui::GetContentRegionAvail();
    if(size.x<=0)size.x=std::max(cyui::dp(40),available.x);
    if(size.y<=0)size.y=std::max(cyui::dp(40),available.y);
    cyui::imageViewport(id,view.state,{texture->id,texture->width,texture->height},size);
}
void Workspace::documentLink(const std::string& link){
    if(link.rfind("doc:",0)==0){openDocumentation(link);return;}
    // A document can propose navigation, never invoke an arbitrary shell scheme.
    const bool scheme=link.rfind("https://",0)==0 || link.rfind("http://",0)==0;
    const auto authorityEnd=link.find('/',link.find("://")+3);
    const bool credential=link.substr(0,authorityEnd).find('@')!=std::string::npos;
    if(!scheme || credential || link.size()>4096 || std::any_of(link.begin(),link.end(),[](unsigned char c){return c<32 || c==127;})){
        cyui::notify(cyui::tr("This link cannot be opened from a document.").c_str(),cyui::Tone::warning);return;
    }
    pendingDocumentLink_=link;
}
void Workspace::openDocumentation(const std::string& topic,const std::string& anchor,const std::string& media){
    send({{"command","docs.open"},{"topicId",topic},{"anchor",anchor},{"mediaId",media}});lastPoll_=0;
}
void Workspace::documentationPanel(){
    cyui::heading(cyui::tr("CyDoc documentation").c_str(),2);
    ImGui::TextWrapped("%s",cyui::tr("Open the complete local guide in your browser. No server or Internet connection is required. Images enlarge inside the reader.").c_str());
    cyui::space(8);
    const auto docs=state_.value("documentation",Json::object());
    {cyui::ScopedDisabled disabled(!docs.value("available",false));if(cyui::primaryButton(cyui::trLabel("Open user guide").c_str()))openDocumentation("getting-started");}
    ImGui::SameLine();if(cyui::button("English"))cyui::setLocale("en");ImGui::SameLine();if(cyui::button("Français"))cyui::setLocale("fr");
    cyui::space(8);
    if(cyui::button("Git / LFS"))openDocumentation("git-workflow");ImGui::SameLine();if(cyui::button("PR / CI"))openDocumentation("ci-actions");
    if(cyui::button(cyui::trLabel("Preferences").c_str()))openDocumentation("preferences");
    ImGui::SameLine();if(cyui::button(cyui::trLabel("Plugin API").c_str()))openDocumentation("plugin-api");
    cyui::space(8);ImGui::TextWrapped("%s",cyui::tr("F1 opens help for the current view. Search, history, language and reading positions are managed by CyDoc.").c_str());
}
void Workspace::documentPanel(const char* property,const char* id){
    cyui::ScopedID scope(id);auto content=value(property);auto& view=documents_[tableKey(id)];view.document.setMarkdown(content);
    if(cyui::iconButton(cyui::icon::copy,cyui::tr("Copy Markdown source").c_str()))ImGui::SetClipboardText(content.c_str());
    ImGui::SameLine();if(cyui::iconButton(cyui::icon::document,cyui::tr("Open source in a separate window").c_str()))openDetached(workspace_,"text",property,property);
    if(ImGui::BeginTabBar("##document-mode")){
        if(ImGui::BeginTabItem(cyui::trLabel("Rendered").c_str())){auto result=cyui::documentView("##document",view.state,view.document);if(!result.link.empty())documentLink(result.link);ImGui::EndTabItem();}
        if(ImGui::BeginTabItem(cyui::trLabel("Source").c_str())){auto& reader=textViews_[tableKey(std::string(id)+":source")];reader.prepare(content,"document.md");reader.drawPrepared("##source");ImGui::EndTabItem();}
        ImGui::EndTabBar();
    }
}
void Workspace::transcriptPanel(const char* source,const char* id,ImVec2 size){
    cyui::ScopedID scope(id);auto& paging=tableViews_[tableKey(std::string(source)+":transcript")];
    requestTable(source,50,paging.skip,"",tableKey(std::string(source)+":transcript"));
    const auto& table=viewTable(source,tableKey(std::string(source)+":transcript"));const int total=table.value("total",0);
    if(paging.skip>=total && paging.skip>0){paging.skip=total?((total-1)/50)*50:0;lastPoll_=0;}
    {cyui::ScopedDisabled disabled(paging.skip==0);if(cyui::ghostButton(cyui::trLabel("Previous messages").c_str())){paging.skip=std::max(0,paging.skip-50);lastPoll_=0;}}
    ImGui::SameLine();{cyui::ScopedDisabled disabled(paging.skip+50>=total);if(cyui::ghostButton(cyui::trLabel("Next messages").c_str())){paging.skip+=50;lastPoll_=0;}}
    ImGui::SameLine();cyui::textDim("%d–%d / %d",total?paging.skip+1:0,std::min(paging.skip+50,total),total);
    std::vector<cyui::TranscriptMessage> messages;
    if(table.contains("rows"))for(const auto& row:table["rows"]){const auto& values=row["values"];
        auto body=text(values,"Body",text(values,"Text",text(values,"Message")));
        auto attachment=text(values,"AttachmentName");if(!attachment.empty())body+="\n\n"+cyui::tr("Attachment")+": "+attachment;
        messages.push_back({uiIdentity(std::string(source)+":"+text(row,"key")),text(values,"Author",text(values,"RoleLabel",text(values,"Role"))),body,text(values,"CreatedText",text(values,"TimestampText",text(values,"SentAt")))});
    }
    auto result=cyui::transcript("##transcript",transcripts_[tableKey(id)],messages,size);
    if(!result.link.empty())documentLink(result.link);
    if(result.copy)for(const auto& message:messages)if(message.id==result.copy)ImGui::SetClipboardText(message.content.c_str());
}
}
