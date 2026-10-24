#include "Workspace.h"
#include <imgui_internal.h>
#include <algorithm>

namespace cyrevision {
void releaseGitViewState(const Workspace*,const std::string&);
void releaseLfsViewState(const Workspace*,const std::string&);
void releaseWidgetViewState(const std::string&);
std::string Workspace::tableKey(const std::string& source)const{return activePane_.empty()?source:activePane_+":"+source;}
const Json& Workspace::viewTable(const std::string& source,const std::string& slot)const{
    static const Json empty=Json::object();auto key=slot.empty()?tableKey(source):slot;
    if(state_.contains("tables") && state_["tables"].contains(key) && state_["tables"][key].is_object())return state_["tables"][key];
    return empty;
}
std::string Workspace::workspaceLabel(const std::string& workspace)const{
    if(catalog_.contains("workspaces"))for(const auto& tab:catalog_["workspaces"])if(text(tab,"id")==workspace)return text(tab,"label",workspace);
    return workspace;
}
bool Workspace::workspaceAvailable(const std::string& workspace)const{
    if(state_.contains("navigation"))for(const auto& tab:state_["navigation"].value("tabs",Json::array()))if(text(tab,"id")==workspace)return tab.value("available",true);
    return false;
}
void Workspace::openDetached(const std::string& workspace,const std::string& kind,const std::string& target,const std::string& title){
    for(auto& pane:detachedPanes_)if(pane.open && pane.workspace==workspace && pane.kind==kind && pane.target==target){pane.focus=true;if(!pane.floating){pane.floating=true;pane.undock=true;}return;}
    if(std::count_if(detachedPanes_.begin(),detachedPanes_.end(),[](const auto& pane){return pane.open;})>=16){error_="Close an auxiliary view before opening another one (16 views maximum).";return;}
    DetachedPane pane;pane.id=nextPane_++;pane.workspace=workspace;pane.kind=kind;pane.target=target;pane.title=title;
    detachedPanes_.push_back(std::move(pane));lastPoll_=0;
}
void Workspace::tabDetachMenu(const std::string& workspace){
    if(ImGui::IsItemHovered() && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left))openDetached(workspace);
    cyui::ScopedID id(workspace.c_str());
    cyui::ScopedColor menuText(ImGuiCol_Text,cyui::palette().text);
    if(ImGui::BeginPopupContextItem("##detachTab")){
        if(ImGui::MenuItem("Open in separate window"))openDetached(workspace);
        ImGui::EndPopup();
    }
    cyui::tooltip("Double-click or right-click to open this workspace in a separate window.");
}
void Workspace::loadDetachedWorkspaces(){
    if(!ready() || busy() || openWorkspace_)return;
    for(auto& pane:detachedPanes_)if(pane.open && pane.needsLoad && workspaceAvailable(pane.workspace)){
        if(send({{"command","workspace"},{"workspace",pane.workspace},{"detached",true},{"viewWorkspace",pane.workspace}}))pane.needsLoad=false;
        break;
    }
}
void Workspace::workspaceBody(){
    auto activity=loadingLabel(workspace_);if(!activity.empty()){loadingIndicator(activity);ImGui::SameLine();if(cyui::ghostButton("Cancel loading###workspaceCancel"))send({{"command","cancel"}});cyui::progressBar(-1,cyui::dp(-1,3),"",cyui::Tone::accent);}
    if(!overviewView() && !gitView() && !pullRequestView() && !ciView() && !toolsView())cyui::emptyState("Select a project workspace","Choose a tab above.",cyui::icon::folder);
}
void Workspace::detachedWindows(){
    const auto count=detachedPanes_.size();
    for(size_t index=0;index<count;++index){auto& pane=detachedPanes_[index];if(!pane.open)continue;
        auto label=(pane.title.empty()?workspaceLabel(pane.workspace):pane.title)+" · "+text(state_,"project","CyRevision");
        auto window=label+"###Detached-"+std::to_string(pane.id);
        ImGui::SetNextWindowSize(cyui::dp(1120,720),ImGuiCond_FirstUseEver);
        ImGui::SetNextWindowPos(ImVec2(ImGui::GetMainViewport()->WorkPos.x+cyui::dp(80+static_cast<float>(pane.id%4)*25),ImGui::GetMainViewport()->WorkPos.y+cyui::dp(90+static_cast<float>(pane.id%4)*25)),ImGuiCond_FirstUseEver);
        ImGuiWindowClass windowClass;windowClass.ParentViewportId=static_cast<ImGuiID>(-1);
        if(pane.floating)windowClass.ViewportFlagsOverrideSet=ImGuiViewportFlags_NoAutoMerge;
        windowClass.ViewportFlagsOverrideClear=ImGuiViewportFlags_NoTaskBarIcon;
        ImGui::SetNextWindowClass(&windowClass);
        if(pane.dock){ImGui::SetNextWindowDockID(mainDock_,ImGuiCond_Always);pane.dock=false;}
        if(pane.undock){ImGui::SetNextWindowDockID(0,ImGuiCond_Always);pane.undock=false;}
        if(pane.focus){ImGui::SetNextWindowFocus();ImGui::SetNextWindowCollapsed(false);pane.focus=false;}
        if(ImGui::Begin(window.c_str(),&pane.open,ImGuiWindowFlags_NoCollapse)){
            if(ImGui::IsWindowDocked())pane.floating=false;
            cyui::textDim("%s · %s · %s",text(state_,"project").c_str(),workspaceLabel(pane.workspace).c_str(),text(state_,"branch").c_str());
            auto showMain=[](){auto handle=static_cast<HWND>(ImGui::GetMainViewport()->PlatformHandle);if(handle){if(IsIconic(handle))ShowWindow(handle,SW_RESTORE);SetForegroundWindow(handle);}};
            if(cyui::ghostButton("Return to main tab")){showMain();reattachWorkspace_=pane.workspace;pane.open=false;}
            ImGui::SameLine();if(cyui::ghostButton("Dock in main window")){showMain();pane.floating=false;pane.dock=true;}
            ImGui::SameLine();if(cyui::ghostButton("Separate window")){pane.floating=true;pane.undock=true;pane.focus=true;}
            ImGui::SameLine();if(cyui::ghostButton("Reload view")){pane.needsLoad=true;lastPoll_=0;}
            cyui::tooltip("All views share the active project and the same operation safeguards.");
            ImGui::Separator();
            if(!error_.empty()){cyui::callout(cyui::Tone::error,error_.c_str());if(cyui::ghostButton("Dismiss error"))error_.clear();}
            if(!ready())loadingIndicator("Starting engine…");
            else if(!workspaceAvailable(pane.workspace))cyui::emptyState("Unavailable in this project mode","Switch to a compatible project or close this view.",cyui::icon::info);
            else if(pane.open){
                const auto savedWorkspace=workspace_,savedPane=activePane_;
                workspace_=pane.workspace;activePane_="view-"+std::to_string(pane.id);
                ImGui::BeginChild("##detachedBody");
                if(pane.kind!="workspace"){auto activity=loadingLabel(workspace_);if(!activity.empty())loadingIndicator(activity);}
                if(pane.kind=="workspace")workspaceBody();
                else if(pane.kind=="list")list(pane.target.c_str());
                else if(pane.kind=="diff")diffPanel(pane.target.c_str(),"detached-diff");
                else if(pane.kind=="image")imagePanel(pane.target.c_str(),"detached-image");
                else textPanel(pane.target.c_str(),"detached-text");
                ImGui::EndChild();
                workspace_=savedWorkspace;activePane_=savedPane;
            }
        }ImGui::End();
    }
    for(auto it=detachedPanes_.begin();it!=detachedPanes_.end();)if(!it->open){
        auto prefix="view-"+std::to_string(it->id)+":";
        auto scope="view-"+std::to_string(it->id);releaseGitViewState(this,scope);releaseLfsViewState(this,scope);releaseWidgetViewState(prefix);
        for(auto entry=tableViews_.begin();entry!=tableViews_.end();)if(entry->first.rfind(prefix,0)==0)entry=tableViews_.erase(entry);else++entry;
        for(auto entry=textViews_.begin();entry!=textViews_.end();)if(entry->first.rfind(prefix,0)==0)entry=textViews_.erase(entry);else++entry;
        for(auto entry=textSearch_.begin();entry!=textSearch_.end();)if(entry->first.rfind(prefix,0)==0)entry=textSearch_.erase(entry);else++entry;
        it=detachedPanes_.erase(it);
    }else++it;
    if(!reattachWorkspace_.empty()){auto workspace=reattachWorkspace_;reattachWorkspace_.clear();auto drafts=draftValues_;auto edits=edits_;navigate(workspace);draftValues_=std::move(drafts);edits_=std::move(edits);selectTable_=true;}
}
Json Workspace::windowLayout()const{
    Json layout=Json::array();for(const auto& pane:detachedPanes_)if(pane.open)layout.push_back({{"id",pane.id},{"workspace",pane.workspace},{"kind",pane.kind},{"target",pane.target},{"title",pane.title},{"floating",pane.floating}});return layout;
}
void Workspace::restoreWindowLayout(const Json& layout){
    if(!layout.is_array())return;
    for(const auto& item:layout){if(detachedPanes_.size()>=16)break;if(!item.is_object())continue;
        auto kind=text(item,"kind"),workspace=text(item,"workspace"),target=text(item,"target"),title=text(item,"title");
        if(workspace.empty() || workspace.size()>80 || target.size()>160 || title.size()>256 || (kind!="workspace" && kind!="list" && kind!="text" && kind!="diff" && kind!="image") || (kind!="workspace" && target.empty()))continue;
        if(!item.contains("id") || !item["id"].is_number_unsigned())continue;auto id=item["id"].get<unsigned long long>();if(!id || id>1000000)continue;
        if(std::any_of(detachedPanes_.begin(),detachedPanes_.end(),[&](const auto& pane){return pane.id==id;}))continue;
        DetachedPane pane;pane.id=id;pane.workspace=workspace;pane.kind=kind;pane.target=target;pane.title=title;pane.floating=!item.contains("floating") || !item["floating"].is_boolean() || item["floating"].get<bool>();pane.focus=false;
        detachedPanes_.push_back(std::move(pane));nextPane_=std::max(nextPane_,id+1);
    }
}
bool Workspace::hasAuxiliaryWindows()const{return !detachedPanes_.empty() || std::any_of(enlargedText_.begin(),enlargedText_.end(),[](const auto& entry){return entry.second;});}
size_t Workspace::detachedPlatformWindowCount()const{
    size_t count=0;for(const auto& pane:detachedPanes_)if(pane.open){auto name="###Detached-"+std::to_string(pane.id);auto* window=ImGui::FindWindowByName(name.c_str());if(window && window->ViewportOwned && window->Viewport && window->Viewport->PlatformHandle){auto handle=static_cast<HWND>(window->Viewport->PlatformHandle);if(IsWindowVisible(handle) && !IsIconic(handle))++count;}}return count;
}
}
