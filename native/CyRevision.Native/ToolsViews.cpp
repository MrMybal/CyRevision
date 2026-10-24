#include "Workspace.h"
#include "StringInput.h"
#include "Assets.h"
#include <imgui_internal.h>
#include <shobjidl.h>
#include <wrl/client.h>
#include <algorithm>
#include <initializer_list>
#include <unordered_set>

namespace cyrevision {
namespace {
struct ToolField { const char* name; const char* label; bool multiline=false; };
enum class ToolPicker { file, folder, save };
std::string pickToolPath(ToolPicker kind,const char* title) {
    Microsoft::WRL::ComPtr<IFileDialog> dialog;
    HRESULT created=kind==ToolPicker::save
        ? CoCreateInstance(CLSID_FileSaveDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog))
        : CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog));
    if(FAILED(created)) return {};
    DWORD options{};dialog->GetOptions(&options);
    options|=FOS_FORCEFILESYSTEM|FOS_NOCHANGEDIR;
    if(kind==ToolPicker::folder) options|=FOS_PICKFOLDERS|FOS_PATHMUSTEXIST;
    else if(kind==ToolPicker::file) options|=FOS_FILEMUSTEXIST|FOS_PATHMUSTEXIST;
    else options|=FOS_OVERWRITEPROMPT|FOS_PATHMUSTEXIST;
    dialog->SetOptions(options);dialog->SetTitle(wide(title).c_str());
    if(FAILED(dialog->Show(GetActiveWindow()))) return {};
    Microsoft::WRL::ComPtr<IShellItem> item;
    if(FAILED(dialog->GetResult(&item))) return {};
    PWSTR path{};if(FAILED(item->GetDisplayName(SIGDN_FILESYSPATH,&path))) return {};
    auto result=utf8(path);CoTaskMemFree(path);return result;
}
bool toolColumns(const char* id,float left=0.44f) {
    if(!ImGui::BeginTable(id,2,ImGuiTableFlags_Resizable|ImGuiTableFlags_BordersInnerV)) return false;
    ImGui::TableSetupColumn("Navigation",ImGuiTableColumnFlags_WidthStretch,left);
    ImGui::TableSetupColumn("Details",ImGuiTableColumnFlags_WidthStretch,1.0f-left);
    ImGui::TableNextRow();ImGui::TableNextColumn();return true;
}
void toolImage(const std::string& path,const char* empty) {
    auto image=nativeTexture(path);
    if(!image || image->width<=0 || image->height<=0) {
        cyui::emptyState(empty,"Choose an image, or use the active asset provider for a rendered preview.",cyui::icon::photo);
        return;
    }
    auto available=ImGui::GetContentRegionAvail();
    float scale=std::min(available.x/static_cast<float>(image->width),available.y/static_cast<float>(image->height));
    scale=std::min(1.0f,std::max(0.01f,scale));
    ImGui::Image(ImTextureRef(image->id),ImVec2(image->width*scale,image->height*scale));
}
}

bool Workspace::toolsView() {
    static const std::unordered_set<std::string> handled={"code","search","console","assets","ai","mcp","sync","syncconflicts","backups","sharedsync","vpn","swarm","vpnfiles","teamchat","builds","discord","wip","plugins","unreal","unity","godot","lore","perforce","cystore","unrealbuilds","diagnostics","help"};
    if(!handled.count(workspace_)) return false;
    cyui::ScopedID scope(workspace_.c_str());
    auto fields=[&](std::initializer_list<ToolField> entries) { for(const auto& entry:entries) field(entry.name,entry.label,entry.multiline); };
    auto summary=[&](const char* property) {
        auto content=value(property);
        if(!content.empty()) {ImGui::TextWrapped("%s",content.c_str());cyui::space(4);}
    };
    auto commands=[&](std::initializer_list<std::pair<const char*,const char*>> entries) {
        bool first=true;
        for(auto entry:entries) {
            if(!first && ImGui::GetItemRectMax().x+ImGui::GetStyle().ItemSpacing.x+cyui::buttonWidth(entry.second)
                < ImGui::GetWindowPos().x+ImGui::GetWindowContentRegionMax().x) ImGui::SameLine();
            command(entry.first,entry.second);first=false;
        }
    };
    auto choose=[&](const char* label,const char* setter,const char* parameter,ToolPicker kind=ToolPicker::folder) {
        cyui::ScopedDisabled disabled(busy());
        if(cyui::button(label)) {
            auto path=pickToolPath(kind,label);
            if(!path.empty()) {
                auto item=action(setter);
                if(item.value("confirmation",false)) {invoke(item);parameters_={{parameter,path}};}
                else submitAction(item,{{parameter,path}},false);
                lastPoll_=0;
            }
        }
    };
    auto invokeWith=[&](const char* name,Json parameters,bool confirm=false) {
        auto item=action(name);
        if(confirm || item.value("confirmation",false)) {
            invoke(item);parameters_=std::move(parameters);
        } else submitAction(item,parameters,false);
        lastPoll_=0;
    };
    auto parameterButton=[&](const char* title,const char* name,Json parameters,bool confirm=false) {
        cyui::ScopedDisabled disabled(busy());
        if(confirm ? cyui::dangerButton(title) : cyui::button(title)) invokeWith(name,std::move(parameters),confirm);
    };
    auto scrollBegin=[&](const char* id) {ImGui::BeginChild(id,ImVec2(0,0));};
    auto codeSelection=[&]() {
        int start=0,end=0;
        try {if(!edits_["tools.code.selectionStart"].empty())start=std::stoi(edits_["tools.code.selectionStart"]);if(!edits_["tools.code.selectionEnd"].empty())end=std::stoi(edits_["tools.code.selectionEnd"]);}catch(...) {}
        return Json{{"selectionStart",start},{"selectionEnd",end}};
    };
    auto selectableCode=[&](const char* id) {
        cyui::ScopedID editor(id);auto content=value("CodePreviewText"),path=value("CodePreviewPath");
        if(edits_["tools.code.selectionPath"]!=path){edits_["tools.code.selectionPath"]=path;edits_["tools.code.selectionStart"]="0";edits_["tools.code.selectionEnd"]="0";}
        parameterButton("History of selected lines","LoadCodeSelectionHistoryAsync",codeSelection());ImGui::SameLine();
        if(cyui::ghostButton("Copy file preview"))ImGui::SetClipboardText(content.c_str());summary("CodeSelectionSummary");
        cyui::ScopedFont font(cyui::monoFont());
        ImGuiID editorId=ImGui::GetID("##selectableCode");
        ImGui::InputTextMultiline("##selectableCode",&content,ImVec2(-1,std::max(cyui::dp(100),ImGui::GetContentRegionAvail().y)),ImGuiInputTextFlags_ReadOnly);
        if(auto selection=ImGui::GetInputTextState(editorId)) {
            size_t begin=static_cast<size_t>(std::max(0,selection->GetSelectionStart()));size_t end=static_cast<size_t>(std::max(0,selection->GetSelectionEnd()));
            begin=std::min(begin,content.size());end=std::min(end,content.size());
            edits_["tools.code.selectionStart"]=std::to_string(wide(content.substr(0,begin)).size());
            edits_["tools.code.selectionEnd"]=std::to_string(wide(content.substr(0,end)).size());
        }
    };

    if(workspace_=="code") {
        field("CodeTreeFilter","Find files / glob patterns");
        parameterButton("Refresh","RefreshCodeWorkspaceAsync",{{"preserveLoadedTree",false}});
        field("CodeIncludeHidden","Show hidden files");
        selector("CodeAutoRefreshFrequencies","CodeAutoRefreshFrequency","Automatic refresh");
        summary("CodeWorkspaceSummary");summary("CodeWorkspaceLastUpdatedText");
        if(toolColumns("##solution",0.45f)) {
            scrollBegin("##files");
            if(ImGui::BeginTabBar("##fileModes")) {
                if(ImGui::BeginTabItem("Tree")) {
                    if(!value("CodeTreeFilter").empty()) list("CodeFileSearchResults");
                    else {
                        auto& pageText=edits_["tools.tree.page"];
                        int page=0;try {if(!pageText.empty()) page=std::max(0,std::stoi(pageText));}catch(...) {pageText="0";}
                        requestTable("NativeSolutionTreeRows",200,page*200);
                        auto tables=state_.value("tables",Json::object());
                        auto snapshot=tables.value("NativeSolutionTreeRows",Json::object());
                        int total=snapshot.value("total",0);
                        {cyui::ScopedDisabled disabled(page==0);if(cyui::ghostButton("Previous page")) {pageText=std::to_string(std::max(0,page-1));lastPoll_=0;}}
                        ImGui::SameLine();{cyui::ScopedDisabled disabled((page+1)*200>=total);if(cyui::ghostButton("Next page")) {pageText=std::to_string(page+1);lastPoll_=0;}}
                        ImGui::SameLine();cyui::textDim("%d items",total);
                        if(snapshot.contains("rows") && !snapshot["rows"].empty()) {
                            if(ImGui::BeginTable("##treeRows",4,ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|ImGuiTableFlags_RowBg|ImGuiTableFlags_ScrollX|ImGuiTableFlags_ScrollY)) {
                                ImGui::TableSetupColumn("Name",ImGuiTableColumnFlags_WidthFixed,cyui::dp(300));ImGui::TableSetupColumn("Type",ImGuiTableColumnFlags_WidthFixed,cyui::dp(100));
                                ImGui::TableSetupColumn("Size",ImGuiTableColumnFlags_WidthFixed,cyui::dp(90));ImGui::TableSetupColumn("Details",ImGuiTableColumnFlags_WidthFixed,cyui::dp(220));ImGui::TableSetupScrollFreeze(0,1);ImGui::TableHeadersRow();
                                const auto& rows=snapshot["rows"];ImGuiListClipper clipper;clipper.Begin(static_cast<int>(rows.size()));
                                while(clipper.Step()) for(int index=clipper.DisplayStart;index<clipper.DisplayEnd;++index) {
                                    const auto& row=rows[index];const auto& values=row["values"];auto path=text(values,"RelativePath");auto key=text(row,"key");
                                    cyui::ScopedID nodeId(key.c_str());ImGui::TableNextRow(ImGuiTableRowFlags_None,ImGui::GetFrameHeightWithSpacing());ImGui::TableNextColumn();
                                    const int depth=values.value("Depth",0);if(depth>0)ImGui::Indent(cyui::dp(static_cast<float>(depth)*14));
                                    bool folder=values.value("IsDirectory",false);
                                    if(folder) {
                                        cyui::ScopedDisabled disabled(busy());
                                        if(cyui::ghostButton(values.value("Expanded",false) ? "v" : ">")) {pendingAction_=true;send({{"command","tools.tree.toggle"},{"path",path},{"key",key}});lastPoll_=0;}
                                        ImGui::SameLine();
                                    }
                                    if(ImGui::Selectable(text(values,"Name").c_str(),values.value("Selected",false),ImGuiSelectableFlags_AllowDoubleClick)) {
                                        if(!busy()) {pendingAction_=true;send({{"command",folder && ImGui::IsMouseDoubleClicked(0) ? "tools.tree.toggle" : "tools.tree.select"},{"path",path},{"key",key}});lastPoll_=0;}
                                    }
                                    if(ImGui::BeginPopupContextItem("##treeContext")) {
                                        if(ImGui::MenuItem("Copy project-relative path")) ImGui::SetClipboardText(path.c_str());
                                        if(!folder && ImGui::MenuItem("Git / LFS file history...",nullptr,false,!busy() && flag("IsGitProject"))) {
                                            pendingAction_=true;send({{"command","fileHistory"},{"path",path}});edits_["tools.code.historySelected"]="1";lastPoll_=0;
                                        }
                                        if(!folder && ImGui::MenuItem("View Sync history",nullptr,false,!busy())) {invokeWith("FilterSyncHistoryForFileAsync",{{"relativePath",path}});navigate("sync");}
                                        ImGui::EndPopup();
                                    }
                                    if(depth>0)ImGui::Unindent(cyui::dp(static_cast<float>(depth)*14));
                                    ImGui::TableNextColumn();ImGui::TextUnformatted(folder ? "Folder" : text(values,"Language").c_str());
                                    ImGui::TableNextColumn();if(!folder) ImGui::Text("%lld",values.value("Size",0LL));
                                    ImGui::TableNextColumn();ImGui::TextUnformatted(text(values,"Detail").c_str());
                                }ImGui::EndTable();
                            }
                        } else cyui::emptyState("No project files loaded","Refresh the solution explorer, or select a project.",cyui::icon::folder);
                    }
                    ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("List")) {list("CodeFileList");ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("Search results")) {list("CodeFileSearchResults");ImGui::EndTabItem();}
                ImGui::EndTabBar();
            }
            ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##fileDetails");
            cyui::heading("File preview",3);summary("CodePreviewSummary");
            command("GenerateAiCodeSummaryAsync","AI summary");ImGui::SameLine();
            {cyui::ScopedDisabled disabled(busy() || !flag("IsGitProject") || value("CodePreviewPath").empty());
                if(cyui::button("Git / LFS history")) {pendingAction_=true;send({{"command","fileHistory"},{"path",value("CodePreviewPath")}});edits_["tools.code.historySelected"]="1";lastPoll_=0;}}
            ImGui::SameLine();
            if(cyui::button("Code search")) navigate("search");
            cyui::space(4);
            if(ImGui::BeginTabBar("##previewDetails")) {
                if(ImGui::BeginTabItem("Preview")) {
                    auto context=state_.value("toolsContext",Json::object());auto imagePath=text(context,"codePreviewPath");
                    if(!imagePath.empty())toolImage(imagePath,"No rendered preview");else selectableCode("##codePreview");
                    ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Symbols")) {list("CodeSymbols");ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("History",nullptr,edits_["tools.code.historySelected"]=="1" ? ImGuiTabItemFlags_SetSelected : ImGuiTabItemFlags_None)) {edits_["tools.code.historySelected"]="0";fileHistoryWindow();ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("AI summary")) {command("ClearAiCodeSummary","Clear");textPanel("CodeAiSummary","##aiSummary");ImGui::EndTabItem();}
                ImGui::EndTabBar();
            }
            ImGui::EndChild();ImGui::EndTable();
        }
        return true;
    }
    if(workspace_=="search") {
        fields({{"CodeSearchQuery","Search project text / regular expression"},{"CodeFilePatterns","File patterns (*.cpp;*.h)"}});
        command("SearchCodeAsync","Search",true);ImGui::SameLine();command("CancelCodeSearch","Cancel");
        fields({{"CodeSearchMatchCase","Match case"},{"CodeSearchWholeWord","Whole words"},{"CodeSearchRegex","Grep / regular expression"}});
        summary("CodeSearchSummary");
        if(toolColumns("##searchSplit",0.45f)) {
            scrollBegin("##searchResults");field("CodeSearchFileFilter","Filter results by path");
            summary("CodeSearchResultFilterSummary");list("FilteredCodeSearchResults");ImGui::EndChild();
            ImGui::TableNextColumn();scrollBegin("##searchPreview");summary("CodePreviewSummary");
            if(ImGui::BeginTabBar("##searchDetailTabs")) {
                if(ImGui::BeginTabItem("Code")) {selectableCode("##searchCode");ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("Symbols")) {list("CodeSymbols");ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("History")) {parameterButton("History of selected lines","LoadCodeSelectionHistoryAsync",codeSelection());summary("CodeSelectionSummary");list("CodeHistory");ImGui::EndTabItem();}
                ImGui::EndTabBar();
            }
            ImGui::EndChild();ImGui::EndTable();
        }
        return true;
    }
    if(workspace_=="console") {
        if(ImGui::BeginTabBar("##consoleTabs")) {
            if(ImGui::BeginTabItem("Repository console")) {
                summary("RepositoryConsoleWorkingDirectory");
                selector("RepositoryShells","SelectedRepositoryShell","Shell");
                command("RunRepositoryCommandAsync","Run",true);ImGui::SameLine();command("StopRepositoryCommand","Stop");ImGui::SameLine();command("ClearRepositoryConsoleOutput","Clear output");
                field("RepositoryConsoleCommand","Command");summary("RepositoryConsoleStatus");
                if(toolColumns("##consoleSplit",0.73f)) {
                    textPanel("RepositoryConsoleOutput","##output");ImGui::TableNextColumn();
                    scrollBegin("##consoleHistory");cyui::heading("Command history",3);field("RepositoryConsoleHistorySearch","Search commands");
                    commands({{"UseSelectedRepositoryCommand","Use selected"},{"ClearRepositoryConsoleHistory","Clear history"}});
                    list("FilteredRepositoryCommandHistory");ImGui::EndChild();ImGui::EndTable();
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Application log")) {
                field("ApplicationLogSearch","Search message / area");
                selector("ApplicationLogLevelFilters","ApplicationLogLevelFilter","Level");
                commands({{"RefreshApplicationLogs","Refresh"},{"ClearApplicationLogView","Clear view"}});
                summary("ApplicationLogProjectSummary");summary("CurrentApplicationLogPath");
                list("FilteredApplicationLogEntries");ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
        return true;
    }
    if(workspace_=="assets") {
        field("AssetExplorerSearch","Find assets");summary("AssetExplorerSummary");
        if(toolColumns("##assetsSplit",0.3f)) {
            scrollBegin("##assetFiles");
            commands({{"UseSelectedAssetAsBaseline","As reference"},{"UseSelectedAssetAsCandidate","As candidate"}});
            list("AssetExplorerFiles");ImGui::EndChild();ImGui::TableNextColumn();
            scrollBegin("##assetCompare");
            field("AssetBaselinePath","Reference");choose("Choose reference...","SetAssetBaseline","path",ToolPicker::file);
            field("AssetCandidatePath","Candidate");choose("Choose candidate...","SetAssetCandidate","path",ToolPicker::file);
            command("CompareAssetsAsync","Compare",true);ImGui::SameLine();command("CompareSelectedChangeToHeadAsync","Git / HEAD");
            if(ImGui::BeginTabBar("##assetViews")) {
                if(ImGui::BeginTabItem("Side by side")) {
                    if(toolColumns("##images",0.5f)) {
                        toolImage(value("AssetBaselinePath"),"No reference preview");ImGui::TableNextColumn();
                        toolImage(value("AssetCandidatePath"),"No candidate preview");ImGui::EndTable();
                    }ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Heatmap / provider preview")) {
                    auto context=state_.value("toolsContext",Json::object());
                    toolImage(text(context,"imagePreviewPath"),"No comparison image");ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Semantic report")) {textPanel("AssetDiffReport","##assetReport");ImGui::EndTabItem();}
                ImGui::EndTabBar();
            }
            ImGui::EndChild();ImGui::EndTable();
        }
        return true;
    }
    if(workspace_=="ai") {
        summary("AiStatus");summary("CodexConnectionStatus");
        command("DetectCodexAsync","Detect provider");ImGui::SameLine();command("ToggleCodexChatConnectionAsync","Connect / disconnect");
        if(toolColumns("##aiSplit",0.34f)) {
            scrollBegin("##aiConfiguration");
            if(cyui::beginSection("Provider",true)) {
                selector("AiProviders","SelectedAiProvider","Provider");summary("AiProviderDescription");
                fields({{"AiModel","Model"},{"AiEndpoint","API endpoint"},{"AiApiKey","Session API key"},{"AiExecutablePath","CLI executable (Codex / OpenCode)"}});
                summary("CodexDetectedVersion");summary("CodexDetectedPath");cyui::endSection();
            }
            if(cyui::beginSection("Permissions",false)) {
                cyui::textDim("Read-only repository access is required. Writes and network remain opt-in.");
                fields({{"AiAllowModify","Allow project file changes"},{"AiAllowNetwork","Allow network / web search"},{"AiStageAfterRun","Stage after success"},{"AiCommitAfterRun","Commit after success"},{"AiCommitMessage","Commit message"}});
                command("RunAiAgentAsync","Run one-shot provider");cyui::endSection();
            }
            cyui::sectionTitle("Conversations");field("NewAiConversationTitle","New conversation title");
            command("CreateAiConversationAsync","Create conversation");
            list("AiConversations",ImVec2(0,cyui::dp(160)));
            if(cyui::beginSection("Conversation settings",false)) {
                fields({{"AiRenderMarkdown","Render Markdown"},{"AiConversationUseWorktree","Isolated worktree"},{"AiConversationPrePrompt","Conversation instructions",true}});
                commands({{"SaveAiConversationSettingsAsync","Save settings"},{"DeleteSelectedAiConversationAsync","Delete conversation"}});
                summary("AiConversationWorkspace");cyui::endSection();
            }
            ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##aiConversation");
            summary("SelectedAiConversation.Title");summary("CodexChatThreadId");
            list("AiChatMessages",ImVec2(0,std::max(cyui::dp(160),ImGui::GetContentRegionAvail().y-cyui::dp(150))));
            field("AiPrompt","Message",true);
            command("SendCodexChatMessageAsync","Send",true);ImGui::SameLine();command("CancelAiChat","Cancel turn");
            ImGui::EndChild();ImGui::EndTable();
        }
        return true;
    }
    if(workspace_=="mcp") {
        fields({{"AiMcpEnabled","Enable MCP for this project"},{"AiMcpBlockUnmanagedServers","Block unmanaged servers"}});
        summary("AiMcpEmergencyState");summary("AiMcpStatus");
        command("SaveAiMcpProfileAsync","Save policy",true);ImGui::SameLine();
        command("EmergencyBlockAiMcpAsync","Block all MCP");ImGui::SameLine();command("UnblockAiMcpAsync","Unblock");
        if(toolColumns("##mcpSplit",0.34f)) {
            scrollBegin("##mcpServers");
            parameterButton("Add STDIO","AddAiMcpServer",{{"transport","Stdio"}});ImGui::SameLine();
            parameterButton("Add HTTP","AddAiMcpServer",{{"transport","StreamableHttp"}});ImGui::SameLine();
            command("RemoveSelectedAiMcpServer","Remove");list("AiMcpServers");ImGui::EndChild();
            ImGui::TableNextColumn();scrollBegin("##mcpConfiguration");
            if(cyui::beginSection("Identity and permissions",true)) {
                fields({{"SelectedAiMcpServer.Name","Name"},{"SelectedAiMcpServer.Id","ID"},{"SelectedAiMcpServer.Enabled","Enabled"},{"SelectedAiMcpServer.Required","Required"},{"SelectedAiMcpServer.Transport","Transport"},{"SelectedAiMcpServer.Capability","Capability"},{"SelectedAiMcpServer.RequiresNetwork","Requires network permission"}});cyui::endSection();
            }
            if(cyui::beginSection("STDIO process",true)) {
                fields({{"SelectedAiMcpServer.Command","Command"},{"SelectedAiMcpServer.Arguments","Arguments (one per line)",true},{"SelectedAiMcpServer.WorkingDirectory","Working directory"},{"SelectedAiMcpServer.EnvironmentVariables","Environment overrides",true},{"SelectedAiMcpServer.ForwardEnvironmentVariables","Forward environment variable names",true}});cyui::endSection();
            }
            if(cyui::beginSection("HTTP and authentication",true)) {
                fields({{"SelectedAiMcpServer.Url","URL"},{"SelectedAiMcpServer.HttpAuth","Authentication"},{"SelectedAiMcpServer.BearerTokenEnvironmentVariable","Bearer-token environment name"},{"SelectedAiMcpServer.OAuthScopes","OAuth scopes"},{"SelectedAiMcpServer.OAuthResource","OAuth resource"},{"SelectedAiMcpServer.HttpHeaders","Literal headers",true},{"SelectedAiMcpServer.EnvironmentHttpHeaders","Headers from environment",true}});cyui::endSection();
            }
            if(cyui::beginSection("Tools, approvals and timeouts",true)) {
                fields({{"SelectedAiMcpServer.EnabledTools","Allowed tools",true},{"SelectedAiMcpServer.DisabledTools","Blocked tools",true},{"SelectedAiMcpServer.ApprovalMode","Approval policy"},{"SelectedAiMcpServer.ToolApprovalOverrides","Per-tool approval overrides",true},{"SelectedAiMcpServer.StartupTimeoutSeconds","Startup timeout (s)"},{"SelectedAiMcpServer.ToolTimeoutSeconds","Tool timeout (s)"}});cyui::endSection();
            }
            ImGui::EndChild();ImGui::EndTable();
        }
        return true;
    }
    if(workspace_=="sync") {
        summary("SynchronizationProfileTitle");summary("SynchronizationProfileDescription");summary("SynchronizationScopeSummary");
        if(ImGui::BeginTabBar("##syncTabs")) {
            if(ImGui::BeginTabItem("Setup wizard")) {
                scrollBegin("##syncSetup");
                cyui::sectionTitle("1. Source folder");field("SyncSourceFolderPath","Synchronized folder");choose("Choose source folder...","SetSyncSourceFolderPath","path");
                cyui::sectionTitle("2. Runtime");summary("SyncthingRuntimeSummary");command("ConfigureSyncthingAutomaticallyAsync","Detect and configure runtime",true);
                field("SyncthingExecutablePath","Dedicated Syncthing executable");choose("Choose runtime...","SetSyncthingExecutableAsync","executablePath",ToolPicker::file);
                cyui::sectionTitle("3. Versions");field("SyncVersionStorePath","Separate version store");choose("Choose version store...","SetSyncVersionStorePath","path");
                selector("SyncthingFolderModeNames","SelectedSyncthingFolderModeName","Folder mode");
                fields({{"SyncthingRescanInterval","Rescan interval (seconds)"},{"SyncthingFileWatcherEnabled","Watch file-system changes"}});
                cyui::sectionTitle("4. Apply and start");commands({{"SaveSyncthingSettingsAsync","Save settings"},{"StartSyncAsync","Start"},{"RefreshSyncthingWorkspaceAsync","Refresh"}});
                summary("SyncState");summary("SyncDetails");ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Overview")) {
                scrollBegin("##syncOverview");summary("SyncState");summary("SyncDetails");summary("SyncthingRuntimeSummary");
                commands({{"StartSyncAsync","Start"},{"PauseSyncAsync","Pause"},{"ResumeSyncAsync","Resume"},{"StopSyncAsync","Stop"},{"RefreshSyncthingWorkspaceAsync","Refresh"},{"ScanSyncthingFolderAsync","Scan folder"}});
                if(cyui::beginSection("Synchronization scope",true)) {
                    field("SyncSourceFolderPath","Source folder");choose("Choose source...","SetSyncSourceFolderPath","path");
                    selector("SyncthingFolderModeNames","SelectedSyncthingFolderModeName","Folder mode");
                    fields({{"SyncthingRescanInterval","Rescan interval (seconds)"},{"SyncthingFileWatcherEnabled","File-system watcher"}});
                    command("SaveSyncthingSettingsAsync","Save and apply");cyui::endSection();
                }
                if(cyui::beginSection("Git + Sync and LFS policy",true)) {
                    command("ExchangeGitAsync","Exchange Git now");selector("LfsHistoryModes","SelectedLfsHistoryMode","LFS history policy");
                    fields({{"SmartSyncRecentVersionCount","Recent asset versions"},{"SmartSyncReplicateBackups","Replicate backups"}});
                    summary("SmartSyncPlanSummary");list("SmartSyncPlanItems",ImVec2(0,cyui::dp(190)));cyui::endSection();
                }
                if(cyui::beginSection("Versioned Sync storage",true)) {
                    field("SyncVersionStorePath","Version store");choose("Choose version store...","SetSyncVersionStorePath","path");
                    field("SyncCompressedBackupEnabled","Keep compressed backup copy");field("SyncCompressedBackupPath","Compressed backup destination");choose("Choose compressed backup folder...","SetSyncCompressedBackupPath","path");
                    command("CreateCompressedSyncBackupAsync","Create compressed copy now");selector("RetentionModes","SelectedRetentionMode","Retention mode");
                    fields({{"RetentionVersions","Versions retained"},{"RetentionDays","Days retained"},{"RetentionBudgetGb","Storage budget (GB)"}});
                    {cyui::ScopedDisabled disabled(busy());if(cyui::button("Save Sync + Versions")) {pendingAction_=true;send({{"command","tools.saveSyncVersionSettings"},{"edits",draftValues_}});}}
                    summary("SyncStorageStatus");cyui::endSection();
                }
                ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Commits")) {
                fields({{"SyncCommitAuthor","Author"},{"SyncCommitMessage","Commit message",true}});
                command("CreateSyncCommitAsync","Commit and publish",true);ImGui::SameLine();command("RefreshSyncCommitsAsync","Refresh");summary("SyncCommitStatus");
                if(toolColumns("##syncCommits")) {
                    list("SyncCommits");ImGui::TableNextColumn();scrollBegin("##syncCommitConflicts");
                    commands({{"AnalyzeSelectedSyncCommitAsync","Analyze selected"},{"ApplySelectedSyncCommitAsync","Apply commit"}});summary("SyncCommitConflictSummary");
                    parameterButton("Keep local","ResolveSelectedSyncCommitConflict",{{"choice","KeepLocal"}});ImGui::SameLine();parameterButton("Use incoming","ResolveSelectedSyncCommitConflict",{{"choice","UseIncoming"}});
                    list("SyncCommitConflicts");ImGui::EndChild();ImGui::EndTable();
                }ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Devices")) {command("RefreshSyncthingWorkspaceAsync","Refresh devices");list("SyncthingDevices");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("Differences")) {summary("SyncthingFolderSummary");command("ScanSyncthingFolderAsync","Scan and refresh");list("SyncthingDifferences");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("History")) {field("SyncHistorySearch","Search path / action / direction");commands({{"ClearSyncHistoryFileFilter","Clear file filter"},{"RefreshSyncHistoryAsync","Search"}});summary("SyncHistorySummary");list("SyncHistory");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("Ignore rules")) {
                scrollBegin("##syncIgnore");commands({{"LoadSyncthingIgnoreRulesAsync","Reload"},{"UseSyncthingUnrealIgnoreTemplate","Unreal template"},{"SaveSyncthingIgnoreRulesAsync","Save .stignore"}});
                field("SyncthingIgnoreRules",".stignore",true);summary("SyncthingIgnoreStatus");
                if(cyui::beginSection("Folder suggestions",true)) {field("IgnoreFolderSearch","Search folders / patterns");selector("IgnoreSuggestionFilters","IgnoreFolderFilter","Show");list("IgnoreFolderSuggestions",ImVec2(0,cyui::dp(170)));cyui::endSection();}
                if(cyui::beginSection("File type suggestions",true)) {field("IgnoreFileTypeSearch","Search extensions");selector("IgnoreSuggestionFilters","IgnoreFileTypeFilter","Show");list("IgnoreFileTypeSuggestions",ImVec2(0,cyui::dp(170)));cyui::endSection();}
                commands({{"LoadIgnoreSuggestionsAsync","Rescan"},{"AppendSelectedSuggestionsToSyncthingIgnore","Add checked suggestions"}});summary("IgnoreSuggestionSummary");ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Peers and roles")) {
                scrollBegin("##syncPeers");selector("PeerRoles","SelectedPeerRole","Invitation role");field("PeerVerificationCode","Verification code");command("CreatePeerInvitationAsync","Create invitation (24 hours)");
                list("PeerMembers",ImVec2(0,cyui::dp(180)));selector("PeerRoles","SelectedPeerMemberRole","Selected member role");commands({{"UpdateSelectedPeerRoleAsync","Update signed role"},{"RevokeSelectedPeerAsync","Revoke member"}});
                field("PeerExchangeText","Invitation / join request / membership certificate",true);commands({{"PreparePeerJoinRequestAsync","Prepare request"},{"ApprovePeerJoinRequestAsync","Approve request"},{"ImportMembershipGrantAsync","Import certificate"}});
                ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Logs")) {command("RefreshSyncthingWorkspaceAsync","Refresh");list("SyncthingLogs");ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="syncconflicts") {
        field("SyncConflictSearch","Filter file / folder / scope / state");field("SyncConflictRetentionDays","Resolution backup retention (days)");command("RefreshSyncConflictsAsync","Scan conflicts",true);summary("SyncConflictSummary");
        if(toolColumns("##syncConflicts")) {
            scrollBegin("##conflictFiles");cyui::heading("Conflicting files",3);
            parameterButton("Keep original...","ResolveSelectedSyncConflictAsync",{{"resolution","KeepOriginal"}},true);ImGui::SameLine();parameterButton("Use conflict version...","ResolveSelectedSyncConflictAsync",{{"resolution","UseConflict"}},true);
            cyui::textDim("Both versions are backed up before resolution.");list("SyncConflicts");ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##conflictBackups");cyui::heading("Recoverable resolutions",3);
            commands({{"RestoreSelectedSyncConflictBackupAsync","Restore selected..."},{"CleanExpiredSyncConflictBackupsAsync","Clean expired backups"}});list("SyncConflictBackups");ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    if(workspace_=="backups") {
        command("CreateBackupAsync","Back up now",true);ImGui::SameLine();choose("Restore selected snapshot into...","RestoreSelectedBackupAsync","destinationDirectory");
        if(ImGui::BeginTabBar("##backupTabs")) {
            if(ImGui::BeginTabItem("Snapshots")) {list("Backups");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("Storage and retention")) {
                scrollBegin("##backupPolicy");cyui::sectionTitle("Hot backup storage");field("BackupStorePath","Backup store");choose("Choose backup store...","SetBackupStoreAsync","path");
                selector("RetentionModes","SelectedRetentionMode","Retention mode");fields({{"RetentionVersions","Versions retained"},{"RetentionDays","Days retained"},{"RetentionBudgetGb","Budget (GB)"}});
                selector("BackupArchiveProfiles","SelectedBackupArchiveProfile","Backup archive profile");summary("BackupArchiveProfileDescription");cyui::sectionTitle("Verified cold storage");
                field("ColdArchivePath","Cold archive folder");choose("Choose cold archive...","SetColdArchivePath","path");field("ColdArchiveAfterDays","Archive versions older than (days)");field("RemoveArchivedHotCopies","After verification, remove eligible hot copies");
                cyui::callout(cyui::Tone::info,"Cold archival is non-destructive by default. Hot copies are removed only when explicitly enabled and confirmed.");
                parameterButton("Archive old backups...","ArchiveOldBackupsAsync",Json::object(),flag("RemoveArchivedHotCopies"));summary("ColdArchiveStatus");command("SaveBackupSettingsAsync","Save policy");ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Git cold archives")) {
                selector("GitArchiveProfiles","SelectedGitArchiveProfile","Git archive profile");summary("GitArchiveProfileDescription");command("AnalyzeGitArchiveCandidatesAsync","Analyze branches");field("RemoveGitBranchAfterArchive","After bundle verification, remove eligible local branch reference");
                parameterButton("Create verified cold bundle...","ArchiveSelectedGitBranchAsync",Json::object(),flag("RemoveGitBranchAfterArchive"));cyui::textDim("Remote branches are unchanged; the current branch is protected. No automatic Git garbage collection.");summary("GitArchiveStatus");
                if(toolColumns("##gitCold")) {list("GitArchiveCandidates");ImGui::TableNextColumn();scrollBegin("##gitColdRestores");command("RestoreSelectedGitArchiveAsync","Restore as new local branch");list("ArchivedGitBranches");ImGui::EndChild();ImGui::EndTable();}ImGui::EndTabItem();
            }ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="sharedsync") {
        summary("SharedSyncFolderStatus");
        if(toolColumns("##sharedSync")) {
            scrollBegin("##sharedFolderSetup");cyui::heading("Independent synchronized folders",3);fields({{"SharedSyncFolderName","Display name"},{"SharedSyncFolderPath","Folder path"}});choose("Choose shared folder...","SetSharedSyncFolderPath","path");
            selector("SyncthingFolderModeNames","SelectedSharedSyncFolderModeName","Folder mode");command("AddOrUpdateSharedSyncFolderAsync","Add / update",true);ImGui::SameLine();command("ScanSelectedSharedSyncFolderAsync","Scan");ImGui::SameLine();command("RemoveSelectedSharedSyncFolderAsync","Remove mapping");
            list("SharedSyncFolders");ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##sharedHistory");cyui::heading("Folder history",3);field("SyncHistorySearch","Search path / action / direction");
            commands({{"ClearSyncHistoryFileFilter","Clear file filter"},{"RefreshSyncHistoryAsync","Search"}});summary("SyncHistorySummary");list("SyncHistory");ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    if(workspace_=="vpn") {
        summary("VpnState");summary("VpnDetails");
        if(ImGui::BeginTabBar("##vpnTabs")) {
            if(ImGui::BeginTabItem("Configuration")) {
                scrollBegin("##vpnConfiguration");selector("VpnBackendModes","SelectedVpnBackendMode","VPN backend");summary("VpnBackendDetails");
                command("ConfigureVpnAsync","Configure selected backend",true);
                fields({{"WireGuardExecutablePath","WireGuard executable"},{"VpnNetworkCidr","Project network (CIDR)"},{"VpnLocalAddress","Local tunnel address"},{"VpnListenPort","Listen port"},{"VpnPublicEndpoint","Public endpoint"}});
                selector("VpnCapabilities","SelectedVpnCapability","This peer's capability");
                commands({{"SaveVpnSettingsAsync","Save"},{"StartVpnAsync","Activate"},{"StopVpnAsync","Stop"},{"RefreshVpnAsync","Refresh"}});
                cyui::sectionTitle("Configuration preview");textPanel("VpnConfigurationPreview","##vpnPreview");ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Setup assistant")) {
                scrollBegin("##vpnSetup");summary("VpnSetupSummary");summary("VpnSetupNetwork");
                fields({{"VpnSetupAcceptIncoming","Accept incoming VPN connections"},{"VpnSetupAllowSwarm","Allow Unreal Swarm"},{"VpnSetupAllowControlApi","Allow CyRevision control API"},{"VpnSetupAllowFileExchange","Allow secure file exchange"},{"VpnSetupAllowRemoteBuild","Allow remote build agent"}});
                commands({{"InspectVpnSetupAsync","1. Diagnose computer"},{"ApplyVpnFirewallAsync","2. Apply project firewall rules..."},{"RemoveVpnFirewallAsync","Remove project rules..."},{"OpenVpnRouterAsync","3. Open router page"},{"TestVpnConnectivityAsync","4. Test tunnel and peers"}});
                summary("VpnFirewallStatus");summary("VpnConnectivityStatus");
                if(ImGui::BeginTabBar("##vpnGuides")) {
                    if(ImGui::BeginTabItem("Computer guide")) {textPanel("VpnComputerGuide","##computerGuide");ImGui::EndTabItem();}
                    if(ImGui::BeginTabItem("Router guide")) {textPanel("VpnRouterGuide","##routerGuide");ImGui::EndTabItem();}
                    if(ImGui::BeginTabItem("Generated firewall commands")) {textPanel("VpnFirewallCommands","##firewallCommands");ImGui::EndTabItem();}
                    ImGui::EndTabBar();
                }ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Peers and invitations")) {
                if(toolColumns("##vpnPeers")) {
                    list("VpnPeers");ImGui::TableNextColumn();scrollBegin("##vpnInvitations");
                    field("VpnInvitationClientName","Client name");selector("VpnCapabilities","SelectedVpnInvitationCapability","Granted capability");command("CreateVpnInvitationAsync","Create VPN invitation (24 hours)");
                    field("VpnExchangeText","Invitation / signed peer response",true);
                    commands({{"JoinVpnInvitationAsync","Join invitation"},{"AcceptVpnResponseAsync","Accept response"},{"RemoveSelectedVpnPeerAsync","Remove selected peer..."}});ImGui::EndChild();ImGui::EndTable();
                }ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Sync exchange")) {
                summary("VpnSyncStatus");commands({{"PublishVpnExchangeViaSyncAsync","Share current message"},{"RefreshVpnSyncMessagesAsync","Refresh inbox"},{"LoadSelectedVpnSyncMessageAsync","Load selected"}});list("VpnSyncMessages");ImGui::EndTabItem();
            }ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="swarm") {
        summary("SwarmStatus");
        if(toolColumns("##swarmSplit",0.5f)) {
            scrollBegin("##swarmSetup");cyui::sectionTitle("Role and coordinator");selector("SwarmRoles","SelectedSwarmRole","Role");
            fields({{"SwarmCoordinatorAddress","Coordinator tunnel address"},{"SwarmCoordinatorAlias","Local DNS alias"},{"SwarmAgentPath","Swarm Agent executable"}});choose("Choose Agent...","SetSwarmAgentPath","path",ToolPicker::file);
            field("SwarmCoordinatorPath","Swarm Coordinator executable");choose("Choose Coordinator...","SetSwarmCoordinatorPath","path",ToolPicker::file);
            field("SwarmOptionsPath","Agent options XML");choose("Choose Agent options...","SetSwarmOptionsPath","path",ToolPicker::file);
            fields({{"SwarmAgentGroup","Agent group"},{"SwarmAllowedGroup","Allowed group"},{"SwarmAllowedAgents","Allowed agents"},{"SwarmCacheFolder","Swarm cache folder"}});choose("Choose cache folder...","SetSwarmCacheFolder","path");
            command("SaveSwarmAsync","Save session",true);ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##swarmActions");
            cyui::sectionTitle("Configuration and diagnosis");commands({{"ApplySwarmOptionsAsync","Apply Agent configuration"},{"ApplySwarmDnsAsync","Apply local DNS alias..."},{"RemoveSwarmDnsAsync","Remove local alias..."},{"DiagnoseSwarmAsync","Run full diagnosis"},{"LaunchSwarmAgentAsync","Launch Agent"},{"LaunchSwarmCoordinatorAsync","Launch Coordinator"}});
            textPanel("SwarmDiagnostic","##swarmDiagnostic");ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    if(workspace_=="vpnfiles") {
        summary("VpnFileStatus");
        if(toolColumns("##vpnFiles",0.45f)) {
            scrollBegin("##vpnFileEndpoint");cyui::sectionTitle("Local endpoint");
            fields({{"VpnFileAccessToken","Access token"},{"VpnFileListenPort","Listen port"},{"VpnFileInboxPath","Private inbox folder"}});choose("Choose private inbox...","SetVpnFileInboxPath","path");
            field("VpnFileSharedFolderPath","Explicitly shared folder");choose("Choose shared folder...","SetVpnFileSharedFolderPath","path");
            fields({{"VpnFileAllowReceive","Receive files into inbox"},{"VpnFileAllowBrowse","Expose file list"},{"VpnFileAllowDownload","Allow downloads"},{"VpnFileStartAutomatically","Start with project"}});
            command("SaveVpnFileExchangeAsync","Save endpoint",true);commands({{"StartVpnFileExchangeAsync","Start endpoint"},{"StopVpnFileExchangeAsync","Stop endpoint"},{"RotateVpnFileTokenAsync","Rotate token..."}});
            cyui::callout(cyui::Tone::info,"Only the configured folder is shared. The private inbox is separate; token rotation stops the endpoint and requires a new trusted exchange.");
            ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##vpnFilePeer");cyui::sectionTitle("Authenticated peer");
            selector("VpnPeers","SelectedVpnFilePeer","Peer");commands({{"TestVpnFilePeerAsync","Test peer"},{"RefreshVpnSharedFilesAsync","Refresh shared folder"}});
            choose("Send file to peer inbox...","SendVpnFileAsync","path",ToolPicker::file);ImGui::SameLine();choose("Download selected as...","DownloadVpnSharedFileAsync","destinationPath",ToolPicker::save);
            list("VpnSharedFiles");ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    if(workspace_=="teamchat") {
        summary("TeamChatStatus");
        if(ImGui::BeginTabBar("##teamChatTabs")) {
            if(ImGui::BeginTabItem("Chat")) {
                if(toolColumns("##chat",0.28f)) {
                    scrollBegin("##chatChannels");cyui::heading("Channels",3);list("TeamChatChannels",ImVec2(0,cyui::dp(180)));
                    fields({{"TeamChatNewChannelName","New channel"},{"TeamChatNewChannelTopic","Topic"}});command("CreateTeamChatChannelAsync","Create channel");
                    cyui::sectionTitle("Participants");list("TeamChatParticipants");ImGui::EndChild();ImGui::TableNextColumn();scrollBegin("##chatMessages");
                    summary("SelectedTeamChatChannelTitle");summary("SelectedTeamChatChannelTopic");command("RefreshTeamChatAsync","Refresh messages");
                    list("TeamChatMessages",ImVec2(0,std::max(cyui::dp(160),ImGui::GetContentRegionAvail().y-cyui::dp(205))));
                    field("TeamChatMessageText","Message",true);field("TeamChatAttachmentPath","Attachment");choose("Attach file...","SetTeamChatAttachment","path",ToolPicker::file);ImGui::SameLine();
                    {cyui::ScopedDisabled disabled(busy());if(cyui::button("Open selected attachment")) {pendingAction_=true;send({{"command","tools.openChatAttachment"}});}}
                    ImGui::SameLine();command("SendTeamChatMessageAsync","Send",true);ImGui::EndChild();ImGui::EndTable();
                }ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Connection and storage")) {
                scrollBegin("##chatSettings");selector("TeamChatTransports","SelectedTeamChatTransport","Transport");field("TeamChatDisplayName","Display name");
                if(cyui::beginSection("VPN chat host",true)) {
                    fields({{"TeamChatPort","Port"},{"TeamChatListenAddress","Listen address"},{"TeamChatPeerEndpoint","Peer endpoint"},{"TeamChatAccessToken","Access token"}});command("RotateTeamChatTokenAsync","Rotate token...");cyui::endSection();
                }
                if(cyui::beginSection("Sync / private server",true)) {
                    field("TeamChatSyncFolderPath","Synchronized chat folder");choose("Choose chat folder...","SetTeamChatSyncFolder","path");
                    fields({{"TeamChatServerBaseUrl","Server URL"},{"TeamChatServerApiToken","Server API token"},{"TeamChatAllowPrivateServerHttp","Allow HTTP on trusted private LAN / VPN"}});cyui::endSection();
                }
                fields({{"TeamChatSaveConversations","Save conversations"},{"TeamChatEncryptStoredConversations","Encrypt local / Sync archive"},{"TeamChatRetentionDays","Retention (days)"},{"TeamChatMaxAttachmentMb","Maximum attachment (MB)"}});
                command("SaveTeamChatAsync","Save settings",true);commands({{"StartTeamChatHostAsync","Host over VPN"},{"StopTeamChatHostAsync","Stop host"},{"RefreshTeamChatAsync","Refresh"}});ImGui::EndChild();ImGui::EndTabItem();
            }ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="builds") {
        summary("RemoteBuildStatus");
        if(toolColumns("##remoteBuild",0.44f)) {
            scrollBegin("##remoteBuildConfiguration");cyui::sectionTitle("Remote build agent");
            fields({{"RemoteBuildEndpoint","Agent endpoint"},{"RemoteBuildAccessToken","Access token"},{"RemoteBuildRecipeId","Recipe ID"}});selector("RemoteBuildSourceModes","SelectedRemoteBuildSourceMode","Source transfer mode");
            field("RemoteBuildArtifactDestination","Artifact destination");choose("Choose artifact folder...","SetRemoteBuildArtifactDestination","path");
            fields({{"RemoteBuildAllowPrivateHttp","Trust HTTP only on private / VPN IP"},{"RemoteBuildMaximumUploadGb","Maximum upload (GB)"}});
            commands({{"SaveRemoteBuildAsync","Save"},{"TestRemoteBuildAsync","Test agent"}});command("StartRemoteBuildAsync","Start remote build",true);ImGui::SameLine();command("CancelRemoteBuildAsync","Cancel");
            ImGui::EndChild();ImGui::TableNextColumn();textPanel("RemoteBuildLog","##remoteBuildLog");ImGui::EndTable();
        }return true;
    }
    if(workspace_=="discord") {
        scrollBegin("##discordSettings");summary("DiscordConnectionSummary");summary("DiscordAgentState");summary("DiscordAgentDetails");summary("DiscordLastActivity");
        selector("DiscordExecutionModes","SelectedDiscordExecutionMode","Execution mode");
        fields({{"DiscordAgentEndpoint","Agent endpoint"},{"DiscordAgentApiToken","Agent API token"},{"DiscordAgentRepositoryPath","Repository on the agent"},{"DiscordAllowPrivateHttp","Allow HTTP only through a trusted private VPN"}});
        commands({{"ConnectDiscordAgentAsync","Connect"},{"LaunchLocalDiscordAgentAsync","Launch local agent"}});
        cyui::sectionTitle("Notifications");fields({{"DiscordWebhookUrl","Webhook URL"},{"DiscordDisplayName","Display name"},{"DiscordProjectLabel","Project label"},{"DiscordRepositoryWebUrl","Repository web URL"},{"DiscordPollIntervalSeconds","Poll interval (seconds)"},{"DiscordNotifyCommits","Grouped new commits"},{"DiscordNotifyBranchChanges","Active branch changes"},{"DiscordStartAutomatically","Start automatically with project"}});
        command("SaveDiscordAgentAsync","Save settings",true);commands({{"TestDiscordWebhookAsync","Send test"},{"RemoveDiscordWebhookAsync","Remove webhook..."},{"StartDiscordAgentAsync","Start"},{"StopDiscordAgentAsync","Stop"},{"CheckDiscordAgentNowAsync","Check now"}});ImGui::EndChild();return true;
    }
    if(workspace_=="wip") {
        summary("ReservationSummary");commands({{"RefreshAdvisoryReservationsAsync","Refresh reservations"},{"RemoveExpiredAdvisoryReservationsAsync","Clean expired reservations"}});list("AdvisoryReservations");return true;
    }
    if(workspace_=="plugins") {
        cyui::callout(cyui::Tone::info,"Plugins are enabled per project. Their modes and tabs become available only when the active plugin and project configuration support them.");
        if(toolColumns("##plugins",0.5f)) {
            list("Plugins");ImGui::TableNextColumn();scrollBegin("##pluginDetails");
            summary("SelectedPlugin.Name");summary("SelectedPlugin.Category");summary("SelectedPlugin.Version");summary("SelectedPlugin.State");summary("SelectedPlugin.Description");
            command("EnableSelectedPluginAsync","Enable for project",true);ImGui::SameLine();command("DisableSelectedPluginAsync","Disable");
            cyui::sectionTitle("Engine integrations");
            if(cyui::button("Unreal companion")) navigate("unreal");ImGui::SameLine();if(cyui::button("Unity companion")) navigate("unity");ImGui::SameLine();if(cyui::button("Godot addon")) navigate("godot");
            if(cyui::button("Lore workspace")) navigate("lore");ImGui::SameLine();if(cyui::button("Perforce workspace")) navigate("perforce");ImGui::SameLine();if(cyui::button("CyStore Alpha")) navigate("cystore");
            ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    if(workspace_=="unreal" || workspace_=="unity" || workspace_=="godot") {
        const std::string engine=workspace_=="unreal" ? "Unreal" : workspace_=="unity" ? "Unity" : "Godot";
        scrollBegin("##engineIntegration");
        if(!flag(("Is"+engine+"IntegrationEnabled").c_str())) cyui::callout(cyui::Tone::warning,"Enable this engine plugin for the selected project in Extensions / Plugins.");
        fields({{("Is"+engine+"ProjectDetected").c_str(),"Project detected"}});
        field((engine+"ProjectPath").c_str(),"Project");
        choose("Choose engine project...",("Set"+engine+"ProjectPath").c_str(),"path",workspace_=="unreal" ? ToolPicker::file : ToolPicker::folder);
        summary((engine+"EngineVersion").c_str());summary((engine+"CompatibilityStatus").c_str());summary((engine+"SupportedVersions").c_str());
        summary((engine+"BundledPluginVersion").c_str());summary((engine+"InstalledPluginVersion").c_str());
        if(workspace_=="unreal") {summary("UnrealEditorPluginVersion");summary("UnrealPrecompiledVersions");summary("UnrealProjectKindSummary");summary("UnrealInstallModeSummary");}
        summary((engine+"PluginSummary").c_str());
        command(("Install"+engine+"EditorPluginAsync").c_str(),"Install / update companion",true);ImGui::SameLine();command(("Configure"+engine+"BridgeAsync").c_str(),"Configure direct connection");
        cyui::textDim("Installation is local to the engine project. Existing companion files are backed up before an update.");summary((engine+"BridgeSummary").c_str());
        if(workspace_=="unreal" && cyui::beginSection("Advanced asset inspection",true)) {
            fields({{"UnrealAdvancedAssetInspectionEnabled","Enable advanced asset inspection"},{"UnrealRenderMeshThumbnails","Render neutral mesh thumbnails"},{"UnrealAssetPreviewResolution","Preview resolution"},{"UnrealAssetCacheBudgetGigabytes","Cache budget (GB)"}});
            commands({{"SaveUnrealAssetInspectionOptionsAsync","Save preview settings"},{"RefreshUnrealAssetInspectionCacheAsync","Refresh cache status"},{"ClearUnrealAssetInspectionCacheAsync","Clear generated cache..."}});summary("UnrealAssetInspectionSummary");
            if(cyui::button("Open local build lab")) navigate("unrealbuilds");cyui::endSection();
        }
        ImGui::EndChild();return true;
    }
    if(workspace_=="lore") {
        summary("LoreProjectSummary");summary("LoreRepositorySummary");summary("LoreCliSummary");
        if(!flag("IsLoreIntegrationEnabled")) cyui::callout(cyui::Tone::warning,"Enable the Lore Alpha plugin for this project before using Lore commands.");
        if(ImGui::BeginTabBar("##loreTabs")) {
            if(ImGui::BeginTabItem("Working tree")) {
                scrollBegin("##loreWorking");field("LoreProjectPath","Lore project");choose("Choose Lore project...","SetLoreProjectPath","path");
                field("LoreCliPath","Lore CLI executable");commands({{"DetectLoreCliAsync","Detect CLI"},{"ReadLoreStatusAsync","Refresh status"},{"ScanLoreStatusAsync","Scan working tree..."},{"ListLoreBranchesAsync","Load branches"}});
                field("LoreStagePath","Project-relative file / folder / Lore glob");command("StageLorePathAsync","Stage path");
                field("LoreCommitMessage","Commit message",true);command("CommitLoreAsync","Commit",true);ImGui::SameLine();command("SyncLoreAsync","Sync");ImGui::SameLine();command("PushLoreAsync","Push...");
                textPanel("LoreStatusOutput","##loreStatus");ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Branches")) {
                field("LoreNewBranchName","Branch name");commands({{"CreateLoreBranchAsync","Create branch"},{"SwitchLoreBranchAsync","Switch branch..."},{"ListLoreBranchesAsync","Refresh"}});textPanel("LoreBranchesOutput","##loreBranches");ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Unreal companion")) {
                summary("LoreUnrealCompanionVersion");field("IsLoreUnrealProjectDetected","Unreal project detected");field("IsLoreUnrealCompanionInstalled","Companion installed");
                command("InstallLoreUnrealCompanionAsync","Install / update companion",true);
                cyui::callout(cyui::Tone::info,"This CyRevision companion links Unreal to the Lore workspace. It is not Epic's official Lore source-control provider. Lore uses a centralized server of record and remains separate from Git / Syncthing.");ImGui::EndTabItem();
            }ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="perforce") {
        if(!flag("IsPerforceIntegrationEnabled")) cyui::callout(cyui::Tone::warning,"Enable the Perforce Alpha plugin for this project.");
        if(cyui::beginSection("Connection",true)) {
            fields({{"PerforceCliPath","P4 executable"},{"PerforceServer","Server (P4PORT)"},{"PerforceUser","User (P4USER)"},{"PerforceWorkspace","Workspace (P4CLIENT)"},{"PerforceIncludeOtherWorkspaces","Include other workspaces"},{"PerforceWriteOperationsEnabled","Explicitly enable workspace writes"}});
            commands({{"DetectPerforceCliAsync","Detect CLI"},{"SavePerforceConfigurationAsync","Save configuration"},{"RefreshPerforceAsync","Validate and refresh"}});summary("PerforceCliSummary");summary("PerforceConnectionSummary");cyui::endSection();
        }
        if(ImGui::BeginTabBar("##perforceTabs")) {
            if(ImGui::BeginTabItem("Opened files")) {
                field("PerforceOpenedSearch","Find file / action / user / workspace / changelist");command("LoadPerforceFileHistoryAsync","File history");ImGui::SameLine();
                {cyui::ScopedDisabled disabled(!flag("CanWritePerforce"));parameterButton("Revert unchanged","RevertSelectedPerforceFileAsync",{{"unchangedOnly",true}});ImGui::SameLine();parameterButton("Revert selected...","RevertSelectedPerforceFileAsync",{{"unchangedOnly",false}},true);}
                list("PerforceOpenedFiles");ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Changelists")) {
                fields({{"PerforceSubmitDefaultChangelist","Submit default changelist"},{"PerforceSubmitDescription","Submit description",true}});
                {cyui::ScopedDisabled disabled(!flag("CanSubmitPerforce"));command("SubmitPerforceAsync","Submit...",true);}
                if(toolColumns("##perforceChanges")) {cyui::heading("Pending",3);list("PerforcePendingChangelists");ImGui::TableNextColumn();cyui::heading("Submitted",3);list("PerforceSubmittedChangelists");ImGui::EndTable();}ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Reconcile / sync")) {
                commands({{"PreviewPerforceReconcileAsync","Reconcile preview"},{"PreviewPerforceSyncAsync","Sync preview"}});
                {cyui::ScopedDisabled disabled(!flag("CanWritePerforce"));commands({{"ApplyPerforceReconcileAsync","Apply reconcile..."},{"ApplyPerforceSyncAsync","Sync workspace..."}});}
                field("PerforcePath","Project-relative file to open for edit");{cyui::ScopedDisabled disabled(!flag("CanWritePerforce"));command("OpenPerforcePathForEditAsync","Open for edit");}
                textPanel("PerforceCommandOutput","##p4Output");ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("File history")) {list("PerforceFileHistory");ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="cystore") {
        cyui::callout(cyui::Tone::info,"CyStore Alpha is a separate segmented cache. Git commits, branches, remotes and standard LFS pointers remain authoritative and are never rewritten by enabling this plugin.");
        summary("CyStoreStatusSummary");summary("CyStoreStorePath");
        command("InitializeCyStoreAsync","Initialize segmented store",true);commands({{"RefreshCyStoreAsync","Refresh"},{"CaptureCyStoreLfsAsync","Capture hydrated Git LFS"},{"CancelCyStoreOperation","Cancel"}});
        summary("CyStoreProgressText");if(flag("IsCyStoreBusy")) cyui::progressBar(-1.0f,cyui::dp(-1,6),"Processing chunks",cyui::Tone::accent);
        if(toolColumns("##cystore",0.65f)) {
            scrollBegin("##cystoreVersions");commands({{"VerifySelectedCyStoreVersionAsync","Verify selected"},{"ReconstructSelectedCyStoreVersionAsync","Reconstruct safely..."}});list("CyStoreVersions");ImGui::EndChild();
            ImGui::TableNextColumn();scrollBegin("##cystoreStatistics");cyui::sectionTitle("Storage and reconstruction");summary("CyStoreLogicalSize");summary("CyStoreStoredSize");summary("CyStoreDeduplicationSummary");summary("CyStoreLastResult");summary("CyStoreWarnings");ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    if(workspace_=="unrealbuilds") {
        commands({{"DiscoverUnrealBuildEnvironmentAsync","Discover engines / targets"},{"RunSelectedUnrealBuildAsync","Build selected"},{"CancelUnrealBuild","Cancel"}});summary("UnrealBuildStatus");
        if(ImGui::BeginTabBar("##buildLabTabs")) {
            if(ImGui::BeginTabItem("Build setup")) {
                scrollBegin("##unrealBuildSetup");
                selector("UnrealBuildTargets","SelectedUnrealBuildTarget","Target");selector("UnrealBuildEngines","SelectedUnrealBuildEngine","Engine version");
                selector("UnrealBuildPlatforms","SelectedUnrealBuildPlatform","Platform");selector("UnrealBuildConfigurations","SelectedUnrealBuildConfiguration","Configuration");
                fields({{"UnrealLinuxToolchainPath","Linux cross-compiler root"},{"UnrealAndroidSdkPath","Android SDK"},{"UnrealAndroidNdkPath","Android NDK override"},{"UnrealJavaHomePath","Java home override"},{"UnrealBuildOutputPath","Output directory"},{"UnrealBuildAutoConfigureToolchains","Auto-configure toolchains"},{"UnrealBuildCookAndPackage","Cook and package"},{"UnrealBuildTimeoutMinutes","Timeout (minutes)"},{"UnrealBuildMaximumParallel","Maximum parallel builds"}});
                summary("SelectedUnrealBuildEngine.ToolchainSummary");cyui::sectionTitle("Version range");selector("UnrealBuildEngines","UnrealBuildRangeFrom","From engine");selector("UnrealBuildEngines","UnrealBuildRangeTo","To engine");command("RunUnrealBuildRangeAsync","Build version range",true);
                cyui::sectionTitle("Presets");selector("UnrealBuildPresets","SelectedUnrealBuildPreset","Preset");field("UnrealBuildPresetName","Preset name");commands({{"ApplySelectedUnrealBuildPreset","Apply preset"},{"SaveUnrealBuildPresetAsync","Save preset"},{"DeleteSelectedUnrealBuildPresetAsync","Delete preset..."}});ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Results")) {list("UnrealBuildResults");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("Errors and warnings")) {list("UnrealBuildDiagnostics");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("Full log")) {list("UnrealBuildLogLines");ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }return true;
    }
    if(workspace_=="diagnostics") {
        summary("PerformanceSummary");commands({{"RefreshPerformanceDiagnostics","Refresh metrics"},{"ClearPerformanceDiagnostics","Clear samples"}});list("PerformanceMetrics");return true;
    }
    if(workspace_=="help") {
        if(toolColumns("##help",0.3f)) {
            scrollBegin("##documentationIndex");cyui::heading("Offline documentation",3);field("DocumentationSearch","Search documentation");list("DocumentationTopics");ImGui::EndChild();
            ImGui::TableNextColumn();scrollBegin("##documentationTopic");
            if(cyui::beginSection("CyRevision updates",false)) {
                summary("CurrentApplicationVersion");summary("UpdateStatus");command("CheckForUpdatesAsync","Check for updates");ImGui::SameLine();command("DownloadAvailableUpdateAsync","Download available update");
                summary("UpdateReleaseNotes");cyui::endSection();
            }
            summary("SelectedDocumentationTopic.Category");summary("SelectedDocumentationTopic.Title");summary("SelectedDocumentationTopic.Summary");textPanel("SelectedDocumentationTopic.Body","##topicBody");ImGui::EndChild();ImGui::EndTable();
        }return true;
    }
    return false;
}

void Workspace::fileHistoryWindow() {
    cyui::textDim("Git / LFS pointer history — browsing does not download historical asset payloads.");
    auto path=text(state_,"fileHistoryPath",value("CodePreviewPath"));
    if(!path.empty()) {
        cyui::textDim("%s",path.c_str());
        cyui::ScopedDisabled disabled(busy());
        if(cyui::button("Refresh file history")){pendingAction_=true;send({{"command","fileHistory"},{"path",path},{"forceRefresh",true}});lastPoll_=0;}
    }
    if(toolColumns("##fileHistory",0.65f)) {
        list("NativeFileHistory");ImGui::TableNextColumn();ImGui::BeginChild("##revisionDetail");
        cyui::heading("Selected revision",3);
        auto selected=selectedRows("NativeFileHistory");
        if(selected.empty())cyui::emptyState("Select a revision","The author, commit and Git LFS metadata will appear here.",cyui::icon::document);
        else {
            auto values=selected.front().value("values",Json::object());
            auto hash=text(values,"CommitHash");
            {cyui::ScopedDisabled disabled(busy() || hash.empty());if(cyui::button("Open complete commit")) {
                submitAction(action("SelectExplorerCommitAsync"),{{"commitHash",hash}},false);navigate("history");
            }}
            if(cyui::beginProperties("##revisionProperties",95.0f)) {
                for(const auto& key:{"CommitHash","Author","AuthorEmail","Date","Change","Path","Storage","LfsOid","Size","AddedLines","DeletedLines"}) {
                    cyui::property(key);ImGui::TextWrapped("%s",display(values.value(key,Json())).c_str());
                }cyui::endProperties();
            }
            cyui::sectionTitle("Commit message");ImGui::TextWrapped("%s",text(values,"Message").c_str());
        }
        ImGui::EndChild();ImGui::EndTable();
    }
}
}
