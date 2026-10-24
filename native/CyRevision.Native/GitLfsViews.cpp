#include "Workspace.h"
#include <map>
#include <algorithm>

namespace cyrevision {
namespace {
struct LfsFolderView {int skip=0;std::string selected,project;};
std::map<std::pair<const Workspace*,std::string>,LfsFolderView> folderViews;
}
void Workspace::gitLfsFolders() {
    auto& view=folderViews[{this,activePane_}];if(view.project!=projectId_){view=LfsFolderView{};view.project=projectId_;}
    requestTable("NativeLfsFolders",200,view.skip);
    const auto& table=viewTable("NativeLfsFolders");
    int total=table.value("total",0);
    {cyui::ScopedDisabled disabled(view.skip==0);if(cyui::ghostButton("Previous folders")){view.skip=std::max(0,view.skip-200);lastPoll_=0;}}
    ImGui::SameLine();{cyui::ScopedDisabled disabled(view.skip+200>=total);if(cyui::ghostButton("Next folders")){view.skip+=200;lastPoll_=0;}}
    if(!table.contains("rows")){cyui::textDim("Loading LFS folders…");return;}
    const auto& rows=table["rows"];
    if(ImGui::BeginTable("##lfs-folders",4,ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|ImGuiTableFlags_RowBg|ImGuiTableFlags_ScrollY|ImGuiTableFlags_ScrollX)) {
        ImGui::TableSetupColumn("File / folder",ImGuiTableColumnFlags_WidthFixed,cyui::dp(330));
        ImGui::TableSetupColumn("Storage",ImGuiTableColumnFlags_WidthFixed,cyui::dp(150));
        ImGui::TableSetupColumn("Size",ImGuiTableColumnFlags_WidthFixed,cyui::dp(100));
        ImGui::TableSetupColumn("Path",ImGuiTableColumnFlags_WidthFixed,cyui::dp(500));
        ImGui::TableSetupScrollFreeze(1,1);ImGui::TableHeadersRow();
        ImGuiListClipper clipper;clipper.Begin(static_cast<int>(rows.size()));
        while(clipper.Step())for(int index=clipper.DisplayStart;index<clipper.DisplayEnd;++index){
            const auto& row=rows[index];const auto& values=row["values"];auto key=text(row,"key");cyui::ScopedID id(key.c_str());
            bool directory=values.value("IsDirectory",false),expanded=values.value("IsExpanded",false);
            ImGui::TableNextRow();ImGui::TableNextColumn();
            if(ImGui::Selectable("##file",view.selected==key,ImGuiSelectableFlags_SpanAllColumns|ImGuiSelectableFlags_AllowOverlap) && !directory && !busy()){
                view.selected=key;send({{"command","gitLfsTree"},{"operation","select"},{"keys",Json::array({key})}});lastPoll_=0;
            }
            ImGui::SameLine(0,cyui::dp(16)*values.value("Depth",0));
            if(directory){
                cyui::ScopedDisabled disabled(busy());
                if(cyui::iconButton(expanded?cyui::icon::chevronDown:cyui::icon::folder,expanded?"Collapse folder":"Expand folder",false,0,cyui::dp(22,22))){
                    send({{"command","gitLfsTree"},{"operation","expand"},{"keys",Json::array({key})},{"expanded",!expanded}});lastPoll_=0;
                }ImGui::SameLine();
            }
            ImGui::TextUnformatted(text(values,"Name").c_str());
            ImGui::TableNextColumn();ImGui::TextUnformatted(text(values,"Availability").c_str());
            ImGui::TableNextColumn();ImGui::TextUnformatted(text(values,"Size").c_str());
            ImGui::TableNextColumn();ImGui::TextUnformatted(text(values,"Path").c_str());
        }ImGui::EndTable();
    }
}
void releaseLfsViewState(const Workspace* workspace,const std::string& scope){folderViews.erase({workspace,scope});}
}
