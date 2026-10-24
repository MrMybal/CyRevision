#include "Workspace.h"
#include <imgui_internal.h>
#include <iostream>
#include <stdexcept>
#include <thread>
#include <chrono>

using namespace cyrevision;

namespace cyrevision {
struct WorkspaceTestAccess {
    static bool running(const Workspace& workspace,const char* action){return workspace.actionRunning(action);}
    static std::string activity(const Workspace& workspace){return workspace.loadingLabel("");}
    static void command(Workspace& workspace){workspace.command("RefreshAsync","Refresh fixture");}
    static void cancel(Workspace& workspace){workspace.send({{"command","cancel"}});}
    static std::string workspace(const Workspace& workspace){return workspace.workspace_;}
    static void closeWindows(Workspace& workspace){for(auto& pane:workspace.detachedPanes_)pane.open=false;}
    static void filterWindow(Workspace& workspace,const char* source,const char* filter){auto id=workspace.detachedPanes_.front().id;workspace.tableViews_["view-"+std::to_string(id)+":"+source].filter=filter;workspace.lastPoll_=0;}
    static void returnWindow(Workspace& workspace){workspace.reattachWorkspace_=workspace.detachedPanes_.front().workspace;workspace.detachedPanes_.front().open=false;}
    static void dockWindow(Workspace& workspace){auto& pane=workspace.detachedPanes_.front();pane.floating=false;pane.dock=true;}
    static void openList(Workspace& workspace){workspace.openDetached("history","list","History","Commit list");}
    static void setDraft(Workspace& workspace){workspace.draftValues_["CommitMessage"]="Unfinished message";workspace.edits_["CommitMessage"]="Unfinished message";}
    static bool draftPreserved(const Workspace& workspace){return workspace.draftValues_.value("CommitMessage","")=="Unfinished message" && workspace.edits_.at("CommitMessage")=="Unfinished message";}
};
}

namespace {
std::string uiError;
class FakeEngine final : public EngineConnection {
    unsigned long long next_=1;
    std::vector<Json> responses_;
public:
    int mutations=0;
    int projectSwitches=0;
    std::string activeProject="fixture-one";
    bool projectLoading=false,operationBusy=false;
    std::string selectedRevision="revision-one",comparisonRevision="revision-two";
    std::vector<Json> selections;
    bool holdInvocations=false;
    std::vector<Json> held;
    Json lastState=Json::object();
    void complete(){for(auto& response:held)responses_.push_back(std::move(response));held.clear();}
    unsigned long long send(Json request) override {
        auto id=next_++;
        request["id"]=id; // Match the real IPC transport's reserved correlation ID.
        Json result=Json::object();
        auto command=text(request,"command");
        if(command=="catalog") {
            result={
                {"version","test"},
                {"actions",Json::array({{{"name","AddExistingRepositoryAsync"},{"label","Open repository"},{"confirmation",false},
                    {"parameters",Json::array({{{"name","path"},{"label","Path"},{"kind","text"},{"optional",false}}})}}})},
                {"workspaces",Json::array({
                    {{"id","overview"},{"label","Project"},{"category","Overview"},{"tables",Json::array()}},
                    {{"id","members"},{"label","Members"},{"category","Overview"},{"tables",Json::array()}},
                    {{"id","changes"},{"label","Changes"},{"category","Git"},{"tables",Json::array()}},
                    {{"id","history"},{"label","History"},{"category","Git"},{"tables",Json::array()}}
                })}
            };
            result["actions"].push_back({{"name","RefreshAsync"},{"label","Refresh"},{"confirmation",false},{"parameters",Json::array()}});
        } else if(command=="state") {
            lastState=request;
            result={{"workspaceId",text(request,"workspace")},{"ready",true},{"busy",projectLoading || operationBusy},{"projectLoading",projectLoading},{"operationBusy",operationBusy},{"status","Ready"},
                {"projectId",activeProject},{"project","Fixture"},{"branch","main"},{"mode","Git - BETA"},
                {"projects",Json::array({
                    {{"id","fixture-one"},{"name","Project One"},{"rootPath","fixture/one"},{"branchName","main"},{"sidebarGroup","General"},{"git",true},{"changeCount",12},{"aheadCount",2},{"unreadNotificationCount",1},{"notificationBadgeText","1"}},
                    {{"id","fixture-two"},{"name","Project Two"},{"rootPath","fixture/two"},{"branchName","dev"},{"sidebarGroup","General"},{"sync",true},{"changeCount",4},{"aheadCount",0},{"unreadNotificationCount",0}}
                })},{"operations",Json::array()},{"properties",Json::array()},{"actions",Json::array()},
                {"fields",Json::object()},{"tables",Json::object()},
                {"navigation",{{"activeWorkspace","overview"},{"activeCategory","Overview"},{"categoryTabs",Json::object()},
                    {"tabs",Json::array({
                        {{"id","overview"},{"label","Project"},{"category","Overview"},{"visible",true},{"available",true}},
                        {{"id","members"},{"label","Members"},{"category","Overview"},{"visible",true},{"available",true}},
                        {{"id","changes"},{"label","Changes"},{"category","Git"},{"visible",true},{"available",true}},
                        {{"id","history"},{"label","History"},{"category","Git"},{"visible",true},{"available",true}}
                    })}}},
                {"preview",""},{"table",{{"name",text(request,"table")},{"skip",0},{"total",0},{"columns",Json::array({"Name"})},{"rows",Json::array()}}}};
            if(request.contains("tables"))for(const auto& table:request["tables"]){auto source=text(table,"source");
                auto key=text(table,"key",source);result["tables"][key]={{"total",0},{"columns",Json::array({"Name"})},{"rows",Json::array()}};
                if(source=="History")result["tables"][key]={{"total",2},{"columns",Json::array({"Subject"})},{"rows",Json::array({
                    {{"index",0},{"key","revision-one"},{"values",{{"Subject","First commit"},{"ShortHash","1111111"}}}},
                    {{"index",1},{"key","revision-two"},{"values",{{"Subject","Second commit"},{"ShortHash","2222222"}}}}})}};
                result["tables"][key]["filter"]=text(table,"filter");
                if(!text(table,"filter").empty() && source=="History"){Json rows=Json::array();for(const auto& row:result["tables"][key]["rows"])if(text(row["values"],"Subject").find(text(table,"filter"))!=std::string::npos)rows.push_back(row);result["tables"][key]["rows"]=rows;result["tables"][key]["total"]=rows.size();}
            }
        } else if(command=="invoke") ++mutations;
        else if(command=="project") {activeProject=text(request,"targetProjectId");++projectSwitches;}
        else if(command=="select"){selections.push_back(request);if(text(request,"source")=="History"){
            if(text(request,"selection")=="SelectedComparisonRevision")comparisonRevision=text(request,"key");
            else selectedRevision=text(request,"key");}}
        Json response={{"id",id},{"ok",true},{"result",std::move(result)}};
        if(command=="invoke" && holdInvocations)held.push_back(std::move(response));else responses_.push_back(std::move(response));
        return id;
    }
    std::vector<Json> take() override { std::vector<Json> result;result.swap(responses_);return result; }
    bool alive() const override { return true; }
};

void require(bool condition,const char* message) {
    if(!condition) throw std::runtime_error(message);
}
void frame(Workspace& workspace) {
    ImGui::GetIO().DeltaTime=1.0f/60.0f;
    ImGui::NewFrame();workspace.frame();ImGui::Render();
    if(!uiError.empty())throw std::runtime_error(uiError);
}
void settle(Workspace& workspace) {for(int index=0;index<4;++index) frame(workspace);}
void key(Workspace& workspace,ImGuiKey code) {
    ImGui::GetIO().AddKeyEvent(code,true);frame(workspace);
    ImGui::GetIO().AddKeyEvent(code,false);settle(workspace);
}
void openAction(Workspace& workspace) {
    auto& io=ImGui::GetIO();
    io.AddKeyEvent(ImGuiMod_Ctrl,true);io.AddKeyEvent(ImGuiKey_O,true);frame(workspace);
    io.AddKeyEvent(ImGuiKey_O,false);io.AddKeyEvent(ImGuiMod_Ctrl,false);settle(workspace);
    auto* modal=ImGui::GetTopMostPopupModal();
    require(modal && std::string(modal->Name)=="Action","The requested action dialog did not open.");
}
ImGuiTabBar* tabBar(const char* id) {
    auto* window=ImGui::FindWindowByName("Workspace");
    require(window!=nullptr,"The project workspace was not rendered.");
    auto* bar=ImGui::GetCurrentContext()->TabBars.GetByKey(window->GetID(id));
    require(bar!=nullptr,"The two-level project navigation is missing.");
    require(bar->Window==window,"Project tools were moved outside the selected project workspace.");
    return bar;
}
void requireTabs(const char* id,const std::vector<std::string>& names) {
    auto* bar=tabBar(id);
    require(bar->Tabs.Size==static_cast<int>(names.size()),"Unexpected project/category tab count.");
    for(int index=0;index<bar->Tabs.Size;++index)
        require(std::string(ImGui::TabBarGetTabName(bar,&bar->Tabs[index]))==names[index],"A project tab is missing or grouped under the wrong category.");
}
void clickTab(Workspace& workspace,const char* id,const char* name) {
    auto* bar=tabBar(id);
    for(auto& tab:bar->Tabs)if(std::string(ImGui::TabBarGetTabName(bar,&tab))==name){
        auto& io=ImGui::GetIO();
        io.AddMousePosEvent(bar->BarRect.Min.x+tab.Offset-bar->ScrollingAnim+tab.Width*0.5f,bar->BarRect.GetCenter().y);frame(workspace);
        io.AddMouseButtonEvent(0,true);frame(workspace);
        io.AddMouseButtonEvent(0,false);settle(workspace);return;
    }
    throw std::runtime_error("The requested project tab was not found.");
}
void refreshSnapshot(Workspace& workspace){std::this_thread::sleep_for(std::chrono::milliseconds(810));settle(workspace);}
void clickProject(Workspace& workspace,int row){
    ImGuiWindow* projects=nullptr;for(auto* window:ImGui::GetCurrentContext()->Windows)if(std::string(window->Name).find("##sidebar/##projects_")==0)projects=window;
    require(projects!=nullptr,"The projects list is missing.");
    auto spacing=ImGui::GetStyle().ItemSpacing.y;
    auto y=projects->DC.CursorStartPos.y+ImGui::GetFrameHeight()+spacing+row*(cyui::dp(92)+2*spacing)+cyui::dp(46);
    auto& io=ImGui::GetIO();io.AddMousePosEvent(projects->WorkRect.Min.x+cyui::dp(35),y);frame(workspace);
    io.AddMouseButtonEvent(0,true);frame(workspace);io.AddMouseButtonEvent(0,false);settle(workspace);
}
void clickHistoryRow(Workspace& workspace,int row){
    ImGuiWindow* body=nullptr;for(auto* window:ImGui::GetCurrentContext()->Windows)if(std::string(window->Name).find("Workspace/##workspaceBody_")==0)body=window;
    require(body!=nullptr,"The history panel is missing.");
    ImGuiTable* table=nullptr;for(auto& candidate:ImGui::GetCurrentContext()->Tables.Buf)if(candidate.ColumnsCount==1 && candidate.ColumnsNames.size()>0 && std::string(candidate.ColumnsNames.c_str())=="Subject")table=&candidate;
    require(table!=nullptr,"The history list did not render.");
    auto& io=ImGui::GetIO();auto height=ImGui::GetTextLineHeight()+2*ImGui::GetStyle().CellPadding.y;
    io.AddMousePosEvent(table->OuterRect.Min.x+cyui::dp(30),table->OuterRect.Min.y+height*(1.5f+row));frame(workspace);
    io.AddMouseButtonEvent(0,true);frame(workspace);io.AddMouseButtonEvent(0,false);settle(workspace);
}
ImRect refreshButtonFrame(Workspace& workspace){
    ImGui::GetIO().DeltaTime=1.0f/60.0f;ImGui::NewFrame();workspace.frame();
    ImGui::SetNextWindowPos(ImVec2(1100,220),ImGuiCond_Always);ImGui::SetNextWindowSize(ImVec2(260,110),ImGuiCond_Always);
    ImGui::Begin("Refresh test fixture");WorkspaceTestAccess::command(workspace);auto rect=ImGui::GetCurrentContext()->LastItemData.Rect;ImGui::End();ImGui::Render();
    require(uiError.empty(),"Loading indicator produced an invalid ImGui layout.");return rect;
}
void clickRefresh(Workspace& workspace){
    auto rect=refreshButtonFrame(workspace);auto& io=ImGui::GetIO();io.AddMousePosEvent(rect.GetCenter().x,rect.GetCenter().y);refreshButtonFrame(workspace);
    io.AddMouseButtonEvent(0,true);refreshButtonFrame(workspace);io.AddMouseButtonEvent(0,false);refreshButtonFrame(workspace);
}
}

int main() {
    try {
        IMGUI_CHECKVERSION();ImGui::CreateContext();
        ImGui::GetCurrentContext()->ErrorCallback=[](ImGuiContext*,void*,const char* message){uiError=message;};
        auto& io=ImGui::GetIO();io.DisplaySize=ImVec2(1500,960);io.IniFilename=nullptr;
        io.ConfigFlags|=ImGuiConfigFlags_DockingEnable|ImGuiConfigFlags_NavEnableKeyboard;
        cyui::apply();cyui::loadFonts();
        unsigned char* pixels{};int width{},height{};
        io.Fonts->GetTexDataAsRGBA32(&pixels,&width,&height);
        FakeEngine engine;Workspace workspace(engine,L"not-used.exe");
        settle(workspace);
        require(workspace.ready(),"The fixture did not load.");
        require(ImGui::GetTopMostPopupModal()==nullptr,"An unrequested modal is blocking the interface at startup.");
        require(ImGui::GetCurrentContext()->OpenPopupStack.empty(),"A popup opened without a user action.");

        // Projects remain in the sidebar; categories and tools belong to the active project.
        auto* sidebar=ImGui::FindWindowByName("##sidebar");
        auto* projectWindow=ImGui::FindWindowByName("Workspace");
        require(sidebar!=nullptr && projectWindow!=nullptr,"The projects pane or project workspace is missing.");
        require(projectWindow->Pos.x>=sidebar->Pos.x+sidebar->Size.x-cyui::dp(2),"The project workspace overlaps the projects-only sidebar.");
        requireTabs("##categories",{"Overview","Git"});
        requireTabs("##projectTabs",{"Project","Members"});
        clickTab(workspace,"##categories","Git");
        requireTabs("##projectTabs",{"Changes","History"});
        require(engine.projectSwitches==0,"Opening a project tool switched to another project.");
        require(ImGui::GetTopMostPopupModal()==nullptr,"Changing project tabs created a blocking modal.");
        clickTab(workspace,"##projectTabs","History");refreshSnapshot(workspace);
        clickHistoryRow(workspace,1);
        require(engine.selectedRevision=="revision-two","The history list changed the comparison selector instead of the inspected commit.");
        engine.projectLoading=true;refreshSnapshot(workspace);clickHistoryRow(workspace,0);
        require(engine.selectedRevision=="revision-one","A background scan silently discarded a history selection.");
        engine.projectLoading=false;refreshSnapshot(workspace);
        engine.operationBusy=true;refreshSnapshot(workspace);clickRefresh(workspace);clickRefresh(workspace);
        require(engine.mutations==0,"A queued refresh ran concurrently with an exclusive operation.");
        require(WorkspaceTestAccess::running(workspace,"RefreshAsync") && WorkspaceTestAccess::activity(workspace).find("Waiting")!=std::string::npos,"Queued refresh has no visible activity state.");
        engine.holdInvocations=true;engine.operationBusy=false;refreshSnapshot(workspace);
        require(engine.mutations==1 && WorkspaceTestAccess::running(workspace,"RefreshAsync"),"The queued refresh did not start once the engine was free.");
        clickRefresh(workspace);require(engine.mutations==1,"A running refresh was launched twice.");
        engine.complete();engine.holdInvocations=false;settle(workspace);
        require(!WorkspaceTestAccess::running(workspace,"RefreshAsync"),"A completed refresh remained stuck in loading.");
        engine.operationBusy=true;refreshSnapshot(workspace);clickRefresh(workspace);WorkspaceTestAccess::cancel(workspace);settle(workspace);
        engine.operationBusy=false;refreshSnapshot(workspace);require(engine.mutations==1,"Cancel replayed a queued refresh.");engine.mutations=0;
        clickTab(workspace,"##categories","Overview");
        requireTabs("##projectTabs",{"Project","Members"});

        // Exercise real ImGui hit testing with mouse events, without desktop input.
        auto* menu=ImGui::FindWindowByName("##MainMenuBar");
        require(menu!=nullptr,"The menu bar was not rendered.");
        io.AddMousePosEvent(menu->Pos.x+cyui::dp(18),menu->Pos.y+menu->Size.y*.5f);frame(workspace);
        io.AddMouseButtonEvent(0,true);frame(workspace);
        io.AddMouseButtonEvent(0,false);settle(workspace);
        require(!ImGui::GetCurrentContext()->OpenPopupStack.empty(),"Clicking File did not open its menu.");
        require(ImGui::GetTopMostPopupModal()==nullptr,"A modal intercepted the menu click.");
        key(workspace,ImGuiKey_Escape);

        openAction(workspace);key(workspace,ImGuiKey_Escape);
        require(ImGui::GetTopMostPopupModal()==nullptr,"Cancel did not release the action dialog.");
        settle(workspace);
        require(ImGui::GetCurrentContext()->OpenPopupStack.empty(),"The cancelled action dialog reopened.");
        require(engine.mutations==0,"Cancelling a dialog invoked an action.");

        workspace.requestClose=true;settle(workspace);
        auto* quit=ImGui::GetTopMostPopupModal();
        require(quit && std::string(quit->Name)=="Quit CyRevision?","The quit confirmation did not open on request.");
        key(workspace,ImGuiKey_Escape);
        require(!workspace.done() && !workspace.requestClose,"Cancelling quit did not keep the application open.");
        require(ImGui::GetTopMostPopupModal()==nullptr,"The cancelled quit dialog reopened.");

        openAction(workspace);workspace.requestClose=true;settle(workspace);
        quit=ImGui::GetTopMostPopupModal();
        require(quit && std::string(quit->Name)=="Quit CyRevision?","An action prevented quit confirmation.");
        require(ImGui::GetCurrentContext()->OpenPopupStack.Size==1,"Two modal dialogs are competing for input.");
        key(workspace,ImGuiKey_Escape);
        require(ImGui::GetCurrentContext()->OpenPopupStack.empty(),"A stale dialog returned after cancelling quit.");
        require(engine.mutations==0,"A confirmation was submitted without consent.");
        clickProject(workspace,1);
        require(engine.activeProject=="fixture-two" && engine.projectSwitches==1,"Clicking another project did not select it.");
        engine.projectLoading=true;refreshSnapshot(workspace);
        clickProject(workspace,0);
        require(engine.activeProject=="fixture-one" && engine.projectSwitches==2,"A background project scan silently blocked project selection.");
        engine.projectLoading=false;refreshSnapshot(workspace);
        engine.operationBusy=true;refreshSnapshot(workspace);
        clickProject(workspace,1);clickProject(workspace,1);
        require(engine.activeProject=="fixture-one" && engine.projectSwitches==2,"Project selection interrupted an active mutation.");
        clickProject(workspace,0);
        engine.operationBusy=false;refreshSnapshot(workspace);
        require(engine.activeProject=="fixture-one" && engine.projectSwitches==2,"A superseded project click was replayed.");
        engine.operationBusy=true;refreshSnapshot(workspace);clickProject(workspace,1);
        engine.operationBusy=false;refreshSnapshot(workspace);
        require(engine.activeProject=="fixture-two" && engine.projectSwitches==3,"The queued project did not open after the operation finished.");
        cyui::applyDpi(1.5f);settle(workspace);
        auto* scaledSidebar=ImGui::FindWindowByName("##sidebar");
        require(scaledSidebar && std::abs(scaledSidebar->Size.x-cyui::dp(244))<2.0f,"The project sidebar was scaled twice at 150% DPI.");
        clickProject(workspace,0);
        require(engine.activeProject=="fixture-one" && engine.projectSwitches==4,"Project hit testing failed at 150% DPI.");
        cyui::applyDpi(1.0f);settle(workspace);
        // Detached workspaces use the same engine/project, not a second application.
        auto mainWorkspace=WorkspaceTestAccess::workspace(workspace);
        workspace.openWorkspaceWindow("history");settle(workspace);refreshSnapshot(workspace);
        require(workspace.windowLayout().size()==1,"Opening a detached workspace did not create a view.");
        workspace.openWorkspaceWindow("history");settle(workspace);
        require(workspace.windowLayout().size()==1,"Opening the same workspace created an unwanted duplicate window.");
        auto paneId=workspace.windowLayout()[0]["id"].get<unsigned long long>();
        auto* detached=ImGui::FindWindowByName(("###Detached-"+std::to_string(paneId)).c_str());
        require(detached && (detached->WindowClass.ViewportFlagsOverrideSet&ImGuiViewportFlags_NoAutoMerge),"A separate view can merge invisibly back into the main OS window.");
        require(WorkspaceTestAccess::workspace(workspace)==mainWorkspace,"Opening a detached view replaced the main project tab.");
        WorkspaceTestAccess::filterWindow(workspace,"History","Second");refreshSnapshot(workspace);
        auto viewKey="view-"+std::to_string(paneId)+":History";bool foundFilter=false;
        for(const auto& table:engine.lastState["tables"])if(text(table,"key")==viewKey)foundFilter=text(table,"filter")=="Second";
        require(foundFilter,"The detached list did not send its own filter/view key.");
        require(engine.lastState["workspace"]==mainWorkspace && std::find(engine.lastState["workspaces"].begin(),engine.lastState["workspaces"].end(),Json("history"))!=engine.lastState["workspaces"].end(),"Detached and main workspaces were not subscribed together.");
        WorkspaceTestAccess::openList(workspace);settle(workspace);
        require(workspace.windowLayout().size()==2,"A list could not be opened separately from its workspace.");
        auto savedLayout=Json::parse(workspace.windowLayout().dump());WorkspaceTestAccess::closeWindows(workspace);settle(workspace);
        require(!workspace.hasAuxiliaryWindows() && !workspace.done(),"Closing auxiliary views quit the application or left stale windows.");
        workspace.restoreWindowLayout(savedLayout);settle(workspace);
        require(workspace.windowLayout().size()==2,"Saved auxiliary windows did not restore.");
        WorkspaceTestAccess::dockWindow(workspace);settle(workspace);
        detached=ImGui::FindWindowByName(("###Detached-"+std::to_string(paneId)).c_str());
        require(detached && detached->DockId!=0,"A detached view could not dock in the main window.");
        WorkspaceTestAccess::setDraft(workspace);WorkspaceTestAccess::returnWindow(workspace);settle(workspace);
        require(WorkspaceTestAccess::workspace(workspace)=="history" && workspace.windowLayout().size()==1,"Returning to the main tab lost the workspace or failed to close its detached copy.");
        require(WorkspaceTestAccess::draftPreserved(workspace),"Returning from a detached view discarded an unfinished form.");
        WorkspaceTestAccess::closeWindows(workspace);settle(workspace);
        for(auto theme:{"mid-grey","light-grey","amber-night","violet","navy"}){cyui::setTheme(theme);settle(workspace);require(uiError.empty(),"A theme produced an invalid interface layout.");}
        ImGui::DestroyContext();
        std::cout<<"PASS: project selection, loading/queue protection, 150% DPI, tabs, detached views, independent filters, docking, layout restoration, preserved drafts, five themes and confirmations.\n";
        return 0;
    } catch(const std::exception& error) {
        std::cerr<<"FAIL: "<<error.what()<<"\n";
        if(ImGui::GetCurrentContext()) ImGui::DestroyContext();
        return 1;
    }
}
