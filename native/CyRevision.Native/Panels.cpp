#include "Workspace.h"
#include <map>
#include <algorithm>
namespace cyrevision {
void Workspace::dataTable(){list(table_.c_str());}
void Workspace::workspacePanel(){
    ImGui::SetNextWindowDockID(mainDock_,ImGuiCond_FirstUseEver);
    if(ImGui::Begin("Workspace")){
        cyui::heading(value("ChangeSummaryBase",text(state_,"project")).c_str(),2);ImGui::SameLine();cyui::textDim("%s %s",value("GitAheadSummary").c_str(),value("GitBehindSummary").c_str());
        cyui::tooltip(value("ToolStatus").c_str());
        if(!error_.empty()){cyui::callout(cyui::Tone::error,error_.c_str());if(cyui::ghostButton("Dismiss"))error_.clear();}
        if(!ready())activityStatus("Starting engine…",true);
        else{
            Json tabs=state_.contains("navigation")?state_["navigation"].value("tabs",Json::array()):catalog_.value("workspaces",Json::array());
            auto rememberedTabs=state_.contains("navigation")?state_["navigation"].value("categoryTabs",Json::object()):Json::object();
            std::vector<std::string> categories;for(auto& tab:tabs){auto category=text(tab,"category");if(tab.value("visible",true) && tab.value("available",true) && std::find(categories.begin(),categories.end(),category)==categories.end())categories.push_back(category);}
            if(ImGui::BeginTabBar("##categories")){
                const auto requestedCategory=category_;const bool forceCategory=selectTable_;
                for(auto& category:categories){bool active=category==requestedCategory;cyui::ScopedColor tabText(ImGuiCol_Text,active?cyui::palette().accentText:cyui::palette().text);if(ImGui::BeginTabItem(category.c_str(),nullptr,forceCategory && active?ImGuiTabItemFlags_SetSelected:0)){
                    if((!forceCategory || active) && category_!=category){category_=category;std::string target;for(auto& tab:tabs)if(text(tab,"category")==category && tab.value("visible",true) && tab.value("available",true)){if(target.empty())target=text(tab,"id");if(text(tab,"id")==workspace_){target=workspace_;break;}for(auto& remembered:rememberedTabs.items())if(display(remembered.value())==text(tab,"loadTab"))target=text(tab,"id");}if(!target.empty())navigate(target);}ImGui::EndTabItem();
                }}ImGui::EndTabBar();
            }
            if(ImGui::BeginTabBar("##projectTabs",ImGuiTabBarFlags_FittingPolicyScroll)){
                const auto requestedWorkspace=workspace_;const bool forceWorkspace=selectTable_;
                for(auto& tab:tabs){if(text(tab,"category")!=category_ || !tab.value("visible",true) || !tab.value("available",true))continue;auto id=text(tab,"id");
                    cyui::ScopedColor tabText(ImGuiCol_Text,id==requestedWorkspace?cyui::palette().accentText:cyui::palette().text);
                    if(ImGui::BeginTabItem(text(tab,"label").c_str(),nullptr,forceWorkspace && id==requestedWorkspace?ImGuiTabItemFlags_SetSelected:0)){if((!forceWorkspace || id==requestedWorkspace) && id!=workspace_)navigate(id);ImGui::EndTabItem();}tabDetachMenu(id);
                }ImGui::EndTabBar();
            }
            selectTable_=false;cyui::space(3);workspaceToolbar();cyui::space(4);ImGui::BeginChild("##workspaceBody");
            workspaceBody();
            ImGui::EndChild();
        }
    }ImGui::End();
}
void Workspace::workspaceToolbar(){
    if(workspace_=="help")return;
    if(cyui::beginToolbar("##project-actions")){
        command("RefreshAsync","Refresh");ImGui::SameLine();command("FetchAsync","Fetch");ImGui::SameLine();command("PullAsync","Pull");ImGui::SameLine();command("PushAsync","Push");ImGui::SameLine();
        auto activity=loadingLabel("");bool active=!activity.empty() || busy();float endWidth=cyui::buttonWidth("Detach")+ImGui::GetFrameHeight()+3*ImGui::GetStyle().ItemSpacing.x;
        activityStatus(activity.empty()?(active?"Operation in progress…":"Ready"):activity,active,std::max(cyui::dp(20),ImGui::GetContentRegionAvail().x-endWidth));
        ImGui::SameLine();{cyui::ScopedDisabled disabled(!active);if(cyui::iconButton(cyui::iconsAvailable()?cyui::icon::close:"X","Cancel the current operation"))send({{"command","cancel"}});}
        ImGui::SameLine();if(cyui::button("Detach"))openDetached(workspace_);cyui::tooltip("Open the active project workspace in a separate window.");
    }cyui::endToolbar();
}
void Workspace::operations(){
    if(!showOperations_)return;
    if(ImGui::Begin("Operations",&showOperations_)){
        // This window never gains focus or opens on its own when background state changes.
        if(state_.contains("operations") && !state_["operations"].empty()){
            if(ImGui::BeginTable("##operations",5,ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|ImGuiTableFlags_RowBg|ImGuiTableFlags_ScrollY)){
                for(auto label:{"State","Operation","Details","Duration","Project"})ImGui::TableSetupColumn(label);ImGui::TableHeadersRow();
                for(auto& operation:state_["operations"]){ImGui::TableNextRow();ImGui::TableNextColumn();cyui::textColoured(statusColour(text(operation,"state")),"%s",text(operation,"state").c_str());ImGui::TableNextColumn();ImGui::TextUnformatted(text(operation,"title").c_str());ImGui::TableNextColumn();ImGui::TextUnformatted(text(operation,"detail").c_str());ImGui::TableNextColumn();ImGui::TextUnformatted(text(operation,"durationText").c_str());ImGui::TableNextColumn();ImGui::TextUnformatted(text(operation,"projectName").c_str());}ImGui::EndTable();
            }
        }else cyui::textDim("No operation in progress.");
    }ImGui::End();
}
}
