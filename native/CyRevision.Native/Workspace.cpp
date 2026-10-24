#include "Workspace.h"
#include "StringInput.h"
#include "Assets.h"
#include <imgui_internal.h>
#include <shellapi.h>
#include <algorithm>
#include <cctype>
#include <filesystem>
#include <map>

namespace cyrevision {
std::string text(const Json& object,const char* key,const std::string& fallback){auto value=object.find(key);return value!=object.end() && value->is_string()?value->get<std::string>():fallback;}
std::string display(const Json& value){if(value.is_null())return "";if(value.is_string())return value.get<std::string>();if(value.is_boolean())return value.get<bool>()?"Yes":"No";return value.dump();}
ImU32 statusColour(const std::string& value){std::string normalized=value;std::transform(normalized.begin(),normalized.end(),normalized.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});const auto& p=cyui::palette();if(normalized=="success" || normalized=="completed" || normalized=="open")return p.success;if(normalized=="merged")return p.accent;if(normalized=="failure" || normalized=="failed" || normalized=="error" || normalized=="conflict" || normalized=="closed")return p.error;if(normalized=="warning" || normalized=="queued" || normalized=="cancelled")return p.warning;if(normalized=="in_progress" || normalized=="running" || normalized=="modified")return p.info;return p.text;}
void TextViewer::draw(const char* id,const std::string& value){
    if(text_!=value){text_=value;lines_={0};for(size_t i=0;i<text_.size();++i)if(text_[i]=='\n')lines_.push_back(i+1);}
    cyui::ScopedFont font(cyui::monoFont());ImGui::BeginChild(id,ImVec2(0,0),ImGuiChildFlags_None,ImGuiWindowFlags_HorizontalScrollbar);
    ImGuiListClipper clip;clip.Begin(static_cast<int>(lines_.size()),ImGui::GetTextLineHeightWithSpacing());
    while(clip.Step())for(int line=clip.DisplayStart;line<clip.DisplayEnd;++line){size_t begin=lines_[line],end=line+1<static_cast<int>(lines_.size())?lines_[line+1]-1:text_.size();auto& p=cyui::palette();cyui::ScopedColor color(ImGuiCol_Text,begin<text_.size() && text_[begin]=='+'?p.success:begin<text_.size() && text_[begin]=='-'?p.error:p.text);ImGui::TextUnformatted(text_.data()+begin,text_.data()+end);}ImGui::EndChild();
}
Workspace::Workspace(EngineConnection& client,std::wstring backend):client_(client),backend_(std::move(backend)){workspace_="overview";table_="";send({{"command","catalog"}});}
unsigned long long Workspace::send(Json request){auto command=text(request,"command");bool operation=command!="state" && command!="catalog" && command!="cancel" && command!="prefs.cancel" && command!="set" && command!="select" && command!="itemSet" && command!="include" && text(request,"operation")!="cancel";
    if(command=="cancel")queuedRefreshes_.clear();
    if(operation){for(const auto& pending:pending_)if(pending.second==command)return 0;pendingAction_=true;}
    if(command=="overview.installUpdate")request["nativeProcessId"]=GetCurrentProcessId();
    Json drafts=request.value("edits",Json::object());if(command=="set")drafts[text(request,"property")]=request.value("value",Json());
    if(command!="state" && command!="catalog" && command!="project")request["projectId"]=projectId_;if(!request.contains("viewWorkspace"))request["viewWorkspace"]=workspace_;auto id=client_.send(request);if(id){pending_[id]=command;pendingRequests_[id]=std::move(request);if(!drafts.empty())submittedDrafts_[id]=std::move(drafts);}else{error_="The engine request queue is full.";pendingAction_=false;}return id;}
bool Workspace::busy()const{return !ready() || pendingAction_ || state_.value("operationBusy",state_.value("busy",false) && !state_.value("projectLoading",false)) || !client_.alive();}
bool Workspace::actionRunning(const std::string& name)const{
    if(queuedRefreshes_.count(name))return true;
    for(const auto& entry:pendingRequests_)if(text(entry.second,"action")==name)return true;
    if(state_.contains("activity") && text(state_["activity"],"action")==name)return true;
    return name=="RefreshAsync" && state_.value("projectLoading",false);
}
bool Workspace::selectionPending(const std::string& source)const{
    for(const auto& entry:pendingRequests_)if(text(entry.second,"command")=="select" && text(entry.second,"source")==source)return true;
    return false;
}
std::string Workspace::loadingLabel(const std::string& workspace)const{
    for(const auto& entry:queuedRefreshes_)if(workspace.empty() || text(entry.second,"viewWorkspace")==workspace)return "Waiting for current operation · "+text(action(entry.first),"label",entry.first);
    for(const auto& entry:pendingRequests_){auto& request=entry.second;auto command=text(request,"command");if(command=="state" || command=="catalog" || command=="set" || command=="navigation")continue;
        if(workspace.empty() || text(request,"viewWorkspace")==workspace){auto name=text(request,"action",command);return "Loading · "+(command=="select"?text(request,"source","selection"):text(action(name),"label",name));}}
    if(state_.contains("activity")){const auto& activity=state_["activity"];if(!text(activity,"command").empty() && (workspace.empty() || text(activity,"workspace")==workspace)){auto name=text(activity,"action");if(name.empty())name=text(activity,"command");return "Loading · "+text(action(name),"label",name);}}
    if(state_.contains("loading"))for(const auto& item:state_["loading"])if(workspace.empty() || text(item,"workspace")==workspace || text(item,"workspace")=="project")return text(item,"label","Loading…");
    if(state_.value("projectLoading",false))return text(state_,"status","Scanning project…");
    return "";
}
bool Workspace::loading(const std::string& workspace)const{return !loadingLabel(workspace).empty();}
bool Workspace::canSwitchProject()const{
    // A cancellable project scan must not lock project navigation. Mutating
    // operations still finish in their original project before we switch.
    return ready() && client_.alive() && !pendingAction_ && !state_.value("operationBusy",state_.value("busy",false) && !state_.value("projectLoading",false));
}
Json Workspace::action(const std::string& name)const{if(catalog_.contains("actions"))for(const auto& item:catalog_["actions"])if(text(item,"name")==name)return item;if(state_.contains("actions"))for(const auto& item:state_["actions"])if(text(item,"name")==name)return item;return {{"name",name},{"label",name},{"missing",true},{"parameters",Json::array()}};}
void Workspace::submitAction(const Json& item,const Json& parameters,bool confirmed){
    Json request=item.value("context",Json::object());request["command"]=text(item,"rpc","invoke");request["action"]=text(item,"name");request["parameters"]=parameters;request["confirmed"]=confirmed;request["edits"]=draftValues_;
    auto name=text(item,"name");if(busy() && name.rfind("Refresh",0)==0){request["projectId"]=projectId_;request["viewWorkspace"]=workspace_;request["edits"]=Json::object();queuedRefreshes_[name]=std::move(request);return;}
    pendingAction_=true;send(std::move(request));lastPoll_=0;
}
void Workspace::invoke(const Json& item){
    if((busy() && text(item,"name").rfind("Refresh",0)!=0) || item.value("missing",false))return;
    if(item.value("confirmation",false) || !item.value("parameters",Json::array()).empty()){
        action_=item;parameters_=Json::object();for(auto it=edits_.begin();it!=edits_.end();)if(it->first.rfind("parameter:",0)==0)it=edits_.erase(it);else++it;
        for(const auto& field:item.value("parameters",Json::array())){auto name=text(field,"name");if(field.contains("value") && !field["value"].is_null())parameters_[name]=field["value"];else parameters_[name]=text(field,"kind")=="bool"?Json(false):Json("");}
    }else submitAction(item,Json::object(),false);
}
void Workspace::menu(){
    if(!ImGui::BeginMainMenuBar())return;
    if(ImGui::BeginMenu("File")){if(ImGui::MenuItem("Open repository...","Ctrl+O",false,!busy()))invoke(action("AddExistingRepositoryAsync"));if(ImGui::MenuItem("Clone repository...",nullptr,false,!busy()))invoke(action("CloneGitRepositoryAsync"));ImGui::Separator();if(ImGui::MenuItem("Hide to tray","Alt+F4"))requestHide=true;if(ImGui::MenuItem("Quit CyRevision"))requestClose=true;ImGui::EndMenu();}
    if(ImGui::BeginMenu("Edit")){if(ImGui::MenuItem("Preferences"))showSettings_=true;ImGui::EndMenu();}
    if(ImGui::BeginMenu("View")){
        if(ImGui::MenuItem("Detach current tab",nullptr,false,ready()))openDetached(workspace_);
        if(ImGui::BeginMenu("Open workspace in window",ready())){if(catalog_.contains("workspaces"))for(const auto& tab:catalog_["workspaces"]){auto id=text(tab,"id");if(!workspaceAvailable(id))continue;if(ImGui::MenuItem((text(tab,"category")+" / "+text(tab,"label")).c_str()))openDetached(id);}ImGui::EndMenu();}
        if(ImGui::BeginMenu("Open windows",!detachedPanes_.empty())){for(auto& pane:detachedPanes_)if(pane.open){cyui::ScopedID id(static_cast<int>(pane.id));if(ImGui::MenuItem((pane.title.empty()?workspaceLabel(pane.workspace):pane.title).c_str()))pane.focus=true;}ImGui::Separator();if(ImGui::MenuItem("Close auxiliary views"))for(auto& pane:detachedPanes_)pane.open=false;ImGui::EndMenu();}
        if(ImGui::MenuItem("Reset panel layout")){layoutChecked_=false;resetLayout_=true;}ImGui::MenuItem("Advanced inspector",nullptr,&showInspector_);if(ImGui::MenuItem("Theme editor"))themeEditor_=true;if(ImGui::MenuItem("Classic interface"))ShellExecuteW(nullptr,L"open",backend_.c_str(),L"--classic",nullptr,SW_SHOWNORMAL);ImGui::EndMenu();}ImGui::EndMainMenuBar();
}
void Workspace::topBar(){
    auto actionsWidth=cyui::buttonWidth("Add folder")+cyui::buttonWidth("Open repository")+cyui::buttonWidth("Clone")+cyui::buttonWidth("Create repository")+cyui::dp(54)+3*ImGui::GetStyle().ItemSpacing.x;
    bool narrow=ImGui::GetMainViewport()->WorkSize.x<actionsWidth+cyui::dp(620);
    if(cyui::beginTopBar("##top",cyui::dp(narrow?92.0f:65.0f))){
        auto logoPath=std::filesystem::path(backend_).parent_path()/L"cyrevision-logo.png";auto logo=nativeTexture(utf8(logoPath.wstring()));
        if(logo){ImGui::Image(ImTextureRef(logo->id),cyui::dp(34,34));ImGui::SameLine();}
        ImGui::BeginGroup();cyui::heading("CyRevision",2);cyui::textFaint("v%s · V2 ALPHA",text(catalog_,"version").c_str());ImGui::EndGroup();ImGui::SameLine(0,cyui::dp(22));
        ImGui::BeginGroup();ImGui::TextUnformatted(text(state_,"project","Loading engine…").c_str());auto path=value("RepositoryPath");cyui::clippedText(ImGui::GetWindowDrawList(),ImGui::GetCursorScreenPos(),cyui::dp(245),cyui::palette().textFaint,path.c_str());ImGui::Dummy(ImVec2(cyui::dp(245),ImGui::GetTextLineHeight()));cyui::tooltip(path.c_str());ImGui::EndGroup();ImGui::SameLine();cyui::badge(text(state_,"branch").c_str());ImGui::SameLine();cyui::badge(text(state_,"mode").c_str());
        if(!narrow)ImGui::SameLine();cyui::alignRight(actionsWidth);
        command("AddFolderProjectAsync","Add folder");ImGui::SameLine();command("AddExistingRepositoryAsync","Open repository");ImGui::SameLine();command("CloneGitRepositoryAsync","Clone");ImGui::SameLine();{cyui::ScopedDisabled disabled(busy());if(cyui::primaryButton("Create repository"))openGitInitialization();}
    }cyui::endTopBar();
}
void Workspace::sidebar(){
    if(cyui::beginSidebar("##sidebar",244.0f)){
        cyui::sidebarHeader("PROJECTS");{cyui::ScopedDisabled disabled(busy());if(cyui::ghostButton("↑"))submitAction(action("MoveSelectedProjectAsync"),{{"offset",-1}},false);ImGui::SameLine();if(cyui::ghostButton("↓"))submitAction(action("MoveSelectedProjectAsync"),{{"offset",1}},false);}
        ImGui::BeginChild("##projects");
        std::map<std::string,std::vector<Json>> groups;if(state_.contains("projects"))for(const auto& project:state_["projects"])groups[text(project,"sidebarGroup","General")].push_back(project);
        for(auto& entry:groups){auto group=entry.first.empty()?"General":entry.first;cyui::ScopedID groupId(group.c_str());
            if(!ImGui::CollapsingHeader((group+" ("+std::to_string(entry.second.size())+")").c_str(),ImGuiTreeNodeFlags_DefaultOpen))continue;
            for(const auto& project:entry.second){auto id=text(project,"id");cyui::ScopedID scope(id.c_str());auto desired=!queuedProject_.empty()?queuedProject_:!openingProject_.empty()?openingProject_:projectId_;bool selected=id==desired;auto start=ImGui::GetCursorScreenPos();float width=ImGui::GetContentRegionAvail().x,height=cyui::dp(92);
                if(ImGui::Selectable("##project",selected,0,ImVec2(width,height)) && client_.alive() && !selected){queuedProject_=id;error_.clear();lastPoll_=0;}
                cyui::tooltip(id==queuedProject_ && !canSwitchProject()?"Project switch queued until the current operation finishes.":"Open this project. A background scan of the previous project will be cancelled.");
                auto end=ImGui::GetCursorScreenPos();auto& p=cyui::palette();ImU32 accent=p.accent;auto hex=text(project,"accentColor");if(hex.size()==7 && hex[0]=='#'){try{auto rgb=std::stoul(hex.substr(1),nullptr,16);accent=IM_COL32((rgb>>16)&255,(rgb>>8)&255,rgb&255,255);}catch(...) {}}
                ImGui::GetWindowDrawList()->AddRectFilled(start,ImVec2(start.x+cyui::dp(3),start.y+height),accent);
                ImGui::SetCursorScreenPos(ImVec2(start.x+cyui::dp(10),start.y+cyui::dp(5)));ImGui::BeginGroup();
                const char* type=project.value("git",false)?cyui::icon::code:project.value("sync",false)?cyui::icon::refresh:cyui::icon::folder;
                cyui::textColoured(accent,"%s %s",type,text(project,"name").c_str());ImGui::SameLine();auto badge=text(project,"notificationBadgeText");if(!badge.empty())cyui::badge(badge.c_str(),cyui::Tone::warning);
                if(id==queuedProject_ || id==openingProject_){ImGui::SameLine();cyui::badge(id==queuedProject_ && !canSwitchProject()?"Queued":"Opening…",cyui::Tone::info);}
                cyui::textFaint("%s",text(project,"rootPath").c_str());cyui::textColoured(accent,"%s",text(project,"branchName").c_str());ImGui::SameLine();cyui::textDim("%s",text(project,"loadDetail").c_str());
                cyui::textFaint("%s",text(project,"modeAndServices").c_str());
                if(project.value("git",false)){ImGui::SameLine();auto changes=project.value("changeCount",Json());cyui::textColoured(p.warning,"%s Δ · ↑%d ↓%d",changes.is_null()?"?":display(changes).c_str(),project.value("aheadCount",0),project.value("behindCount",0));}
                if(project.value("isLoading",false))cyui::progressBar(-1,cyui::dp(width-24,3),"Loading",cyui::Tone::accent);
                ImGui::EndGroup();ImGui::SetCursorScreenPos(end);ImGui::Dummy(ImVec2(0,0));
            }
        }ImGui::EndChild();
    }cyui::endSidebar();
}
void Workspace::defaultLayout(ImGuiID id){ImGui::DockBuilderRemoveNode(id);ImGui::DockBuilderAddNode(id,ImGuiDockNodeFlags_DockSpace);ImGui::DockBuilderSetNodeSize(id,ImGui::GetMainViewport()->WorkSize);ImGuiID centre=id;if(showOperations_){auto bottom=ImGui::DockBuilderSplitNode(centre,ImGuiDir_Down,.15f,nullptr,&centre);ImGui::DockBuilderDockWindow("Operations",bottom);}ImGui::DockBuilderDockWindow("Workspace",centre);ImGui::DockBuilderFinish(id);layoutChecked_=true;resetLayout_=false;}
void Workspace::tick(bool background){
    for(auto& response:client_.take()){
        auto id=response.value("id",0ULL);auto found=pending_.find(id);auto command=found==pending_.end()?"":found->second;if(found!=pending_.end())pending_.erase(found);pendingRequests_.erase(id);
        if(command=="state")requestingState_=false;
        pendingAction_=false;for(const auto& pending:pending_)if(pending.second!="state" && pending.second!="catalog" && pending.second!="cancel" && pending.second!="prefs.cancel" && pending.second!="set" && pending.second!="select" && pending.second!="itemSet" && pending.second!="include")pendingAction_=true;
        auto submitted=submittedDrafts_.find(id);
        if(!response.value("ok",false)){error_=text(response,"error","Engine request failed.");if(command=="project")openingProject_.clear();if(submitted!=submittedDrafts_.end())submittedDrafts_.erase(submitted);continue;}
        if(submitted!=submittedDrafts_.end()){for(const auto& entry:submitted->second.items()){if(draftValues_.contains(entry.key()) && draftValues_[entry.key()]==entry.value())draftValues_.erase(entry.key());auto draft=edits_.find(entry.key());if(draft!=edits_.end() && draft->second==display(entry.value()))edits_.erase(draft);}submittedDrafts_.erase(submitted);}
        const auto result=response.value("result",Json::object());if(command=="catalog")catalog_=result;
        else if(command=="state"){
            if(text(result,"workspaceId")!=workspace_)continue;state_=result;++stateGeneration_;projectId_=text(state_,"projectId");
            if(projectId_==openingProject_)openingProject_.clear();
            if(projectId_!=lastProject_){lastProject_=projectId_;edits_.clear();draftValues_=Json::object();tableViews_.clear();requestedTables_=Json::object();requestedFields_.clear();detail_.clear();
                for(auto& pane:detachedPanes_)pane.needsLoad=true;
                queuedRefreshes_.clear();if(state_.contains("navigation")){auto& nav=state_["navigation"];workspace_=text(nav,"activeWorkspace","overview");category_=text(nav,"activeCategory","Overview");}openWorkspace_=true;selectTable_=true;
            }
        }else{lastPoll_=0;
            if(command=="overview.installUpdate" && result.contains("installPlan")){
                const auto& plan=result["installPlan"];auto executable=wide(text(plan,"executablePath"));auto arguments=wide(text(plan,"arguments"));
                if(plan.value("parentProcessId",0UL)!=GetCurrentProcessId() || executable.empty() || arguments.empty()){error_="The update launch plan is no longer valid.";continue;}
                std::wstring line=L"\""+executable+L"\" "+arguments;STARTUPINFOW startup{};startup.cb=sizeof(startup);startup.dwFlags=STARTF_USESHOWWINDOW;startup.wShowWindow=SW_HIDE;PROCESS_INFORMATION process{};
                if(CreateProcessW(executable.c_str(),line.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&process)){CloseHandle(process.hThread);CloseHandle(process.hProcess);done_=true;}else error_="The update helper could not start. CyRevision remains open.";
            }
        }
    }
    if(!queuedProject_.empty() && !requestClose && canSwitchProject()){
        auto target=queuedProject_;queuedProject_.clear();
        if(target!=projectId_ || !openingProject_.empty()){
            auto request=send({{"command","project"},{"targetProjectId",target}});
            if(request){openingProject_=target;lastPoll_=0;}
        }
    }
    if(!queuedRefreshes_.empty() && queuedProject_.empty() && openingProject_.empty() && !requestClose && !busy()){
        auto entry=queuedRefreshes_.begin();auto request=entry->second;queuedRefreshes_.erase(entry);
        if(text(request,"projectId")==projectId_)send(std::move(request));
    }
    auto now=GetTickCount64();auto interval=background?2000ULL:loading("") || busy()?250ULL:800ULL;if(!requestingState_ && now-lastPoll_>=interval && client_.alive()){
        Json tables=Json::array(),fields=Json::array();for(const auto& name:requestedFields_)fields.push_back(name);for(const auto& item:requestedTables_.items())tables.push_back(item.value());
        Json views=Json::array();for(const auto& pane:detachedPanes_)if(pane.open && workspaceAvailable(pane.workspace) && std::find(views.begin(),views.end(),Json(pane.workspace))==views.end())views.push_back(pane.workspace);
        send({{"command","state"},{"workspace",workspace_},{"workspaces",views},{"table",table_},{"fields",fields},{"tables",tables}});requestingState_=true;lastPoll_=now;
    }
    if(openWorkspace_ && ready() && !busy()){pendingAction_=true;send({{"command","workspace"},{"workspace",workspace_}});openWorkspace_=false;}
    loadDetachedWorkspaces();
}
void Workspace::frame(){
    tick();
    // Scoped semantic colours keep all nested selected tabs readable in every theme.
    cyui::ScopedColor selectedTab(ImGuiCol_TabSelected,cyui::palette().accent);
    cyui::ScopedColor selectedDimmedTab(ImGuiCol_TabDimmedSelected,cyui::palette().accentDim);
    cyui::ScopedColor hoveredTab(ImGuiCol_TabHovered,cyui::palette().accentHover);
    requestedFields_.clear();menu();topBar();if(cyui::beginStatusBar("##status")){auto activity=loadingLabel("");if(!activity.empty()){cyui::spinner("##statusLoading",4);ImGui::SameLine();}cyui::statusText(!error_.empty()?cyui::Tone::error:!activity.empty() || busy()?cyui::Tone::warning:cyui::Tone::success,"%s",!error_.empty()?error_.c_str():!activity.empty()?activity.c_str():text(state_,"status","Connecting to engine…").c_str());ImGui::SameLine();if(cyui::ghostButton(showOperations_?"Hide details":"Task details"))showOperations_=!showOperations_;}cyui::endStatusBar();
    sidebar();auto dock=cyui::dockSpace();mainDock_=dock;if(!layoutChecked_){auto node=ImGui::DockBuilderGetNode(dock);if(resetLayout_ || node==nullptr)defaultLayout(dock);else layoutChecked_=true;}
    requestedTables_=Json::object();workspacePanel();detachedWindows();if(showInspector_)inspector();operations();modals();reviewLogWindows();gitInitializationWizard();gitConflictEditor();gitBranchInspector();
    for(auto& entry:enlargedText_)if(entry.second){ImGui::SetNextWindowSize(cyui::dp(1100,700),ImGuiCond_FirstUseEver);ImGuiWindowClass windowClass;windowClass.ParentViewportId=static_cast<ImGuiID>(-1);windowClass.ViewportFlagsOverrideSet=ImGuiViewportFlags_NoAutoMerge;windowClass.ViewportFlagsOverrideClear=ImGuiViewportFlags_NoTaskBarIcon;ImGui::SetNextWindowClass(&windowClass);activePane_="aux:"+entry.first;if(ImGui::Begin(entry.first.c_str(),&entry.second)){if(entry.first=="File history")fileHistoryWindow();else if(entry.first.rfind("text:",0)==0)textViews_[entry.first].draw("##fullText",value(entry.first.substr(5)));}ImGui::End();activePane_.clear();}
    if(themeEditor_)cyui::showThemeEditor(&themeEditor_);if(showSettings_)preferences();
    // Confirmation dialogs must never become invisible input blockers when a
    // command is invoked from an auxiliary window while the main one is minimized.
    if(auto* modal=ImGui::GetTopMostPopupModal()){auto handle=static_cast<HWND>(ImGui::GetMainViewport()->PlatformHandle);if(handle && IsIconic(handle)){ShowWindow(handle,SW_RESTORE);RECT client{};GetClientRect(handle,&client);POINT origin{};ClientToScreen(handle,&origin);ImGui::SetWindowPos(modal,ImVec2(origin.x+(client.right-modal->Size.x)*.5f,origin.y+(client.bottom-modal->Size.y)*.5f),ImGuiCond_Always);ImGui::FocusWindow(modal);}}
    shortcuts();cyui::renderNotifications();
}
}
