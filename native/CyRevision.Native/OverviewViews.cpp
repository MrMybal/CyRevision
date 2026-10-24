#include "Workspace.h"
#include "ReviewViews.h"
#include "StringInput.h"
#include <shobjidl.h>
#include <wrl/client.h>
#include <set>

namespace cyrevision {
namespace {
struct OverviewUi {
    std::string project,preset;
    bool removeCaches=false,initializeOpen=false,lfsOnly=false,initialCommit=false,createIgnore=true;
    bool modeOpen=false,modeCreateIgnore=true;
    bool updateOpen=false;
    std::string seenUpdateVersion;
    int step=0,loadedPreparation=0,seenModeRevision=0;
    std::string path,preparedPath,ignoreText,customPatterns,userName,userEmail,remote,initialMessage="Initial commit",modeIgnore;
    std::unordered_set<std::string> patterns;
};
OverviewUi& overviewUi() { static OverviewUi state;return state; }
std::string chooseDirectory() {
    Microsoft::WRL::ComPtr<IFileOpenDialog> dialog;
    if(FAILED(CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog))))return {};
    DWORD options=0;dialog->GetOptions(&options);
    dialog->SetOptions(options|FOS_PICKFOLDERS|FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST);
    if(FAILED(dialog->Show(GetActiveWindow())))return {};
    Microsoft::WRL::ComPtr<IShellItem> item;if(FAILED(dialog->GetResult(&item)))return {};
    PWSTR path=nullptr;if(FAILED(item->GetDisplayName(SIGDN_FILESYSPATH,&path)))return {};
    std::string result=utf8(path);CoTaskMemFree(path);return result;
}
std::string draftValue(const std::unordered_map<std::string,std::string>& edits,const char* name,const std::string& fallback) {
    auto found=edits.find(name);return found==edits.end()?fallback:found->second;
}
}

bool Workspace::overviewView() {
    if(workspace_!="overview" && workspace_!="gitoverview" && workspace_!="members" && workspace_!="notifications" && workspace_!="visible" && workspace_!="license")return false;
    auto& ui=overviewUi();
    if(ui.project!=projectId_) {ui.project=projectId_;ui.removeCaches=false;}
    if(workspace_=="overview") {
        cyui::panelHeader("Project",value("RuntimeModeSummary").c_str());
        if(!flag("HasSelectedProject")) {
            cyui::emptyState("No project selected","Open a repository or add a project folder. Project tools remain scoped to the selected project.",cyui::icon::folder);
            if(cyui::primaryButton("Initialize Git repository..."))openGitInitialization();
            return true;
        }
        ImGui::BeginChild("##projectOverviewScroll");
        if(cyui::beginSection("Operating mode",true)) {
            cyui::textDim("Git and Sync remain independent. Select a mode for this project only.");
            selector("Presets","SelectedPreset","Operating mode");
            cyui::textDim("%s",value("SelectedPreset.Description").c_str());
            std::string maturity=value("SelectedPreset.Maturity");if(!maturity.empty())reviewStatus(maturity);
            if(flag("SelectedPreset.IsPluginMode"))cyui::callout(cyui::Tone::info,value("SelectedPreset.AvailabilitySummary").c_str());
            {cyui::ScopedDisabled disabled(busy() || !flag("SelectedPreset.IsAvailable"));
            if(cyui::primaryButton("Apply selected mode..."))send({{"command","overview.prepareMode"}});}
            if(flag("HasActivePluginOperatingMode")) {
                cyui::sectionTitle(value("ActivePluginOperatingModeName").c_str());
                cyui::textDim("%s",value("ActivePluginOperatingModeSummary").c_str());
            }
            cyui::textFaint("Changing mode does not delete existing project data. Active project services are stopped and reconfigured first.");
            cyui::endSection();
        }
        if(cyui::beginSection("Project settings",true)) {
            field("ProjectDisplayNameDraft","Display name");
            command("SetSelectedProjectDisplayNameAsync","Rename in CyRevision");
            cyui::tooltip("Only changes the name displayed in the catalogue. The project folder is not renamed.");
            cyui::textDim("%s",value("RepositoryPath").c_str());
            requestTable("ProjectAccentColors");
            const auto& tables=reviewObject(state_,"tables");
            const auto& colors=reviewObject(tables,"ProjectAccentColors");
            if(ImGui::BeginCombo("Project colour",value("SelectedProjectAccentColor").c_str())) {
                for(const auto& row:colors.value("rows",Json::array())) {
                    auto color=text(reviewObject(row,"values"),"Value");
                    if(ImGui::Selectable(color.c_str(),color==value("SelectedProjectAccentColor")))
                        send({{"command","invoke"},{"action","SetSelectedProjectAccentColorAsync"},{"parameters",{{"accentColor",color}}}});
                }
                ImGui::EndCombo();
            }
            selector("ProjectSidebarGroupOptions","SidebarGroupDraft","Existing sidebar group");
            field("SidebarGroupDraft","Group (existing or new)");command("SetSelectedProjectSidebarGroupAsync","Apply group");
            cyui::sectionTitle("Project disk");
            cyui::textDim("%s",value("ProjectDiskSummary").c_str());
            float free=0;try{free=std::stof(value("ProjectDiskFreePercent","0"))/100.0f;}catch(...){}
            cyui::progressBar(std::clamp(free,0.0f,1.0f),ImVec2(-1,cyui::dp(9)),nullptr,free<.05f?cyui::Tone::error:free<.1f?cyui::Tone::warning:cyui::Tone::success);
            cyui::textDim("%s",value("ProjectDiskWarning").c_str());
            if(free<.1f && flag("IsGitProject"))if(cyui::ghostButton("Inspect Git LFS storage"))navigate("lfs");
            cyui::endSection();
        }
        if(cyui::beginSection("Automatic project services",true)) {
            cyui::textDim("Only services explicitly enabled for this project start automatically.");
            {cyui::ScopedDisabled disabled(busy());
            bool sync=flag("StartSyncAutomatically"),vpn=flag("StartVpnAutomatically");
            if(cyui::toggleSwitch("Start Sync automatically",&sync))send({{"command","overview.serviceStartup"},{"sync",sync}});
            if(cyui::toggleSwitch("Start VPN automatically",&vpn))send({{"command","overview.serviceStartup"},{"vpn",vpn}});}
            command("StartSyncAsync","Start Sync");ImGui::SameLine();command("PauseSyncAsync","Pause Sync");ImGui::SameLine();command("ResumeSyncAsync","Resume Sync");
            command("StartVpnAsync","Start VPN");ImGui::SameLine();command("StopVpnAsync","Stop VPN");
            cyui::endSection();
        }
        if(cyui::beginSection("Project catalogue",false)) {
            if(cyui::button("Open project folder"))send({{"command","overview.openFolder"}});
            cyui::toggleSwitch("Also remove generated CyRevision caches",&ui.removeCaches);
            cyui::textFaint("Removing a project only updates the catalogue. Source files, Git history and external stores stay on disk.");
            {cyui::ScopedDisabled disabled(busy());
            if(cyui::dangerButton("Remove from CyRevision..."))invoke(reviewRpc("Remove project from catalogue","overview.removeProject",{{"removeGeneratedCaches",ui.removeCaches}},
                ui.removeCaches?"Remove this project from the catalogue and delete only its generated CyRevision caches? Project files and Git data remain on disk.":"Remove this project from the CyRevision catalogue? Project files and caches remain on disk."));}
            cyui::endSection();
        }
        ImGui::EndChild();
    }
    else if(workspace_=="gitoverview") {
        cyui::panelHeader("Git repository overview","Repository information, tracking state and local remote configuration.");
        if(!flag("IsGitProject")) {cyui::emptyState("Git is disabled","Choose a Git operating mode for this project first.",cyui::icon::code);return true;}
        if(cyui::button("Open repository folder"))send({{"command","overview.openFolder"}});
        ImGui::SameLine();command("RefreshAsync","Refresh project");
        ImGui::BeginChild("##gitOverviewScroll");
        if(cyui::beginSection("Repository information",true)) {
            if(cyui::beginProperties("##gitRepositoryInformation",130.0f)) {
                for(auto property:std::initializer_list<std::pair<const char*,const char*>>{{"Mode","GitConnectionKind"},{"Path","RepositoryPath"},{"Branch / HEAD","CurrentBranch"},
                    {"Working tree","GitOverviewRepositoryState"},{"Tracking","GitOverviewTrackingState"},{"Latest commit","GitOverviewHeadSummary"},{"Tools","ToolStatus"}}) {
                    cyui::property(property.first);ImGui::TextWrapped("%s",value(property.second).c_str());
                }
                cyui::endProperties();
            }
            cyui::endSection();
        }
        if(cyui::beginSection("Remote repositories",true)) {
            cyui::textDim("%s",value("GitOverviewRemoteStatus").c_str());
            command("BeginNewGitRemote","New remote");ImGui::SameLine();command("RefreshGitOverviewRemotesAsync","Refresh remotes");ImGui::SameLine();command("FetchAsync","Fetch remotes");
            list("GitRemotes",ImVec2(0,cyui::dp(205)));
            {cyui::ScopedDisabled disabled(flag("IsEditingGitRemote"));field("GitRemoteNameDraft","Remote name");}
            field("GitRemoteFetchUrlDraft","Fetch URL");field("GitRemotePushUrlDraft","Push URL (optional)");
            cyui::textFaint("Use an HTTPS or SSH repository URL. An empty push URL uses the fetch URL.");
            cyui::sectionTitle("Repository page");cyui::textDim("%s",value("SelectedGitRemoteWebUrl").c_str());
            {cyui::ScopedDisabled disabled(!flag("CanSaveGitRemote"));command("SaveGitRemoteAsync",value("GitRemoteSaveAction","Save remote").c_str(),true);}
            {cyui::ScopedDisabled disabled(busy() || !flag("CanOpenSelectedGitRemote"));
            ImGui::SameLine();if(cyui::button("Open provider page"))send({{"command","overview.openRemote"}});}
            {cyui::ScopedDisabled disabled(busy() || !flag("CanRemoveSelectedGitRemote"));
            ImGui::SameLine();if(cyui::dangerButton("Remove remote..."))invoke(reviewRpc("Remove local remote configuration","overview.removeRemote",{{"remote",value("SelectedGitRemote.Name")}},
                "Remove the selected remote and its remote-tracking references from local Git configuration? The server repository and server branches are not deleted."));}
            cyui::endSection();
        }
        ImGui::EndChild();
    }
    else if(workspace_=="members") {
        cyui::panelHeader("Project members",value("ProjectMembersSummary").c_str());
        command("RefreshProjectMembersAsync","Refresh status",true);
        if(ImGui::BeginTabBar("##projectMembers")) {
            if(flag("ShowGitMemberPanel") && ImGui::BeginTabItem("Git contributors")) {cyui::textDim("Authors found in the repository history.");list("GitProjectMembers");ImGui::EndTabItem();}
            if(flag("ShowSyncMemberPanel") && ImGui::BeginTabItem("Sync identities")) {cyui::textDim("Authorized devices and their live connection state.");list("SyncProjectMembers");ImGui::EndTabItem();}
            if(flag("ShowVpnMemberPanel") && ImGui::BeginTabItem("VPN peers")) {cyui::textDim("WireGuard capabilities and handshake state.");list("VpnProjectMembers");ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }
    }
    else if(workspace_=="notifications") {
        cyui::panelHeader("Project notifications",value("ProjectNotificationSummary").c_str());
        bool enabled=flag("ProjectNotificationsEnabled");
        {cyui::ScopedDisabled disabled(busy());if(cyui::toggleSwitch("Enabled for this project",&enabled))
            send({{"command","invoke"},{"action","SetProjectNotificationsEnabledAsync"},{"parameters",{{"enabled",enabled}}}});}
        command("MarkProjectNotificationsRead","Mark all read");
        field("ProjectNotificationSearch","Search task, result or error");
        selector("ProjectNotificationStateFilters","ProjectNotificationStateFilter","State");
        list("ProjectNotifications");
    }
    else if(workspace_=="visible") {
        cyui::panelHeader("Visible project tabs","Project and Visible tabs stay accessible so hidden tools can always be restored.");
        if(cyui::button("Show all"))send({{"command","navigation"},{"operation","showAll"}});
        const auto& navigation=reviewObject(state_,"navigation");
        std::string selectedPreset=ui.preset.empty()?text(navigation,"preset","Full workspace"):ui.preset;
        if(ImGui::BeginCombo("Workspace preset",selectedPreset.c_str())) {
            for(const auto& preset:navigation.value("presets",Json::array())) {
                std::string name=preset.is_string()?preset.get<std::string>():text(preset,"name");
                if(ImGui::Selectable(name.c_str(),name==selectedPreset))ui.preset=name;
            }
            ImGui::EndCombo();
        }
        {cyui::ScopedDisabled disabled(busy() || selectedPreset.empty());
        if(cyui::primaryButton("Apply workspace preset"))send({{"command","navigation"},{"operation","preset"},{"name",selectedPreset}});}
        ImGui::BeginChild("##visibleTabsTree",ImVec2(0,0),ImGuiChildFlags_Borders);
        Json tabs=navigation.value("tabs",Json::array());std::set<std::string> seenCategories;std::vector<std::string> categories;
        for(const auto& tab:tabs){auto category=text(tab,"category");if(seenCategories.insert(category).second)categories.push_back(category);}
        for(const auto& category:categories) {
            int shown=0,total=0;
            for(const auto& tab:tabs)if(text(tab,"category")==category){total++;if(tab.value("visible",true)&&tab.value("available",false))shown++;}
            std::string label=category+"  ("+std::to_string(shown)+" / "+std::to_string(total)+")";
            if(ImGui::TreeNodeEx(category.c_str(),ImGuiTreeNodeFlags_DefaultOpen,"%s",label.c_str())) {
                for(const auto& tab:tabs)if(text(tab,"category")==category) {
                    auto id=text(tab,"id");cyui::ScopedID scope(id.c_str());
                    bool visible=tab.value("visible",true),available=tab.value("available",false),required=id=="overview"||id=="visible";
                    {cyui::ScopedDisabled disabled(busy() || !available || required);
                    if(ImGui::Checkbox(text(tab,"label").c_str(),&visible))send({{"command","navigation"},{"operation","visibility"},{"workspace",id},{"visible",visible}});}
                    if(required){ImGui::SameLine();cyui::textFaint("Always available");}
                    else if(!available){ImGui::SameLine();cyui::textFaint("Unavailable in this project mode or plugin scope");}
                }
                ImGui::TreePop();
            }
        }
        ImGui::EndChild();
    }
    else if(workspace_=="license") {
        cyui::panelHeader("Project license",value("ProjectLicenseStatus").c_str());
        reviewStatus(value("ProjectLicenseDetectedId"));ImGui::SameLine();command("RefreshProjectLicenseAsync","Refresh license");
        if(flag("IsProjectLicenseLoading")){ImGui::SameLine();cyui::spinner("##licenseLoading");}
        ImGui::BeginChild("##projectLicenseScroll");
        selector("ProjectLicenseTemplates","SelectedProjectLicenseTemplate","Starter template");
        field("ProjectLicenseHolder","Copyright holder");field("ProjectLicenseYear","Year");field("ProjectLicenseFileName","Root file name");
        if(cyui::beginSection("Template permissions and conditions",false)) {
            for(auto property:std::initializer_list<std::pair<const char*,const char*>>{{"Permissions","Permissions"},{"Conditions","Conditions"},{"Limitations","Limitations"},{"Reference","ReferenceUrl"}}) {
                cyui::sectionTitle(property.first);ImGui::TextWrapped("%s",value(std::string("SelectedProjectLicenseTemplate.")+property.second).c_str());
            }
            cyui::endSection();
        }
        command("ApplySelectedProjectLicenseTemplate","Apply template to editor");ImGui::SameLine();command("ImportProjectLicenseDraftAsync","Import text file...");
        cyui::textFaint("Nothing is written until you save.");
        field("ProjectLicenseDraft","License text",true);
        cyui::callout(cyui::Tone::warning,"Changing a license can affect copyright and redistribution rights. Review the final terms before saving.");
        {cyui::ScopedDisabled disabled(busy() || !flag("CanSaveProjectLicense"));
        bool exists=flag("ProjectLicenseTargetExists");
        if(cyui::primaryButton(exists?"Replace project license...":"Save project license..."))invoke(reviewRpc(exists?"Replace project license":"Save project license","overview.saveLicense",
            {{"fileName",draftValue(edits_,"ProjectLicenseFileName",value("ProjectLicenseFileName"))},{"overwrite",exists}},
            exists?"Replace the existing project license with the complete edited terms? This changes a project file.":"Create the root license file with the complete edited terms? This changes a project file."));}
        ImGui::EndChild();
    }
    return true;
}

void Workspace::openGitInitialization(const std::string& path,bool lfsOnly) {
    auto& ui=overviewUi();
    ui.initializeOpen=true;ui.lfsOnly=lfsOnly;ui.step=0;ui.path=path;ui.preparedPath.clear();ui.loadedPreparation=0;
    ui.ignoreText.clear();ui.patterns.clear();ui.customPatterns.clear();ui.userName.clear();ui.userEmail.clear();ui.remote.clear();
    ui.createIgnore=true;ui.initialCommit=false;ui.initialMessage="Initial commit";
    if(!path.empty())send({{"command","overview.prepareInitialization"},{"path",path},{"lfsOnly",lfsOnly}});
}

void Workspace::gitInitializationWizard() {
    auto& ui=overviewUi();
    const auto& context=reviewObject(state_,"overviewContext");
    const auto& mode=reviewObject(context,"mode");
    int revision=mode.value("revision",0);
    if(requestClose) {
        ui.initializeOpen=false;ui.modeOpen=false;ui.updateOpen=false;ui.seenModeRevision=revision;
        return;
    }
    // A single modal owns input at a time. An action confirmation or the task
    // picker has priority over optional initialization/update prompts.
    if(!action_.empty() || reviewUi(projectId_).taskPicker)return;
    if(mode.value("pending",false) && revision!=ui.seenModeRevision) {
        ui.seenModeRevision=revision;ui.modeOpen=true;ui.modeCreateIgnore=true;ui.modeIgnore=text(mode,"ignoreContent");
    }
    bool modeOpen=ui.modeOpen;
    if(modeOpen && !ui.initializeOpen && cyui::beginModal("Apply project operating mode",&modeOpen,740)) {
        cyui::heading(text(mode,"presetName","Project mode").c_str(),2);
        cyui::textDim("Active services are stopped and reconfigured for this project. Existing stored data is retained.");
        if(mode.value("needsIgnore",false)) {
            cyui::callout(cyui::Tone::warning,"No .gitignore exists in this project. Add recommended rules to keep generated files and caches out of Git.");
            cyui::toggleSwitch("Create recommended .gitignore",&ui.modeCreateIgnore);
            {cyui::ScopedDisabled disabled(!ui.modeCreateIgnore);cyui::ScopedFont mono(cyui::monoFont());
            ImGui::InputTextMultiline("##modeIgnore",&ui.modeIgnore,ImVec2(-1,cyui::dp(260)),ImGuiInputTextFlags_AllowTabInput);}
            if(!ui.modeCreateIgnore)cyui::textFaint("Continue without adding ignore rules. Existing files are not deleted.");
        }
        {cyui::ScopedDisabled disabled(busy());
        if(cyui::primaryButton("Apply mode")) {
            send({{"command","overview.applyMode"},{"revision",revision},{"ignoreChoice",ui.modeCreateIgnore?"create":"continue"},{"ignoreContent",ui.modeIgnore},{"confirmed",true}});
            modeOpen=false;ImGui::CloseCurrentPopup();
        }}
        ImGui::SameLine();if(cyui::ghostButton("Cancel")){send({{"command","overview.cancelMode"}});modeOpen=false;ImGui::CloseCurrentPopup();}
        cyui::endModal();
    }
    if(ui.modeOpen && !modeOpen && mode.value("pending",false)) {
        // A cancelled modal must not reopen from an older in-flight snapshot.
        ui.seenModeRevision=revision;
    }
    ui.modeOpen=modeOpen;

    if(modeOpen && !ui.initializeOpen)return;

    std::string updateVersion=value("LatestApplicationVersion");
    bool updateAvailable=flag("HasUpdateAvailable");
    if(updateAvailable && !updateVersion.empty() && updateVersion!=ui.seenUpdateVersion && !ui.initializeOpen && !modeOpen) {
        ui.seenUpdateVersion=updateVersion;ui.updateOpen=true;
    }
    bool updateOpen=ui.updateOpen;
    if(updateOpen && !ui.initializeOpen && cyui::beginModal("CyRevision update available",&updateOpen,790)) {
        cyui::heading(("CyRevision "+updateVersion).c_str(),2);
        cyui::textDim("Installed: %s",value("CurrentApplicationVersion").c_str());
        cyui::textDim("%s",value("UpdateStatus").c_str());
        ImGui::BeginChild("##nativeUpdateNotes",ImVec2(0,cyui::dp(265)),ImGuiChildFlags_Borders);
        textPanel("UpdateReleaseNotes","##nativeUpdateNotesText");ImGui::EndChild();
        cyui::callout(cyui::Tone::info,"The package is downloaded and verified before installation. CyRevision and its engine close before the external installer starts. Save active work first.");
        {cyui::ScopedDisabled disabled(busy() || !flag("CanInstallUpdate"));
        if(cyui::primaryButton("Download, close and install")) {
            send({{"command","overview.installUpdate"},{"confirmed",true}});
            updateOpen=false;ImGui::CloseCurrentPopup();
        }}
        ImGui::SameLine();if(cyui::ghostButton("Not now")){updateOpen=false;ImGui::CloseCurrentPopup();}
        cyui::endModal();
    }
    ui.updateOpen=updateOpen;

    if(!ui.initializeOpen)return;
    bool open=ui.initializeOpen;
    if(cyui::beginModal(ui.lfsOnly?"Configure Git LFS":"Initialize Git repository",&open,910)) {
        const auto& preparation=reviewObject(context,"initialization");
        bool prepared=!preparation.empty() && (text(preparation,"path")==ui.path || text(preparation,"requestedPath")==ui.path) && preparation.value("lfsOnly",false)==ui.lfsOnly;
        if(prepared && ui.preparedPath!=ui.path && !busy()) {
            ui.path=text(preparation,"path");ui.preparedPath=ui.path;ui.ignoreText=text(preparation,"gitIgnoreContent");
            ui.createIgnore=!preparation.value("hasGitIgnore",false);ui.patterns.clear();
            for(const auto& pattern:preparation.value("lfsPatterns",Json::array()))if(pattern.value("isRecommended",false))ui.patterns.insert(text(pattern,"pattern"));
            for(const auto& pattern:preparation.value("existingLfsPatterns",Json::array()))if(pattern.is_string())ui.patterns.insert(pattern.get<std::string>());
        }
        const char* titles[]={"Folder and tools","Ignore rules","Git LFS attributes","Identity and remote","Review and initialize"};
        cyui::heading(titles[ui.step],2);cyui::textDim("Step %d / 5",ui.step+1);
        if(!error_.empty())cyui::callout(cyui::Tone::error,error_.c_str());
        float bodyHeight=std::min(cyui::dp(470),ImGui::GetMainViewport()->WorkSize.y*.6f);
        ImGui::BeginChild("##gitInitializationBody",ImVec2(0,bodyHeight),ImGuiChildFlags_Borders);
        bool canNext=prepared && !busy();
        const auto& tools=reviewObject(preparation,"tools");
        bool toolReady=tools.value("gitAvailable",false) && tools.value("lfsAvailable",false);
        if(ui.step==0) {
            if(cyui::beginProperties("##initializationFolder",125.0f)) {
                cyui::property("Project folder");ImGui::InputText("##initializationRoot",&ui.path);
                cyui::endProperties();
            }
            {cyui::ScopedDisabled disabled(busy());
            if(cyui::button("Browse folder...")){std::string path=chooseDirectory();if(!path.empty()){ui.path=path;ui.preparedPath.clear();send({{"command","overview.prepareInitialization"},{"path",path},{"lfsOnly",ui.lfsOnly}});}}
            ImGui::SameLine();if(cyui::button("Analyze folder")){ui.preparedPath.clear();send({{"command","overview.prepareInitialization"},{"path",ui.path},{"lfsOnly",ui.lfsOnly}});}}
            if(prepared) {
                cyui::textDim("Detected project: %s",text(preparation,"detected").c_str());
                reviewStatus(tools.value("gitAvailable",false)?"Git available":"Git missing");ImGui::SameLine();reviewStatus(tools.value("lfsAvailable",false)?"Git LFS available":"Git LFS missing");
                cyui::textDim("%s",text(tools,"gitVersion").c_str());cyui::textDim("%s",text(tools,"lfsVersion").c_str());
                if(!ui.lfsOnly && preparation.value("hasRepository",false))cyui::callout(cyui::Tone::warning,"This folder already contains a Git repository. Open it instead; initialization will not overwrite it.");
                if(ui.lfsOnly && !preparation.value("hasRepository",false))cyui::callout(cyui::Tone::warning,"Select an existing Git repository before applying LFS recommendations.");
            }
            else cyui::emptyState("Choose and analyze a folder","Analysis checks the Git tools, existing configuration, recommended ignore/LFS patterns and a bounded file sample. It does not modify project files.",cyui::icon::folder);
            canNext=canNext && toolReady && (ui.lfsOnly==preparation.value("hasRepository",false));
        }
        else if(ui.step==1) {
            bool existing=preparation.value("hasGitIgnore",false);
            if(ui.lfsOnly)cyui::callout(cyui::Tone::info,"LFS configuration does not change the project's .gitignore.");
            else if(existing)cyui::callout(cyui::Tone::info,"An existing .gitignore was found. It is shown read-only and will not be overwritten.");
            {cyui::ScopedDisabled disabled(existing || ui.lfsOnly);cyui::toggleSwitch("Create recommended .gitignore",&ui.createIgnore);}
            cyui::ScopedFont mono(cyui::monoFont());
            ImGuiInputTextFlags flags=ImGuiInputTextFlags_AllowTabInput | ((existing || ui.lfsOnly)?ImGuiInputTextFlags_ReadOnly:0);
            ImGui::InputTextMultiline("##initializationIgnore",&ui.ignoreText,ImVec2(-1,std::max(cyui::dp(130),ImGui::GetContentRegionAvail().y)),flags);
        }
        else if(ui.step==2) {
            cyui::textDim("Selected patterns are merged into .gitattributes. Existing rules are preserved and checked rows already tracked by LFS cannot be removed here.");
            std::unordered_set<std::string> existing;
            for(const auto& value:preparation.value("existingLfsPatterns",Json::array()))if(value.is_string())existing.insert(value.get<std::string>());
            if(ImGui::BeginTable("##initializationLfsPatterns",4,ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|ImGuiTableFlags_RowBg|ImGuiTableFlags_ScrollY|ImGuiTableFlags_BordersInnerV,ImVec2(0,cyui::dp(280)))) {
                ImGui::TableSetupColumn("Track",ImGuiTableColumnFlags_WidthFixed,cyui::dp(50));ImGui::TableSetupColumn("Pattern",ImGuiTableColumnFlags_WidthFixed,cyui::dp(105));
                ImGui::TableSetupColumn("Category",ImGuiTableColumnFlags_WidthFixed,cyui::dp(115));ImGui::TableSetupColumn("Reason");ImGui::TableHeadersRow();
                for(const auto& item:preparation.value("lfsPatterns",Json::array())) {
                    std::string pattern=text(item,"pattern");cyui::ScopedID id(pattern.c_str());bool checked=ui.patterns.count(pattern)>0;
                    ImGui::TableNextRow();ImGui::TableNextColumn();
                    {cyui::ScopedDisabled disabled(existing.count(pattern)>0);if(ImGui::Checkbox("##track",&checked)){if(checked)ui.patterns.insert(pattern);else ui.patterns.erase(pattern);}}
                    ImGui::TableNextColumn();ImGui::TextUnformatted(pattern.c_str());ImGui::TableNextColumn();ImGui::TextUnformatted(text(item,"category").c_str());
                    ImGui::TableNextColumn();ImGui::TextUnformatted(text(item,"reason").c_str());
                }
                ImGui::EndTable();
            }
            cyui::textDim("Additional patterns (one per line)");
            ImGui::InputTextMultiline("##customLfsPatterns",&ui.customPatterns,ImVec2(-1,cyui::dp(80)));
            cyui::textFaint("LFS attributes affect future commits. Existing history is not rewritten and no remote objects are deleted.");
        }
        else if(ui.step==3) {
            if(ui.lfsOnly)cyui::callout(cyui::Tone::info,"The repository identity, remotes and commits remain unchanged in LFS-only mode.");
            cyui::ScopedDisabled disabled(ui.lfsOnly);
            if(cyui::beginProperties("##initializationIdentity",165.0f)) {
                cyui::property("Local author name");ImGui::InputTextWithHint("##initializationName","Blank inherits the existing Git identity",&ui.userName);
                cyui::property("Local author email");ImGui::InputTextWithHint("##initializationEmail","Blank inherits the existing Git identity",&ui.userEmail);
                cyui::property("Remote origin (optional)");ImGui::InputTextWithHint("##initializationRemote","https://server/team/repository.git",&ui.remote);
                cyui::endProperties();
            }
            cyui::toggleSwitch("Create an initial commit",&ui.initialCommit);
            {cyui::ScopedDisabled commitDisabled(!ui.initialCommit);ImGui::InputText("Commit message",&ui.initialMessage);}
            cyui::textFaint("Identity is configured for this repository only. No global Git configuration is changed. Nothing is pushed to a remote by this wizard.");
            canNext=canNext && (ui.lfsOnly || ui.userName.empty()==ui.userEmail.empty());
            if(!ui.lfsOnly && ui.userName.empty()!=ui.userEmail.empty())cyui::callout(cyui::Tone::warning,"Enter both an author name and email, or leave both blank.");
        }
        else {
            cyui::textDim("Folder: %s",ui.path.c_str());
            cyui::textDim(".gitignore: %s",ui.lfsOnly?"Unchanged":ui.createIgnore?"Create recommended rules":"Preserve existing / do not create");
            cyui::textDim("Selected LFS patterns: %zu",ui.patterns.size());
            cyui::textDim("Initial commit: %s",ui.initialCommit && !ui.lfsOnly?"Yes - include eligible changes after ignore rules":"No");
            const auto& preview=reviewObject(preparation,"preview");
            cyui::textDim("Bounded file preview: %d files, %lld bytes%s",preview.value("fileCount",0),preview.value("totalBytes",0LL),preview.value("wasLimited",false)?" (sample limited)":"");
            cyui::textFaint("Generated folders and directory links are excluded from this preview. The actual initial commit uses Git status after ignore rules are applied.");
            if(ImGui::BeginChild("##initializationSample",ImVec2(0,cyui::dp(245)),ImGuiChildFlags_Borders,ImGuiWindowFlags_HorizontalScrollbar)) {
                cyui::ScopedFont mono(cyui::monoFont());
                for(const auto& sample:preview.value("samplePaths",Json::array()))ImGui::TextUnformatted(sample.get_ref<const std::string&>().c_str());
            }
            ImGui::EndChild();
            cyui::callout(cyui::Tone::info,ui.lfsOnly?"Only selected Git LFS attributes will be merged. Existing project files, history and remotes are retained.":"Finish initializes local Git, optionally adds ignore/LFS rules, local identity, origin and an initial commit. It does not upload or delete project files.");
            canNext=canNext && toolReady && (ui.lfsOnly || !ui.initialCommit || !ui.initialMessage.empty());
        }
        ImGui::EndChild();
        {cyui::ScopedDisabled disabled(ui.step==0 || busy());if(cyui::button("Back"))ui.step--;}
        ImGui::SameLine();
        {cyui::ScopedDisabled disabled(!canNext);
        if(cyui::primaryButton(ui.step==4?(ui.lfsOnly?"Apply Git LFS attributes":"Initialize repository"):"Next")) {
            if(ui.step<4)ui.step++;
            else {
                Json patterns=Json::array();for(const auto& pattern:ui.patterns)patterns.push_back(pattern);
                std::string line;for(size_t start=0;start<=ui.customPatterns.size();) {
                    size_t end=ui.customPatterns.find('\n',start);line=ui.customPatterns.substr(start,end==std::string::npos?std::string::npos:end-start);
                    if(!line.empty() && line.back()=='\r')line.pop_back();if(!line.empty())patterns.push_back(line);
                    if(end==std::string::npos)break;start=end+1;
                }
                send({{"command","overview.initialize"},{"path",ui.path},{"lfsOnly",ui.lfsOnly},{"confirmed",true},{"options",{
                    {"createGitIgnore",ui.createIgnore && !ui.lfsOnly},{"gitIgnoreContent",ui.ignoreText},{"lfsPatterns",patterns},
                    {"userName",ui.lfsOnly?"":ui.userName},{"userEmail",ui.lfsOnly?"":ui.userEmail},{"remoteUrl",ui.lfsOnly?"":ui.remote},
                    {"createInitialCommit",!ui.lfsOnly && ui.initialCommit},{"initialCommitMessage",ui.initialMessage}}}});
                open=false;ImGui::CloseCurrentPopup();
            }
        }}
        ImGui::SameLine();if(cyui::ghostButton("Cancel")){send({{"command","cancel"}});open=false;ImGui::CloseCurrentPopup();}
        cyui::endModal();
    }
    ui.initializeOpen=open;
}
}
