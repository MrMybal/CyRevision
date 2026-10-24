#include "Workspace.h"
#include "ReviewViews.h"
#include "StringInput.h"

namespace cyrevision {
bool Workspace::pullRequestView() {
    if(workspace_!="pullrequests") return false;
    auto& ui=reviewUi(projectId_);
    const auto& context=reviewContext(state_);
    const auto& selected=reviewObject(context,"selected");
    Json selection={{"number",selected.value("number",0)}};
    cyui::panelHeader("Pull requests",value("PullRequestRepositoryName").c_str());
    ImGui::TextWrapped("%s",value("PullRequestStatus").c_str());
    field("PullRequestStateFilter","State");
    command("ResolvePullRequestRepositoryAsync","Detect remote"); ImGui::SameLine();
    command("RefreshPullRequestsAsync","Refresh",true);
    if(flag("IsPullRequestLoading")) { ImGui::SameLine();cyui::spinner("##prLoading");cyui::textDim("Loading pull requests..."); }
    if(cyui::beginSection("Connection and authentication",false)) {
        field("PullRequestApiBaseUrl","API / GitHub Enterprise URL");
        field("PullRequestTokenEnvironmentVariable","Token environment variable");
        field("PullRequestToken","Session token");
        cyui::textDim("%s",value("PullRequestAuthenticationState").c_str());
        cyui::endSection();
    }
    cyui::divider();
    ImVec2 available=ImGui::GetContentRegionAvail();
    float left=reviewSplitWidth("##prListSplitter",ui.prSplit,available.x,230);
    ImGui::BeginChild("##prRequestList",ImVec2(left,0),ImGuiChildFlags_Borders);
    if(ImGui::BeginTabBar("##requestTabs")) {
        if(ImGui::BeginTabItem("Requests")) {
            field("PullRequestSearch","Search number, title, author, branch or state");
            list("FilteredPullRequests");
            ImGui::EndTabItem();
        }
        if(ImGui::BeginTabItem("Create")) {
            cyui::textDim("Push the source branch before creating a pull request.");
            field("NewPullRequestTitle","Title");
            field("NewPullRequestHeadBranch","Source branch");
            field("NewPullRequestBaseBranch","Target branch");
            field("NewPullRequestBody","Description (Markdown)",true);
            field("NewPullRequestIsDraft","Create as draft");
            {
                cyui::ScopedDisabled disabled(busy() || !flag("HasWorkItemPlugins"));
                if(cyui::button("Task links...")) ui.taskPicker=true;
                cyui::tooltip("Find Jira, ClickUp or CyTask tasks using the plugins enabled for this project.");
            }
            ImGui::SameLine();
            {
                cyui::ScopedDisabled disabled(!flag("CanGenerateAiPullRequestDraft"));
                command("GenerateAiPullRequestDraftAsync","AI draft");
            }
            command("CreatePullRequestAsync","Create pull request",true);
            ImGui::EndTabItem();
        }
        ImGui::EndTabBar();
    }
    ImGui::EndChild(); ImGui::SameLine(0,cyui::dp(7));
    ImGui::BeginChild("##prSelected",ImVec2(0,0),ImGuiChildFlags_Borders);
    if(selected.empty()) {
        cyui::emptyState("Select a pull request","Choose a request to inspect its description, changes, conversation, CI and conflicts.",cyui::icon::code);
    } else {
        std::string heading="#"+std::to_string(selected.value("number",0))+"  "+text(selected,"title");
        cyui::heading(heading.c_str(),2);
        reviewStatus(text(selected,"stateText"));ImGui::SameLine();reviewStatus(text(selected,"ciStateText","NO CI"));
        if(selected.value("isOwnedByCurrentUser",false)) { ImGui::SameLine();reviewStatus("MINE"); }
        ImGui::SameLine();cyui::textDim("%s",text(selected,"branchText").c_str());
        cyui::textDim("%s",value("PullRequestDetailsSummary").c_str());
        if(context.value("needsTaskConfirmation",false)) {
            cyui::callout(cyui::Tone::success,"The pull request was merged. Update its detected tasks to their completion status?");
            if(cyui::button("Complete linked tasks...")) invoke(reviewRpc("Complete linked tasks","prCompleteTasks",selection,
                "Move each resolved linked task to its provider completion status? Unresolved references are skipped."));
            ImGui::SameLine();if(cyui::ghostButton("Not now")) {Json request=selection;request["command"]="prDismissTaskPrompt";send(request);}
        }
        if(ImGui::BeginTabBar("##prDetails")) {
            if(ImGui::BeginTabItem("Description")) {
                if(cyui::beginProperties("##prMetadata",95.0f)) {
                    for(auto name:{"author","createdAt","updatedAt","mergeabilityText"}) {
                        cyui::property(name);ImGui::TextUnformatted(display(selected.value(name,Json())).c_str());
                    }
                    cyui::endProperties();
                }
                textPanel("PullRequestBody","##prBody");ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Files")) {
                field("IsPullRequestDiffPreviewEnabled","Show diff preview");
                if(cyui::ghostButton("Open diff large")) ui.prPatchWindow=true;
                ImVec2 region=ImGui::GetContentRegionAvail();
                float files=reviewSplitWidth("##prFilesSplitter",ui.fileSplit,region.x,160);
                ImGui::BeginChild("##prFiles",ImVec2(files,0));list("PullRequestFiles");ImGui::EndChild();
                ImGui::SameLine(0,cyui::dp(7));ImGui::BeginChild("##prPatch",ImVec2(0,0));
                if(flag("IsPullRequestDiffPreviewEnabled")) diffPanel("PullRequestPatch","##prProviderPatch");
                else cyui::emptyState("Diff hidden","Enable the diff preview above to inspect the selected file.",cyui::icon::document);
                ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Conversation")) {
                float height=std::max(cyui::dp(120),ImGui::GetContentRegionAvail().y-cyui::dp(132));
                ImGui::BeginChild("##conversation",ImVec2(0,height));
                if(ImGui::BeginTabBar("##conversationKinds")) {
                    if(ImGui::BeginTabItem("Comments")){list("PullRequestComments");ImGui::EndTabItem();}
                    if(ImGui::BeginTabItem("Reviews")){list("PullRequestReviews");ImGui::EndTabItem();}
                    ImGui::EndTabBar();
                }
                ImGui::EndChild();
                field("PullRequestComment","Comment",true);
                cyui::ScopedDisabled disabled(busy() || !flag("HasSelectedPullRequest"));
                if(cyui::button("Publish comment...")) invoke(reviewRpc("Publish comment","prComment",selection,"Publish the entered comment on the remote pull request?"));
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("CI")) {
                cyui::textDim("%s",value("PullRequestCiStatus").c_str());
                selector("PullRequestCiWorkflows","SelectedPullRequestCiWorkflow","Workflow");
                {
                    cyui::ScopedDisabled disabled(!flag("CanDispatchPullRequestCiWorkflow"));
                    command("DispatchSelectedPullRequestCiWorkflowAsync","Run workflow");
                }
                ImGui::SameLine();command("RefreshSelectedPullRequestCiAsync","Refresh CI");
                if(flag("IsPullRequestCiLoading")) {ImGui::SameLine();cyui::spinner("##prCiLoading");}
                ImVec2 region=ImGui::GetContentRegionAvail();
                float runs=reviewSplitWidth("##prCiSplit",ui.ciRunsSplit,region.x,170);
                ImGui::BeginChild("##prCiRuns",ImVec2(runs,0),ImGuiChildFlags_Borders);
                cyui::sectionTitle("Runs for this pull request");
                const auto& run=reviewObject(context,"prCiRun");
                Json runContext=selection;runContext["runId"]=run.value("id",0LL);
                {
                    cyui::ScopedDisabled disabled(busy() || run.empty());
                    if(cyui::ghostButton("Open run")){auto request=runContext;request["command"]="openPrCiRun";send(request);}
                }
                {
                    cyui::ScopedDisabled disabled(busy() || !flag("CanRerunSelectedPullRequestCiRun"));
                    ImGui::SameLine();if(cyui::ghostButton("Rerun failed..."))invoke(reviewRpc("Rerun failed jobs","prCiRerun",runContext,"Queue the failed jobs of this workflow run again?"));
                }
                list("PullRequestCiRuns");ImGui::EndChild();
                ImGui::SameLine(0,cyui::dp(7));ImGui::BeginChild("##prCiDetails",ImVec2(0,0),ImGuiChildFlags_Borders);
                if(ImGui::BeginTabBar("##prCiKinds")) {
                    if(ImGui::BeginTabItem("Jobs")) {
                        cyui::textFaint("Right-click a job to open or cancel its complete workflow run.");
                        list("PullRequestCiJobs");ImGui::EndTabItem();
                    }
                    if(ImGui::BeginTabItem("Full log")){ciLogPanel(true);ImGui::EndTabItem();}
                    ImGui::EndTabBar();
                }
                ImGui::EndChild();ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Conflicts")) {
                cyui::callout(selected.value("hasMergeConflicts",false)?cyui::Tone::warning:cyui::Tone::info,value("PullRequestConflictSummary").c_str());
                list("PullRequestConflictFiles");ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Actions")) {
                ImGui::BeginChild("##prActions",ImVec2(0,0));
                if(cyui::beginSection("Review",true)) {
                    cyui::textDim("%s",value("PullRequestReviewAvailability").c_str());
                    field("PullRequestReviewAction","Review action");field("PullRequestReviewBody","Review body",true);
                    {cyui::ScopedDisabled disabled(busy() || !flag("CanReviewSelectedPullRequest"));
                    if(cyui::button("Submit review...")) invoke(reviewRpc("Submit review","prReview",selection,"Submit the selected review action and review text? The provider validates your account permissions."));}
                    cyui::endSection();
                }
                if(cyui::beginSection("Linked tasks",true)) {
                    field("PullRequestTaskUpdateMode","After merge");
                    cyui::textDim("%s",value("PullRequestTaskUpdateModeDescription").c_str());
                    cyui::textDim("%s",value("PullRequestLinkedTaskStatus").c_str());
                    list("PullRequestLinkedWorkItems",ImVec2(0,cyui::dp(160)));
                    {cyui::ScopedDisabled disabled(busy() || !selected.value("isMerged",false) || !flag("HasPullRequestLinkedWorkItems"));
                    if(cyui::button("Complete linked tasks..."))invoke(reviewRpc("Complete linked tasks","prCompleteTasks",selection,"Move each resolved task to its configured provider completion status?"));}
                    cyui::endSection();
                }
                if(cyui::beginSection("Pull request and branch",true)) {
                    command("OpenSelectedPullRequestInBrowser","Open pull request");
                    cyui::sectionTitle("Associated commits");
                    list("PullRequestCommitRevisions",ImVec2(0,cyui::dp(145)));
                    {cyui::ScopedDisabled disabled(busy());
                    if(cyui::button("Checkout pull request..."))invoke(reviewRpc("Checkout pull request","prCheckout",selection,"Fetch and switch to a local pull-request branch? The working tree must be clean."));}
                    {cyui::ScopedDisabled disabled(busy() || selected.value("isMerged",false));
                    ImGui::SameLine();if(cyui::button("Close / reopen..."))invoke(reviewRpc("Change pull request state","prToggle",selection,"Change the state of this pull request on the remote repository?"));}
                    field("PullRequestMergeMethod","Merge method");
                    {cyui::ScopedDisabled disabled(busy() || !flag("CanMergeSelectedPullRequest") || selected.value("isDraft",false));
                    if(cyui::dangerButton("Merge pull request..."))invoke(reviewRpc("Merge pull request","prMerge",selection,"Merge this pull request into its target branch using the selected merge method? This changes the remote repository and can start CI or deployments."));}
                    cyui::textDim("%s",value("PullRequestLocalBranchStatus").c_str());
                    {cyui::ScopedDisabled disabled(busy() || !flag("CanRemovePullRequestLocalBranch"));
                    if(cyui::button("Analyze local branch removal")){auto request=selection;request["command"]="prAnalyzeBranchRemoval";send(request);}}
                    const auto& analysis=reviewObject(context,"branchAnalysis");
                    if(!analysis.empty()) {
                        cyui::callout(analysis.value("canRemoveSafely",false)?cyui::Tone::info:cyui::Tone::warning,text(analysis,"safetyMessage").c_str());
                        cyui::textDim("Unique commits: %d  -  Retained by: %s",analysis.value("uniqueCommitCount",0),text(analysis,"retainedBy").c_str());
                        Json removal=selection;removal["force"]=false;
                        {cyui::ScopedDisabled disabled(busy() || !analysis.value("canRemoveSafely",false));
                        if(cyui::dangerButton("Remove local branch..."))invoke(reviewRpc("Remove merged local branch","prRemoveBranch",removal,"Only the local branch reference will be removed. Remote branches and working files are unchanged."));}
                        if(!analysis.value("canRemoveSafely",false) && analysis.value("canForceRemove",false)) {
                            removal["force"]=true;
                            if(cyui::dangerButton("Force-remove local branch..."))invoke(reviewRpc("Force-remove merged local branch","prRemoveBranch",removal,"No verified Git reference retains every unique commit. Force removal can make unpublished commits difficult to recover. Remote branches and working files are unchanged."));
                        }
                    }
                    cyui::endSection();
                }
                ImGui::EndChild();ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
    }
    ImGui::EndChild();
    workItemPicker();
    return true;
}
}
