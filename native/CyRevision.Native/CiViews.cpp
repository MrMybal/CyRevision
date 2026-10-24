#include "Workspace.h"
#include "ReviewViews.h"
#include "StringInput.h"

namespace cyrevision {
bool Workspace::ciView() {
    if(workspace_!="ci") return false;
    const auto& context=reviewContext(state_);
    const auto& run=reviewObject(context,"ciRun");
    auto& ui=reviewUi(projectId_);
    cyui::panelHeader("Continuous integration",value("CiStatus").c_str());
    command("RefreshCiAsync","Refresh workflows and runs",true);
    if(flag("IsCiLoading")) {ImGui::SameLine();cyui::spinner("##ciLoading");cyui::textDim("Loading workflow data...");}
    cyui::divider();
    ImVec2 available=ImGui::GetContentRegionAvail();
    float left=std::min(cyui::dp(260),std::max(cyui::dp(180),available.x*.22f));
    ImGui::BeginChild("##ciDispatch",ImVec2(left,0),ImGuiChildFlags_Borders);
    cyui::sectionTitle("Start a workflow");
    selector("CiWorkflows","SelectedCiWorkflow","Workflow");
    field("CiGitRef","Git ref");
    {cyui::ScopedDisabled disabled(busy() || !context.value("canDispatchCi",false));
    command("DispatchSelectedCiWorkflowAsync","Run selected workflow");}
    cyui::tooltip("Dispatch the selected workflow on the entered branch or tag. The provider checks your Actions write permission.");
    cyui::divider();cyui::sectionTitle("Create a release");
    cyui::textDim("Dispatch the release workflow with its version input. No local tag or commit is created.");
    field("CiReleaseVersion","Release version");
    {cyui::ScopedDisabled disabled(busy() || flag("IsCiLoading"));command("DispatchReleaseCiWorkflowAsync","Build release on CI");}
    cyui::divider();
    cyui::textFaint("Workflow dispatch, cancellation and reruns require permissions on the remote provider. An authentication error is shown in the status rather than reported as success.");
    ImGui::EndChild();ImGui::SameLine();
    ImGui::BeginChild("##ciMain",ImVec2(0,0));
    available=ImGui::GetContentRegionAvail();
    float runs=reviewSplitWidth("##ciRunSplitter",ui.ciRunsSplit,available.x,210);
    ImGui::BeginChild("##ciRuns",ImVec2(runs,0),ImGuiChildFlags_Borders);
    cyui::sectionTitle("Recent runs");list("CiRuns");ImGui::EndChild();
    ImGui::SameLine(0,cyui::dp(7));ImGui::BeginChild("##ciDetails",ImVec2(0,0),ImGuiChildFlags_Borders);
    if(run.empty()) cyui::emptyState("Select a workflow run","Select a recent run to inspect its jobs, errors, complete log and details.",cyui::icon::code);
    else {
        cyui::heading(text(run,"name").c_str(),2);reviewStatus(text(run,"stateText"));ImGui::SameLine();
        cyui::textDim("#%lld - %s",run.value("id",0LL),text(run,"branch").c_str());
        Json runContext={{"runId",run.value("id",0LL)}};
        {cyui::ScopedDisabled disabled(busy());if(cyui::ghostButton("Open run")){auto request=runContext;request["command"]="openCiRun";send(request);}}
        ImGui::SameLine();
        {cyui::ScopedDisabled disabled(busy() || !context.value("canRerunCi",false));
        if(cyui::ghostButton("Rerun failed..."))invoke(reviewRpc("Rerun failed jobs","ciRerun",runContext,"Queue only the failed jobs of this selected workflow run again?"));}
        ImGui::SameLine();
        {cyui::ScopedDisabled disabled(busy() || !context.value("canCancelCi",false));
        if(cyui::dangerButton("Cancel run..."))invoke(reviewRpc("Cancel workflow run","ciCancel",runContext,"Cancel the complete workflow run, including every queued and running job? This requires Actions write permission."));}
        if(ImGui::BeginTabBar("##ciDetailsTabs")) {
            if(ImGui::BeginTabItem("Overview")) {
                float height=ImGui::GetContentRegionAvail().y;
                cyui::sectionTitle("Jobs and failures");
                list("CiJobs",ImVec2(0,std::max(cyui::dp(110),height*.31f)));
                cyui::sectionTitle("Error summary");
                ImGui::BeginChild("##ciErrors",ImVec2(0,std::max(cyui::dp(75),height*.19f)),ImGuiChildFlags_Borders);
                textPanel("CiRunDetails","##ciErrorReport");ImGui::EndChild();
                cyui::sectionTitle("Run details");textPanel("CiRunDescription","##ciRunReport");
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Full log")){ciLogPanel(false);ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }
    }
    ImGui::EndChild();ImGui::EndChild();
    return true;
}

void Workspace::ciLogPanel(bool pullRequest,bool detached) {
    auto& ui=reviewUi(projectId_);
    cyui::ScopedID scope(pullRequest?"prFullLog":"ciFullLog");
    if(!detached) {
        if(cyui::ghostButton("Open full log in large window")) {
            if(pullRequest)ui.prLogWindow=true;else ui.ciLogWindow=true;
        }
        cyui::tooltip("Open a separate, resizable native window. Log tables preserve horizontal scrolling and one consistent monospace font.");
    }
    else {
        bool* topmost=pullRequest?&ui.prLogTopmost:&ui.ciLogTopmost;
        cyui::toggleSwitch("Always on top",topmost);
        cyui::tooltip("Keep this detached log window above other application windows.");
    }
    field(pullRequest?"PullRequestCiLogFilterMode":"CiLogFilterMode","Lines");
    cyui::textDim("%s",value(pullRequest?"PullRequestCiStatus":"CiStatus").c_str());
    cyui::textFaint("Search the number, source or complete line text. Error/Warning filters apply together with the search.");
    cyui::ScopedFont mono(cyui::monoFont());
    list(pullRequest?"FilteredPullRequestCiLogLines":"FilteredCiLogLines");
}

void Workspace::reviewLogWindows() {
    auto& ui=reviewUi(projectId_);
    const auto savedPane=activePane_;
    if(ui.ciLogWindow || ui.prLogWindow) {
        // Subscribe while detached, even after the main workspace changes.
        for(bool pr:{false,true}) {
            bool* open=pr?&ui.prLogWindow:&ui.ciLogWindow;
            if(!*open)continue;
            activePane_=pr?"aux-pr-log":"aux-ci-log";
            ImGui::SetNextWindowSize(cyui::dp(1180,700),ImGuiCond_FirstUseEver);
            ImGuiWindowClass windowClass;
            windowClass.ParentViewportId=static_cast<ImGuiID>(-1);
            windowClass.ViewportFlagsOverrideSet=ImGuiViewportFlags_NoAutoMerge;
            windowClass.ViewportFlagsOverrideClear=ImGuiViewportFlags_NoTaskBarIcon;
            if(pr?ui.prLogTopmost:ui.ciLogTopmost) windowClass.ViewportFlagsOverrideSet|=ImGuiViewportFlags_TopMost;
            else windowClass.ViewportFlagsOverrideClear|=ImGuiViewportFlags_TopMost;
            ImGui::SetNextWindowClass(&windowClass);
            const char* title=pr?"Pull request - complete CI log###detachedPrLog":"CI / Actions - complete log###detachedCiLog";
            if(ImGui::Begin(title,open,ImGuiWindowFlags_NoCollapse))ciLogPanel(pr,true);
            ImGui::End();
        }
    }
    if(ui.prPatchWindow) {
        activePane_="aux-pr-patch";
        ImGui::SetNextWindowSize(cyui::dp(1180,700),ImGuiCond_FirstUseEver);
        ImGuiWindowClass windowClass;windowClass.ParentViewportId=static_cast<ImGuiID>(-1);windowClass.ViewportFlagsOverrideSet=ImGuiViewportFlags_NoAutoMerge;windowClass.ViewportFlagsOverrideClear=ImGuiViewportFlags_NoTaskBarIcon;ImGui::SetNextWindowClass(&windowClass);
        if(ImGui::Begin("Pull request - file diff###detachedPrPatch",&ui.prPatchWindow,ImGuiWindowFlags_NoCollapse))
            diffPanel("PullRequestPatch","##detachedPrPatchContents");
        ImGui::End();
    }
    activePane_=savedPane;
}

void Workspace::ciJobContext(const std::string& source,const Json& row) {
    if(source!="CiJobs" && source!="PullRequestCiJobs")return;
    bool pr=source=="PullRequestCiJobs";
    const auto& context=reviewContext(state_);
    const auto& run=reviewObject(context,pr?"prCiRun":"ciRun");
    const auto& selected=reviewObject(context,"selected");
    Json request={{"runId",run.value("id",0LL)}};
    if(pr)request["number"]=selected.value("number",0);
    auto values=row.find("values");
    if(values!=row.end() && values->is_object()) {
        auto id=values->find("Id");if(id!=values->end() && id->is_number_integer())request["jobId"]=*id;
    }
    ImGui::Separator();
    if(ImGui::MenuItem("Open complete workflow run",nullptr,false,!run.empty() && !busy())) {
        auto open=request;open["command"]=pr?"openPrCiRun":"openCiRun";send(open);
    }
    bool cancel=pr?flag("CanCancelSelectedPullRequestCiRun"):context.value("canCancelCi",false);
    if(ImGui::MenuItem("Cancel complete workflow run...",nullptr,false,cancel && !busy()))
        invoke(reviewRpc("Cancel workflow run",pr?"prCiCancel":"ciCancel",request,
            "GitHub cancels the complete run, not only the selected job. Every queued or running job will be cancelled. Actions write permission is required."));
    bool rerun=pr?flag("CanRerunSelectedPullRequestCiRun"):context.value("canRerunCi",false);
    if(ImGui::MenuItem("Rerun failed jobs...",nullptr,false,rerun && !busy()))
        invoke(reviewRpc("Rerun failed workflow jobs",pr?"prCiRerun":"ciRerun",request,
            "Queue the failed jobs of this selected workflow run again? Actions write permission is required."));
}

void Workspace::workItemPicker() {
    auto& ui=reviewUi(projectId_);
    if(requestClose){ui.taskPicker=false;ui.taskSessionToken.clear();return;}
    if(!action_.empty())return;
    if(!ui.taskPicker)return;
    const auto& context=reviewContext(state_);
    Json providers=context.value("taskProviders",Json::array());
    bool open=ui.taskPicker;
    if(cyui::beginModal("Add task links to pull request",&open,840)) {
        if(providers.empty())cyui::emptyState("No task integration enabled","Enable the Jira, ClickUp or CyTask plugin for this project.",cyui::icon::library);
        else {
            if(ImGui::BeginCombo("Provider",ui.provider.empty()?"Select provider":ui.provider.c_str())) {
                for(const auto& provider:providers) {
                    auto id=text(provider,"id");
                    if(ImGui::Selectable(text(provider,"name").c_str(),ui.provider==id)) {
                        ui.provider=id;ui.loadedTaskProvider.clear();ui.taskSessionToken.clear();
                        send({{"command","taskLoadProvider"},{"provider",id}});
                    }
                }
                ImGui::EndCombo();
            }
            const auto& settings=reviewObject(context,"taskSettings");
            if(!ui.provider.empty() && ui.loadedTaskProvider!=ui.provider && !settings.empty() && !busy()) {
                ui.taskBaseUrl=text(settings,"baseUrl");ui.taskScope=text(settings,"scopeId");
                ui.taskAccount=text(settings,"accountName");ui.taskTokenEnvironment=text(settings,"tokenEnvironmentVariable");
                ui.loadedTaskProvider=ui.provider;
            }
            if(cyui::beginProperties("##taskConnection",170.0f)) {
                for(auto entry:{std::pair<const char*,std::string*>("API / site URL",&ui.taskBaseUrl),{"Scope / list / project",&ui.taskScope},
                    {"Account",&ui.taskAccount},{"Token environment",&ui.taskTokenEnvironment}}) {
                    cyui::property(entry.first);ImGui::InputText((std::string("##task")+entry.first).c_str(),entry.second);
                }
                cyui::property("Session token");ImGui::InputText("##taskSessionToken",&ui.taskSessionToken,ImGuiInputTextFlags_Password);
                cyui::property("Search");ImGui::InputTextWithHint("##taskQuery","Key, title, status, list or folder...",&ui.taskQuery);
                cyui::endProperties();
            }
            auto request=[&](const char* command) {
                return Json{{"command",command},{"provider",ui.provider},{"query",ui.taskQuery},{"sessionToken",ui.taskSessionToken},
                    {"settings",{{"baseUrl",ui.taskBaseUrl},{"scopeId",ui.taskScope},{"accountName",ui.taskAccount},{"tokenEnvironmentVariable",ui.taskTokenEnvironment}}}};
            };
            {
                cyui::ScopedDisabled disabled(busy() || ui.provider.empty());
                if(cyui::button("Test and save connection"))send(request("taskSave"));
                ImGui::SameLine();if(cyui::button("Search tasks"))send(request("taskSearch"));
            }
            cyui::textDim("%s",text(context,"taskStatus").c_str());
            list("NativeWorkItemSearch",ImVec2(0,cyui::dp(240)));
            cyui::toggleSwitch("Prefix title with first selected task key",&ui.prefixTaskTitle);
            {
                Json selected=selectedRows("NativeWorkItemSearch");
                cyui::ScopedDisabled disabled(busy() || selected.empty());
                if(cyui::primaryButton("Add task links")) {
                    send({{"command","taskAdd"},{"rows",selected},{"prefixTitle",ui.prefixTaskTitle},{"forPullRequest",true},{"edits",draftValues_}});
                    open=false;ui.taskSessionToken.clear();ImGui::CloseCurrentPopup();
                }
            }
            ImGui::SameLine();if(cyui::ghostButton("Cancel")){open=false;ui.taskSessionToken.clear();ImGui::CloseCurrentPopup();}
        }
        cyui::endModal();
    }
    ui.taskPicker=open;
    if(!open)ui.taskSessionToken.clear();
}
}
