#include "Workspace.h"
#include "StringInput.h"
#include <algorithm>
#include <map>

namespace cyrevision {
void Workspace::gitConflictEditor() {
    if(!flag("GitConflictEditorOpen")) return;
    struct Editor {std::string key,server,result,retention="30";int skip=0;};
    static std::map<const Workspace*,Editor> editors;
    auto& editor=editors[this];
    std::string key=value("GitConflictEditorSelectedKey"),path=value("GitConflictEditorPath"),server=value("GitConflictEditorResult");
    if(editor.key!=key){editor.key=key;editor.server=server;editor.result=server;}
    Json context={{"key",key},{"path",path}};
    auto operation=[&](const char* name){Json request=context;request["command"]="gitConflict";request["operation"]=name;send(request);lastPoll_=0;};
    auto confirmed=[&](const char* name,const char* title){
        Json input=context;input["operation"]=name;
        invoke({{"name",std::string("GitConflict-")+name},{"label",title},{"rpc","gitConflict"},{"context",input},{"confirmation",true},{"parameters",Json::array()}});
    };
    ImGui::SetNextWindowSize(cyui::dp(1320,840),ImGuiCond_FirstUseEver);
    bool open=true;
    if(ImGui::Begin((value("GitConflictEditorTitle")+"###native-git-conflicts").c_str(),busy() || flag("GitConflictEditorBusy")?nullptr:&open)) {
        cyui::textDim("%s",value("GitConflictEditorSummary").c_str());
        cyui::textFaint("%s",value("GitConflictEditorSelectedSummary").c_str());
        {
            cyui::ScopedDisabled disabled(busy() || flag("GitConflictEditorBusy"));
            if(cyui::button("Refresh conflicts")) operation("refresh");
            ImGui::SameLine();{
                cyui::ScopedDisabled unavailable(!flag("GitConflictEditorCanContinue"));
                if(cyui::primaryButton("Continue Git operation...")) confirmed("continue","Continue the Git operation using the resolved, staged files");
            }
            ImGui::SameLine();{
                cyui::ScopedDisabled unavailable(!flag("GitConflictEditorCanAbort"));
                if(cyui::dangerButton("Abort Git operation...")) confirmed("abort","Abort the active Git operation and restore its previous state");
            }
        }
        if(flag("GitConflictEditorBusy")) cyui::progressBar(-1.0f,ImVec2(-1,cyui::dp(6)),"Git conflict operation in progress");
        requestTable("NativeGitConflicts",200,editor.skip);
        static const Json empty=Json::object();
        const auto& table=state_.contains("tables") && state_["tables"].contains("NativeGitConflicts") && state_["tables"]["NativeGitConflicts"].is_object()?state_["tables"]["NativeGitConflicts"]:empty;
        const float upper=std::max(cyui::dp(180),ImGui::GetContentRegionAvail().y*0.40f);
        if(ImGui::BeginTable("##conflict-file-and-stages",2,ImGuiTableFlags_Resizable|ImGuiTableFlags_BordersInnerV|ImGuiTableFlags_ScrollY,ImVec2(0,upper))) {
            ImGui::TableSetupColumn("Conflicted files",ImGuiTableColumnFlags_WidthStretch,0.28f);
            ImGui::TableSetupColumn("Original versions",ImGuiTableColumnFlags_WidthStretch,0.72f);
            ImGui::TableNextRow();ImGui::TableNextColumn();
            cyui::heading("Conflicted files",3);
            {cyui::ScopedDisabled disabled(editor.skip==0);if(cyui::ghostButton("Previous")){editor.skip=std::max(0,editor.skip-200);lastPoll_=0;}}
            ImGui::SameLine();{cyui::ScopedDisabled disabled(editor.skip+200>=table.value("total",0));if(cyui::ghostButton("Next")){editor.skip+=200;lastPoll_=0;}}
            ImGui::BeginChild("##conflict-file-list",ImVec2(0,0),ImGuiChildFlags_Borders,ImGuiWindowFlags_HorizontalScrollbar);
            if(table.contains("rows")) for(const auto& row:table["rows"]) {
                const auto rowKey=text(row,"key");const auto filePath=text(row["values"],"Path");
                cyui::ScopedDisabled disabled(busy() || flag("GitConflictEditorBusy"));
                if(ImGui::Selectable((filePath+"###"+rowKey).c_str(),rowKey==key)) send({{"command","gitConflict"},{"operation","select"},{"key",rowKey}});
                cyui::tooltip(filePath.c_str());
            }
            if(table.value("total",0)==0) cyui::textFaint("No unresolved files.");
            ImGui::EndChild();
            ImGui::TableNextColumn();
            if(ImGui::BeginTabBar("##original-conflict-stages")) {
                if(ImGui::BeginTabItem("Common base")){textPanel("GitConflictEditorBase","git-conflict-base");ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("Our branch")){textPanel("GitConflictEditorOurs","git-conflict-ours");ImGui::EndTabItem();}
                if(ImGui::BeginTabItem("Incoming branch")){textPanel("GitConflictEditorTheirs","git-conflict-theirs");ImGui::EndTabItem();}
                ImGui::EndTabBar();
            }
            ImGui::EndTable();
        }
        {
            cyui::ScopedDisabled disabled(busy() || flag("GitConflictEditorBusy") || key.empty());
            if(cyui::button("Use complete Base version")) operation("base");ImGui::SameLine();
            if(cyui::button("Use complete Our version")) operation("ours");ImGui::SameLine();
            if(cyui::button("Use complete Incoming version")) operation("theirs");
            cyui::tooltip("Changes only the proposed Result. No working file is changed until you explicitly mark it resolved.");
            if(cyui::ghostButton("Previous marker block")) operation("previousBlock");ImGui::SameLine();
            if(cyui::ghostButton("Next marker block")) operation("nextBlock");ImGui::SameLine();
            if(cyui::button("Accept Our block")) operation("blockOurs");ImGui::SameLine();
            if(cyui::button("Accept Incoming block")) operation("blockIncoming");ImGui::SameLine();
            cyui::textFaint("%s",value("GitConflictEditorBlocks").c_str());
        }
        if(ImGui::BeginTabBar("##conflict-result-tabs")) {
            if(ImGui::BeginTabItem("Result to write and stage")) {
                cyui::textDim("%s · %s",path.c_str(),value("GitConflictEditorSource").c_str());
                if(flag("GitConflictEditorLarge")) cyui::callout(cyui::Tone::warning,"This file is too large for the embedded editor. Choose a complete Base / Our / Incoming version or use an external merge editor. No truncated text will be written.");
                {
                    cyui::ScopedDisabled disabled(busy() || flag("GitConflictEditorBusy") || flag("GitConflictEditorReadOnly"));
                    const float height=std::max(cyui::dp(110),ImGui::GetContentRegionAvail().y-cyui::dp(145));
                    if(flag("GitConflictEditorReadOnly")) {
                        ImGui::BeginChild("##read-only-conflict-result",ImVec2(0,height));textPanel("GitConflictEditorResult","git-conflict-readonly-result");ImGui::EndChild();
                    } else {
                    ImGui::PushFont(cyui::monoFont());
                    ImGui::InputTextMultiline("##conflict-result",&editor.result,ImVec2(-1,height),ImGuiInputTextFlags_AllowTabInput);
                    bool editing=ImGui::IsItemActive();bool changed=ImGui::IsItemDeactivatedAfterEdit();
                    ImGui::PopFont();
                    if(changed){Json request=context;request["command"]="gitConflict";request["operation"]="setResult";request["result"]=editor.result;send(request);editor.server=editor.result;lastPoll_=0;}
                    else if(!editing && editor.server!=server){editor.server=server;editor.result=server;}
                    }
                }
                cyui::textFaint("%s",value("GitConflictEditorResolutionHint").c_str());
                bool backup=flag("GitConflictEditorBackup",true);
                {
                    cyui::ScopedDisabled disabled(busy() || flag("GitConflictEditorBusy"));
                    if(ImGui::Checkbox("Create a safety backup before writing / staging",&backup)) send({{"command","gitConflict"},{"operation","backup"},{"enabled",backup},{"retention",value("GitConflictEditorRetention","30")}});
                    ImGui::SameLine();ImGui::SetNextItemWidth(cyui::dp(62));
                    if(editor.retention.empty()) editor.retention=value("GitConflictEditorRetention","30");
                    ImGui::InputText("Retention days",&editor.retention);
                    if(ImGui::IsItemDeactivatedAfterEdit()) send({{"command","gitConflict"},{"operation","backup"},{"enabled",backup},{"retention",editor.retention}});
                    ImGui::SameLine();{
                        cyui::ScopedDisabled unavailable(!flag("GitConflictEditorCanResolve"));
                        if(cyui::dangerButton("Write and mark selected file resolved...")) confirmed("resolve","Write the reviewed Result and stage this resolved file");
                    }
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("AI assistance (review first)")) {
                {cyui::ScopedDisabled disabled(busy() || !flag("GitConflictEditorCanUseAi"));
                    if(cyui::button("Explain this conflict")) operation("aiExplain");ImGui::SameLine();
                    if(cyui::button("Propose a resolution")) operation("aiPropose");
                }
                ImGui::SameLine();{cyui::ScopedDisabled disabled(busy() || !flag("GitConflictEditorCanPreviewAi"));if(cyui::button("Preview AI proposal in Result")) operation("aiPreview");}
                cyui::callout(cyui::Tone::info,"AI output is never written automatically. Inspect the Result and explicitly confirm writing and staging it.");
                textPanel("GitConflictEditorAiAdvice","git-conflict-ai");ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
        cyui::textDim("%s",value("GitConflictEditorStatus").c_str());
    }
    ImGui::End();
    if(!open && !busy() && !flag("GitConflictEditorBusy")) operation("close");
}
}
