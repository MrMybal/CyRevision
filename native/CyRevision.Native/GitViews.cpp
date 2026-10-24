#include "Workspace.h"
#include "StringInput.h"
#include <algorithm>
#include <cmath>
#include <map>
#include <set>
#include <sstream>
#include <ctime>

namespace cyrevision {
namespace {
struct GitUiState {
    std::string project;
    int changesMode=0, changesSkip=0, changeAnchor=-1, branchSkip=0, branchAnchor=-1;
    std::set<std::string> changes, branchFiles;
    std::map<std::string,std::string> branchPaths;
    std::string branchSearch;
    float graphZoom=1.0f;
    std::string graphSelected;
};
std::map<std::pair<const Workspace*,std::string>,GitUiState> gitStates;
const Json& tableData(const Json& state,const std::string& source) {
    static const Json empty=Json::object();
    if(state.contains("tables") && state["tables"].contains(source) && state["tables"][source].is_object()) return state["tables"][source];
    if(state.contains("table") && state["table"].is_object() && text(state["table"],"name")==source) return state["table"];
    return empty;
}
const Json& tableRows(const Json& state,const std::string& source) {
    static const Json empty=Json::array();
    const auto& table=tableData(state,source);
    return table.contains("rows") && table["rows"].is_array() ? table["rows"] : empty;
}
Json rowKeys(const Json& rows) {
    Json keys=Json::array();
    for(const auto& row:rows) keys.push_back(text(row,"key"));
    return keys;
}
Json keys(const std::set<std::string>& selected) { Json result=Json::array(); for(const auto& key:selected) result.push_back(key); return result; }
void pickRow(std::set<std::string>& selected,const Json& rows,int index,int& anchor) {
    const auto key=text(rows[index],"key");
    auto& io=ImGui::GetIO();
    if(!io.KeyCtrl && !io.KeyShift) selected.clear();
    if(io.KeyShift && anchor>=0) {
        for(int at=std::min(anchor,index);at<=std::max(anchor,index) && at<static_cast<int>(rows.size());++at)
            selected.insert(text(rows[at],"key"));
    } else if(io.KeyCtrl && selected.count(key)) selected.erase(key);
    else selected.insert(key);
    anchor=index;
}
void nextButton() { ImGui::SameLine(0,cyui::dp(6)); }
bool splitBegin(const char* id,float first=0.48f) {
    if(!ImGui::BeginTable(id,2,ImGuiTableFlags_Resizable|ImGuiTableFlags_BordersInnerV|ImGuiTableFlags_SizingStretchProp)) return false;
    ImGui::TableSetupColumn("Left",ImGuiTableColumnFlags_WidthStretch,first);
    ImGui::TableSetupColumn("Right",ImGuiTableColumnFlags_WidthStretch,1.0f-first);
    ImGui::TableNextRow(); ImGui::TableNextColumn();
    return true;
}
void labelValue(const char* label,const std::string& value) {
    cyui::property(label); ImGui::TextWrapped("%s",value.empty() ? "—" : value.c_str());
}
Json customAction(const char* name,const char* label,const char* rpc,Json context,bool confirm=false,Json parameters=Json::array()) {
    return {{"name",name},{"label",label},{"rpc",rpc},{"context",std::move(context)},{"confirmation",confirm},{"parameters",std::move(parameters)}};
}
Json parameter(const char* name,const char* label,const char* kind,Json initial=Json(),Json options=Json::array()) {
    return {{"name",name},{"label",label},{"kind",kind},{"optional",false},{"value",std::move(initial)},{"options",std::move(options)}};
}
const Json& fieldJson(const Json& state,const char* name) {
    static const Json empty=Json::array();
    if(state.contains("fields") && state["fields"].contains(name) && state["fields"][name].contains("value")) return state["fields"][name]["value"];
    return empty;
}
long calendarDay(const std::string& date) {
    if(date.size()<10) return 0;
    int year=0,month=0,day=0;
    if(sscanf_s(date.c_str(),"%d-%d-%d",&year,&month,&day)!=3 || month<1 || month>12 || day<1 || day>31) return 0;
    year-=month<=2;
    const int era=(year>=0?year:year-399)/400;
    const unsigned yoe=static_cast<unsigned>(year-era*400);
    const unsigned doy=(153*(month+(month>2?-3:9))+2)/5+day-1;
    const unsigned doe=yoe*365+yoe/4-yoe/100+doy;
    return era*146097+static_cast<int>(doe)-719468;
}
std::string dayLabel(long day) {
    std::time_t stamp=static_cast<std::time_t>(day)*86400;
    std::tm date{};gmtime_s(&date,&stamp);char output[16]{};
    std::strftime(output,sizeof(output),"%Y-%m-%d",&date);return output;
}
std::string relationGraph(const Json& relations,const char* id,float height) {
    if(!relations.is_array() || relations.empty()) return {};
    struct View {ImVec2 pan;float zoom=1;std::string selected;};static std::map<std::string,View> views;
    auto& view=views[id];cyui::ScopedID scope(id);
    if(cyui::ghostButton("−"))view.zoom=std::max(0.4f,view.zoom-0.1f);ImGui::SameLine();
    if(cyui::ghostButton("+"))view.zoom=std::min(2.5f,view.zoom+0.1f);ImGui::SameLine();
    if(cyui::ghostButton("Fit relations")){view.zoom=1;view.pan=ImVec2();}ImGui::SameLine();cyui::textFaint("Drag to pan · click a node to filter its file list");
    std::map<std::string,int> degree;
    for(const auto& relation:relations) {
        auto source=text(relation,"sourcePath"),target=text(relation,"targetPath");
        if(degree.size()<150 || degree.count(source))degree[source]+=relation.value("coChangeCount",1);
        if(degree.size()<150 || degree.count(target))degree[target]+=relation.value("coChangeCount",1);
    }
    ImVec2 size=ImVec2(std::max(cyui::dp(100),ImGui::GetContentRegionAvail().x),height),origin=ImGui::GetCursorScreenPos();
    ImGui::InvisibleButton("##relation-canvas",size,ImGuiButtonFlags_MouseButtonLeft);
    bool hovered=ImGui::IsItemHovered();if(ImGui::IsItemActive() && ImGui::IsMouseDragging(0)){view.pan.x+=ImGui::GetIO().MouseDelta.x;view.pan.y+=ImGui::GetIO().MouseDelta.y;}
    auto* draw=ImGui::GetWindowDrawList();const auto& palette=cyui::palette();
    draw->AddRectFilled(origin,ImVec2(origin.x+size.x,origin.y+size.y),palette.bg0);
    draw->PushClipRect(origin,ImVec2(origin.x+size.x,origin.y+size.y),true);
    std::map<std::string,ImVec2> positions;std::map<std::string,int> series;int at=0;
    const float radius=std::max(cyui::dp(35),std::min(size.x,size.y)*0.36f)*view.zoom;
    for(const auto& node:degree) {
        float angle=static_cast<float>(at)*6.28318530718f/static_cast<float>(degree.size());
        positions[node.first]=ImVec2(origin.x+size.x*0.5f+view.pan.x+std::cos(angle)*radius,origin.y+size.y*0.5f+view.pan.y+std::sin(angle)*radius);
        series[node.first]=at++;
    }
    for(const auto& relation:relations) {
        auto source=text(relation,"sourcePath"),target=text(relation,"targetPath");
        if(!positions.count(source) || !positions.count(target))continue;
        bool selected=view.selected==source || view.selected==target;
        draw->AddLine(positions[source],positions[target],cyui::alpha(selected?palette.accent:palette.textFaint,selected?0.8f:0.2f),cyui::dp(std::min(4.0f,1.0f+std::sqrt(static_cast<float>(relation.value("coChangeCount",1)))*0.2f)));
    }
    std::string clicked;
    for(const auto& node:positions) {
        const float nodeRadius=cyui::dp(4)+cyui::dp(std::min(8.0f,std::sqrt(static_cast<float>(degree[node.first]))));
        draw->AddCircleFilled(node.second,nodeRadius,view.selected==node.first?palette.accent:cyui::categorical(series[node.first]));
        auto slash=node.first.find_last_of('/');auto label=slash==std::string::npos?node.first:node.first.substr(slash+1);
        if(label.size()>24)label=label.substr(0,21)+"…";
        draw->AddText(ImVec2(node.second.x+nodeRadius+cyui::dp(3),node.second.y-ImGui::GetFontSize()*0.5f),palette.textDim,label.c_str());
        const auto mouse=ImGui::GetIO().MousePos;float dx=mouse.x-node.second.x,dy=mouse.y-node.second.y;
        if(hovered && dx*dx+dy*dy<=(nodeRadius+cyui::dp(4))*(nodeRadius+cyui::dp(4))) {
            ImGui::BeginTooltip();ImGui::TextUnformatted(node.first.c_str());ImGui::Text("%d co-change links",degree[node.first]);ImGui::EndTooltip();
            if(ImGui::IsMouseReleased(0) && !ImGui::IsMouseDragging(0)) {view.selected=node.first;clicked=node.first;}
        }
    }
    draw->PopClipRect();return clicked;
}
}

void Workspace::gitGraphsPanel() {
    auto& ui=gitStates[{this,activePane_}];
    if(ui.project!=projectId_) {ui=GitUiState{};ui.project=projectId_;}
    cyui::textDim("%s",value("GitGraphSummary").c_str());
    field("GitGraphCommitLimit","Commits to analyze (10–1000)");field("GitGraphFileLimit","Files in relation graph (10–150)");
    field("GitGraphIncludeAllBranches","Include all branches");command("AnalyzeGitGraphsAsync","Analyze repository",true);
    cyui::textFaint("Read-only analysis: no index, working file or commit is modified.");
    if(!ImGui::BeginTabBar("##git-graph-tabs")) return;
    if(ImGui::BeginTabItem("Commit network")) {
        const auto& nodes=fieldJson(state_,"NativeGraphNodes");
        if(!nodes.is_array() || nodes.empty()) cyui::emptyState("No commit graph yet","Analyze the repository to load commit subjects, authors, decorations and parent connections.",cyui::icon::code);
        else {
            if(cyui::button("Zoom −")) ui.graphZoom=std::max(0.5f,ui.graphZoom-0.1f);nextButton();
            if(cyui::button("Zoom +")) ui.graphZoom=std::min(2.0f,ui.graphZoom+0.1f);nextButton();
            if(cyui::button("100%")) ui.graphZoom=1;nextButton();
            cyui::textFaint("%.0f%% · wheel scrolls · click selects · double-click loads commit details in History",ui.graphZoom*100);
            std::unordered_map<std::string,int> indexes;
            std::vector<int> lanes(nodes.size(),0);
            std::vector<std::string> active;
            int maxLane=0;
            for(size_t index=0;index<nodes.size();++index) indexes[text(nodes[index],"hash")]=static_cast<int>(index);
            for(size_t index=0;index<nodes.size();++index) {
                const auto hash=text(nodes[index],"hash");
                auto found=std::find(active.begin(),active.end(),hash);
                int lane=found==active.end()?static_cast<int>(active.size()):static_cast<int>(found-active.begin());
                if(found==active.end()) active.push_back(hash);
                lanes[index]=lane;maxLane=std::max(maxLane,lane);
                active.erase(active.begin()+lane);
                std::istringstream parents(text(nodes[index],"parents"));std::string parent;int insertion=lane;
                while(parents>>parent) if(std::find(active.begin(),active.end(),parent)==active.end()) {
                    active.insert(active.begin()+std::min(insertion,static_cast<int>(active.size())),parent);++insertion;
                }
            }
            const float rowHeight=cyui::dp(31)*ui.graphZoom;
            const float laneWidth=cyui::dp(18)*ui.graphZoom;
            const float graphWidth=std::max(cyui::dp(160),laneWidth*(maxLane+3)+cyui::dp(80));
            const float subjectX=graphWidth+cyui::dp(12),authorX=subjectX+cyui::dp(550),dateX=authorX+cyui::dp(160);
            const float width=dateX+cyui::dp(230),height=rowHeight*(nodes.size()+1);
            ImGui::BeginChild("##commit-network-canvas",ImVec2(0,0),ImGuiChildFlags_Borders,ImGuiWindowFlags_HorizontalScrollbar);
            auto origin=ImGui::GetCursorScreenPos();auto* draw=ImGui::GetWindowDrawList();
            ImGui::Dummy(ImVec2(width,height));
            const auto& p=cyui::palette();
            draw->AddRectFilled(origin,ImVec2(origin.x+width,origin.y+rowHeight),p.bg2);
            draw->AddText(ImVec2(origin.x+cyui::dp(8),origin.y+cyui::dp(7)),p.textDim,"Graph / commit");
            draw->AddText(ImVec2(origin.x+subjectX,origin.y+cyui::dp(7)),p.textDim,"Subject and references");
            draw->AddText(ImVec2(origin.x+authorX,origin.y+cyui::dp(7)),p.textDim,"Author");
            draw->AddText(ImVec2(origin.x+dateX,origin.y+cyui::dp(7)),p.textDim,"Date");
            for(size_t index=0;index<nodes.size();++index) {
                float y=origin.y+rowHeight*(index+1),center=y+rowHeight*0.5f;
                const auto hash=text(nodes[index],"hash");
                if(ui.graphSelected==hash) draw->AddRectFilled(ImVec2(origin.x,y),ImVec2(origin.x+width,y+rowHeight),p.selection);
                else if(index%2) draw->AddRectFilled(ImVec2(origin.x,y),ImVec2(origin.x+width,y+rowHeight),cyui::alpha(p.bg2,0.42f));
                draw->AddLine(ImVec2(origin.x,y+rowHeight),ImVec2(origin.x+width,y+rowHeight),p.line);
                const float x=origin.x+cyui::dp(16)+laneWidth*lanes[index];
                std::istringstream parents(text(nodes[index],"parents"));std::string parent;
                while(parents>>parent) if(auto found=indexes.find(parent);found!=indexes.end()) {
                    const float targetX=origin.x+cyui::dp(16)+laneWidth*lanes[found->second];
                    const float targetY=origin.y+rowHeight*(found->second+1.5f);
                    draw->AddBezierCubic(ImVec2(x,center),ImVec2(x,center+rowHeight*0.6f),ImVec2(targetX,targetY-rowHeight*0.6f),ImVec2(targetX,targetY),cyui::categorical(lanes[index]),cyui::dp(1.5f));
                }
                draw->AddCircleFilled(ImVec2(x,center),cyui::dp(4),cyui::categorical(lanes[index]));
                const float textY=center-ImGui::GetFontSize()*0.5f;
                draw->AddText(ImVec2(origin.x+graphWidth-cyui::dp(72),textY),p.textDim,text(nodes[index],"shortHash").c_str());
                const auto decorations=text(nodes[index],"decorations");
                std::string subject=decorations.empty()?text(nodes[index],"subject"):"["+decorations+"]  "+text(nodes[index],"subject");
                draw->PushClipRect(ImVec2(origin.x+subjectX,y),ImVec2(origin.x+authorX-cyui::dp(8),y+rowHeight),true);
                draw->AddText(ImVec2(origin.x+subjectX,textY),p.text,subject.c_str());draw->PopClipRect();
                draw->PushClipRect(ImVec2(origin.x+authorX,y),ImVec2(origin.x+dateX-cyui::dp(8),y+rowHeight),true);
                draw->AddText(ImVec2(origin.x+authorX,textY),p.textDim,text(nodes[index],"authorName").c_str());draw->PopClipRect();
                draw->AddText(ImVec2(origin.x+dateX,textY),p.textDim,text(nodes[index],"authoredAt").c_str());
                if(ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(ImVec2(origin.x,y),ImVec2(origin.x+width,y+rowHeight))) {
                    ImGui::BeginTooltip();ImGui::TextUnformatted(text(nodes[index],"subject").c_str());ImGui::TextUnformatted(hash.c_str());ImGui::TextUnformatted(text(nodes[index],"authorName").c_str());ImGui::EndTooltip();
                    if(ImGui::IsMouseClicked(0)) ui.graphSelected=hash;
                    if(ImGui::IsMouseDoubleClicked(0) && !busy()) {
                        send({{"command","invoke"},{"action","SelectExplorerCommitAsync"},{"parameters",{{"commitHash",hash}}}});
                        cyui::notify("Commit loaded into History. Open the History tab to inspect its files.",cyui::Tone::info);
                    }
                }
            }
            ImGui::EndChild();
        }
        ImGui::EndTabItem();
    }
    if(ImGui::BeginTabItem("File relations")) {
        auto selectedPath=relationGraph(fieldJson(state_,"NativeGraphRelations"),"file-relation-graph",cyui::dp(280));
        if(!selectedPath.empty()){tableViews_[tableKey("GitFileActivities")].filter=selectedPath;tableViews_[tableKey("GitFileActivities")].skip=0;lastPoll_=0;}
        if(splitBegin("##git-file-relations",0.56f)) {
            cyui::heading("Frequently changed files",3);list("GitFileActivities");
            ImGui::TableNextColumn();cyui::heading("Files changed together",3);list("GitFileRelations");ImGui::EndTable();
        }
        ImGui::EndTabItem();
    }
    if(ImGui::BeginTabItem("Git activity")) {
        cyui::textDim("%s",value("GitInsightsSummary").c_str());
        const auto& days=fieldJson(state_,"NativeGraphDays");
        if(days.is_array() && !days.empty()) {
            std::map<long,const Json*> activity;int maximum=1;
            for(const auto& day:days) {activity[calendarDay(text(day,"day"))]=&day;maximum=std::max(maximum,day.value("commitCount",0));}
            long last=activity.rbegin()->first,first=std::max(activity.begin()->first,last-364);
            long monday=first-((first+3)%7+7)%7;
            const float cell=cyui::dp(14),gap=cyui::dp(3),step=cell+gap;
            int weeks=static_cast<int>((last-monday)/7+1);
            ImGui::BeginChild("##git-activity-heatmap",cyui::dp(0,160),ImGuiChildFlags_Borders,ImGuiWindowFlags_HorizontalScrollbar);
            auto origin=ImGui::GetCursorScreenPos();origin.y+=cyui::dp(16);
            ImGui::Dummy(ImVec2((weeks+1)*step,cyui::dp(142)));
            auto* draw=ImGui::GetWindowDrawList();const auto& palette=cyui::palette();
            for(long day=monday;day<=last;++day) {
                int week=static_cast<int>((day-monday)/7),weekday=static_cast<int>((day-monday)%7);
                auto at=activity.find(day);int commits=at==activity.end()?0:at->second->value("commitCount",0);
                auto min=ImVec2(origin.x+week*step,origin.y+weekday*step),max=ImVec2(min.x+cell,min.y+cell);
                ImU32 colour=commits==0?palette.bg3:cyui::mix(palette.bg3,palette.success,0.2f+0.8f*std::sqrt(static_cast<float>(commits)/maximum));
                draw->AddRectFilled(min,max,colour,cyui::dp(2));
                if(ImGui::IsWindowHovered() && ImGui::IsMouseHoveringRect(min,max)) {
                    ImGui::BeginTooltip();ImGui::Text("%s · %d commits",dayLabel(day).c_str(),commits);
                    if(at!=activity.end()) ImGui::Text("%d files · +%lld / -%lld",at->second->value("filesTouched",0),at->second->value("addedLines",0LL),at->second->value("deletedLines",0LL));
                    ImGui::EndTooltip();
                }
                if(weekday==0 && (week%4)==0) draw->AddText(ImVec2(min.x,origin.y-cyui::dp(15)),palette.textDim,dayLabel(day).substr(5).c_str());
            }
            ImGui::EndChild();
            cyui::textFaint("Daily commits · intensity relative to the busiest day in this analysis · last 365 analyzed days");
        } else cyui::emptyState("No activity yet","Analyze the repository to display the daily activity heatmap.",cyui::icon::calendar);
        if(splitBegin("##git-contributors",0.5f)) {
            cyui::heading("Contributors",3);list("GitContributors");ImGui::TableNextColumn();cyui::heading("Hot files",3);list("GitHotFiles");ImGui::EndTable();
        }
        ImGui::EndTabItem();
    }
    if(flag("IsUnrealIntegrationEnabled") && ImGui::BeginTabItem("Unreal dependencies")) {
        cyui::textDim("%s",value("UnrealDependencySummary").c_str());
        auto selectedPath=relationGraph(fieldJson(state_,"NativeUnrealRelations"),"unreal-relation-graph",cyui::dp(280));
        if(!selectedPath.empty()){tableViews_[tableKey("UnrealAssets")].filter=selectedPath;tableViews_[tableKey("UnrealAssets")].skip=0;lastPoll_=0;}
        if(splitBegin("##unreal-dependencies",0.55f)) {
            list("UnrealAssets");ImGui::TableNextColumn();list("UnrealDependencyRelations");ImGui::EndTable();
        }
        ImGui::EndTabItem();
    }
    ImGui::EndTabBar();
}

void Workspace::gitChangesPanel() {
    auto& ui=gitStates[{this,activePane_}];
    if(ui.project!=projectId_) {ui=GitUiState{};ui.project=projectId_;}
    cyui::textDim("%s",value("ChangePreparationSummary").c_str());
    command("ScanAllUntrackedFilesAsync","Scan untracked"); nextButton();
    command("IncludeAllPreparedChanges","Include all"); nextButton();
    command("KeepAllPreparedChanges","Keep all");
    if(flag("HasGitConflicts")) {
        nextButton();cyui::ScopedDisabled disabled(busy());
        if(cyui::dangerButton("Resolve Git conflicts...")) send({{"command","gitConflict"},{"operation","open"}});
    }
    field("ChangeSearch","Search files, folders, state or owner (supports *.cpp)");
    selector("ChangeSortOptions","ChangeSort","Sort files");
    const char* modes[]={"List","Folders"};
    if(cyui::segmented("##changes-presentation",modes,2,&ui.changesMode)) {ui.changesSkip=0;ui.changes.clear();ui.changeAnchor=-1;lastPoll_=0;}
    cyui::tooltip("List groups files by versioning state; Folders shows the complete hierarchy. Local stays collapsed by default.");
    const char* source=ui.changesMode==0 ? "NativeGitChanges" : "NativeGitFolders";
    requestTable(source,200,ui.changesSkip);
    const auto& rows=tableRows(state_,tableKey(source));
    const auto& table=tableData(state_,tableKey(source));
    const int total=table.value("total",0);
    {
        cyui::ScopedDisabled disabled(ui.changesSkip<=0);
        if(cyui::ghostButton("Previous files")) {ui.changesSkip=std::max(0,ui.changesSkip-200);ui.changeAnchor=-1;lastPoll_=0;}
    }
    nextButton();
    {
        cyui::ScopedDisabled disabled(ui.changesSkip+200>=total);
        if(cyui::ghostButton("Next files")) {ui.changesSkip+=200;ui.changeAnchor=-1;lastPoll_=0;}
    }
    nextButton(); cyui::textFaint("%d–%d / %d visible rows · %zu highlighted",total?ui.changesSkip+1:0,std::min(total,ui.changesSkip+200),total,ui.changes.size());
    const float reserved=cyui::dp(168);
    float listHeight=std::max(cyui::dp(100),ImGui::GetContentRegionAvail().y-reserved);
    if(ImGui::BeginTable("##change-tree",6,ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|
        ImGuiTableFlags_RowBg|ImGuiTableFlags_BordersInnerV|ImGuiTableFlags_ScrollX|ImGuiTableFlags_ScrollY,ImVec2(0,listHeight))) {
        ImGui::TableSetupColumn("File / folder",ImGuiTableColumnFlags_WidthFixed,cyui::dp(380));
        ImGui::TableSetupColumn("State",ImGuiTableColumnFlags_WidthFixed,cyui::dp(110));
        ImGui::TableSetupColumn("Preparation",ImGuiTableColumnFlags_WidthFixed,cyui::dp(100));
        ImGui::TableSetupColumn("Area",ImGuiTableColumnFlags_WidthFixed,cyui::dp(110));
        ImGui::TableSetupColumn("Lock owner",ImGuiTableColumnFlags_WidthFixed,cyui::dp(160));
        ImGui::TableSetupColumn("Path",ImGuiTableColumnFlags_WidthFixed,cyui::dp(480));
        ImGui::TableSetupScrollFreeze(1,1); ImGui::TableHeadersRow();
        ImGuiListClipper clipper; clipper.Begin(static_cast<int>(rows.size()));
        while(clipper.Step()) for(int index=clipper.DisplayStart;index<clipper.DisplayEnd;++index) {
            const auto& row=rows[index]; const auto& v=row["values"];
            const auto key=text(row,"key"); cyui::ScopedID scope(key.c_str());
            ImGui::TableNextRow(); ImGui::TableSetColumnIndex(0);
            bool clicked=ImGui::Selectable("##select",ui.changes.count(key)!=0,ImGuiSelectableFlags_SpanAllColumns|ImGuiSelectableFlags_AllowOverlap);
            if(clicked && !busy()) {
                if(v.value("IsPlaceholder",false)) send({{"command","gitChange"},{"source",source},{"operation","loadMore"},{"keys",Json::array({key})}});
                else {
                    pickRow(ui.changes,rows,index,ui.changeAnchor);
                    send({{"command","gitChange"},{"source",source},{"operation","select"},{"keys",Json::array({key})}});
                }
            }
            if(ImGui::BeginPopupContextItem("##change-menu")) {
                if(!ui.changes.count(key)) {ui.changes.clear();ui.changes.insert(key);}
                cyui::ScopedDisabled disabled(busy());
                if(ImGui::MenuItem("Include all selected descendants")) send({{"command","gitChange"},{"source",source},{"operation","include"},{"keys",keys(ui.changes)},{"included",true}});
                if(ImGui::MenuItem("Keep selected files out of commit")) send({{"command","gitChange"},{"source",source},{"operation","include"},{"keys",keys(ui.changes)},{"included",false}});
                ImGui::Separator();
                if(ImGui::MenuItem("Set local (unversioned files)")) send({{"command","gitChange"},{"source",source},{"operation","local"},{"keys",keys(ui.changes)}});
                if(ImGui::MenuItem("Restore Local files to list")) send({{"command","gitChange"},{"source",source},{"operation","restore"},{"keys",keys(ui.changes)}});
                if(ImGui::MenuItem("Rollback selected...")) invoke(customAction("RollbackNativeChanges","Rollback selected working files","gitChange",{{"source",source},{"operation","rollback"},{"keys",keys(ui.changes)}},true));
                if(ImGui::MenuItem("Delete selected files...")) invoke(customAction("DeleteNativeChanges","Delete selected files from disk","gitChange",{{"source",source},{"operation","delete"},{"keys",keys(ui.changes)}},true));
                if(ImGui::MenuItem("Copy path")) ImGui::SetClipboardText(text(v,"Path").c_str());
                ImGui::EndPopup();
            }
            ImGui::SameLine(0,0); ImGui::SetCursorPosX(ImGui::GetCursorPosX()+cyui::dp(16)*v.value("Depth",0));
            const bool directory=v.value("IsDirectory",false), expanded=v.value("IsExpanded",false);
            if(directory) {
                cyui::ScopedDisabled disabled(busy());
                if(cyui::iconButton(expanded ? cyui::icon::chevronDown : cyui::icon::folder,expanded ? "Collapse folder" : "Expand folder",false,0,cyui::dp(22,22))) {
                    send({{"command","gitChange"},{"source",source},{"operation","expand"},{"keys",Json::array({key})},{"expanded",!expanded}}); lastPoll_=0;
                }
            } else ImGui::Dummy(cyui::dp(22,0));
            ImGui::SameLine(0,cyui::dp(3));
            if(v.value("CanInclude",false)) {
                const auto included=v.value("IsIncluded",Json());
                bool checked=included.is_boolean() && included.get<bool>();
                if(included.is_null()) ImGui::PushItemFlag(ImGuiItemFlags_MixedValue,true);
                {
                    cyui::ScopedDisabled disabled(busy());
                    if(ImGui::Checkbox("##include",&checked)) {
                        send({{"command","gitChange"},{"source",source},{"operation","include"},{"keys",Json::array({key})},{"included",checked}}); lastPoll_=0;
                    }
                }
                if(included.is_null()) ImGui::PopItemFlag();
                ImGui::SameLine(0,cyui::dp(5));
            }
            cyui::textColoured(v.value("IsLocalOnly",false) ? cyui::palette().textFaint : directory ? cyui::palette().accent : statusColour(text(v,"State")),"%s",text(v,"Name").c_str());
            cyui::tooltip(text(v,"Path").c_str());
            ImGui::TableSetColumnIndex(1); ImGui::TextUnformatted(text(v,"State").c_str());
            ImGui::TableSetColumnIndex(2); ImGui::TextUnformatted(text(v,"Preparation").c_str());
            ImGui::TableSetColumnIndex(3); ImGui::TextUnformatted(text(v,"Area").c_str());
            ImGui::TableSetColumnIndex(4); ImGui::TextUnformatted(text(v,"LockOwner").c_str());
            ImGui::TableSetColumnIndex(5); ImGui::TextUnformatted(text(v,"Path").c_str());
        }
        ImGui::EndTable();
    }
    if(rows.empty()) cyui::textFaint("No visible changes. Refresh or scan untracked files to update this list.");
    {
        cyui::ScopedDisabled disabled(busy() || ui.changes.empty());
        if(cyui::button("Set local")) send({{"command","gitChange"},{"source",source},{"operation","local"},{"keys",keys(ui.changes)}});
        nextButton(); if(cyui::button("Restore to list")) send({{"command","gitChange"},{"source",source},{"operation","restore"},{"keys",keys(ui.changes)}});
        nextButton(); if(cyui::dangerButton("Delete...")) invoke(customAction("DeleteNativeChanges","Delete selected files from disk","gitChange",{{"source",source},{"operation","delete"},{"keys",keys(ui.changes)}},true));
        nextButton(); if(cyui::dangerButton("Rollback...")) invoke(customAction("RollbackNativeChanges","Rollback selected working files","gitChange",{{"source",source},{"operation","rollback"},{"keys",keys(ui.changes)}},true));
    }
    field("CommitMessage","Commit message",true);
    command("GenerateAiCommitDescriptionAsync","AI description"); nextButton();
    command("StageAllAsync","Stage included"); nextButton();
    command("RollbackIncludedChangesAsync","Rollback included..."); nextButton();
    command("CommitAsync","Commit selected",true);
}

void Workspace::gitBranchInspector() {
    if(!flag("BranchInspectorOpen")) return;
    auto& ui=gitStates[{this,activePane_}];
    ImGui::SetNextWindowSize(cyui::dp(1120,740),ImGuiCond_FirstUseEver);
    bool open=true;
    if(ImGui::Begin((value("BranchInspectorTitle")+"###native-branch-inspector").c_str(),&open)) {
        cyui::textDim("%s",value("BranchInspectorSummary").c_str());
        if(flag("BranchInspectorBusy")) {
            const auto fraction=flag("BranchInspectorIndeterminate") ? -1.0f : std::stof(value("BranchInspectorProgress","0"))/100.0f;
            cyui::progressBar(fraction,ImVec2(-1,cyui::dp(6)),value("BranchInspectorStage").c_str());
            cyui::textFaint("%s",value("BranchInspectorDetail").c_str());
            if(cyui::button("Cancel Git / LFS operation")) send({{"command","gitBranchInspector"},{"operation","cancel"}});
        }
        {
            cyui::ScopedDisabled disabled(busy() || flag("BranchInspectorBusy"));
            if(cyui::button("Refresh branch tree")) send({{"command","gitBranchInspector"},{"operation","open"}});
            nextButton();
            {
                cyui::ScopedDisabled remoteDisabled(!flag("BranchInspectorCanRefreshRemote"));
                if(cyui::button("Fetch remote inspection only")) send({{"command","gitBranchInspector"},{"operation","refresh"}});
                cyui::tooltip("Fetches only the chosen branch into a temporary inspection reference. It does not checkout or update the working branch.");
            }
        }
        if(ImGui::BeginTabBar("##branch-inspector-tabs")) {
            if(ImGui::BeginTabItem("Files and preview")) {
                if(ImGui::InputTextWithHint("##branch-search","Find branch files (supports *.uasset)",&ui.branchSearch,ImGuiInputTextFlags_EnterReturnsTrue) || ImGui::IsItemDeactivatedAfterEdit()) {
                    send({{"command","gitBranchInspector"},{"operation","search"},{"search",ui.branchSearch}});
                    ui.branchSkip=0;ui.branchFiles.clear();ui.branchPaths.clear();lastPoll_=0;
                }
                requestTable("NativeBranchFiles",200,ui.branchSkip);
                const auto& rows=tableRows(state_,tableKey("NativeBranchFiles"));
                const auto& snapshot=tableData(state_,tableKey("NativeBranchFiles"));
                int total=snapshot.value("total",0);
                if(splitBegin("##branch-files-split",0.52f)) {
                    {
                        cyui::ScopedDisabled disabled(ui.branchSkip<=0);
                        if(cyui::ghostButton("Previous")) {ui.branchSkip=std::max(0,ui.branchSkip-200);ui.branchAnchor=-1;lastPoll_=0;}
                    }
                    nextButton(); {
                        cyui::ScopedDisabled disabled(ui.branchSkip+200>=total);
                        if(cyui::ghostButton("Next")) {ui.branchSkip+=200;ui.branchAnchor=-1;lastPoll_=0;}
                    }
                    nextButton(); cyui::textFaint("%d rows",total);
                    if(ImGui::BeginTable("##branch-file-tree",3,ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|ImGuiTableFlags_RowBg|ImGuiTableFlags_ScrollX|ImGuiTableFlags_ScrollY,ImVec2(0,std::max(cyui::dp(100),ImGui::GetContentRegionAvail().y-cyui::dp(150))))) {
                        ImGui::TableSetupColumn("File / folder",ImGuiTableColumnFlags_WidthFixed,cyui::dp(350));
                        ImGui::TableSetupColumn("Size",ImGuiTableColumnFlags_WidthFixed,cyui::dp(110));
                        ImGui::TableSetupColumn("Path",ImGuiTableColumnFlags_WidthFixed,cyui::dp(500));
                        ImGui::TableSetupScrollFreeze(1,1);ImGui::TableHeadersRow();
                        ImGuiListClipper clipper;clipper.Begin(static_cast<int>(rows.size()));
                        while(clipper.Step()) for(int index=clipper.DisplayStart;index<clipper.DisplayEnd;++index) {
                            const auto& row=rows[index];const auto& v=row["values"];auto key=text(row,"key");
                            cyui::ScopedID scope(key.c_str());
                            ImGui::TableNextRow();ImGui::TableNextColumn();
                            bool directory=v.value("IsDirectory",false),expanded=v.value("IsExpanded",false);
                            if(ImGui::Selectable("##file",ui.branchFiles.count(key)!=0,ImGuiSelectableFlags_SpanAllColumns|ImGuiSelectableFlags_AllowOverlap) && !busy()) {
                                pickRow(ui.branchFiles,rows,index,ui.branchAnchor);
                                for(auto it=ui.branchPaths.begin();it!=ui.branchPaths.end();) if(!ui.branchFiles.count(it->first)) it=ui.branchPaths.erase(it); else ++it;
                                for(const auto& candidate:rows) if(ui.branchFiles.count(text(candidate,"key"))) ui.branchPaths[text(candidate,"key")]=text(candidate["values"],"Path");
                                Json paths=Json::array();
                                for(const auto& path:ui.branchPaths) paths.push_back(path.second);
                                send({{"command","gitBranchInspector"},{"operation","select"},{"paths",paths}});
                            }
                            ImGui::SameLine(0,cyui::dp(16)*v.value("Depth",0));
                            if(directory) {
                                if(cyui::iconButton(expanded?cyui::icon::chevronDown:cyui::icon::folder,expanded?"Collapse folder":"Expand folder",false,0,cyui::dp(22,22)))
                                    send({{"command","gitBranchInspector"},{"operation","expand"},{"keys",Json::array({key})},{"expanded",!expanded}});
                                ImGui::SameLine();
                            }
                            ImGui::TextUnformatted(text(v,"Name").c_str());
                            ImGui::TableNextColumn(); ImGui::TextUnformatted(text(v,"Size").c_str());
                            ImGui::TableNextColumn(); ImGui::TextUnformatted(text(v,"Path").c_str());
                        }
                        ImGui::EndTable();
                    }
                    cyui::textDim("%s",value("BranchInspectorSelected").c_str());
                    {
                        cyui::ScopedDisabled disabled(busy() || flag("BranchInspectorBusy"));
                        if(cyui::button("Retrieve selected files...")) invoke(customAction("RetrieveNativeBranchFiles","Retrieve selected Git / LFS files","gitBranchInspector",{{"operation","retrieve"}},true,
                            Json::array({parameter("destination","Destination directory","text","")})));
                        nextButton();if(cyui::button("Export file...")) invoke(customAction("ExportNativeBranchFile","Export selected file","gitBranchInspector",{{"operation","export"}},true,
                            Json::array({parameter("destination","Destination file path","text","")})));
                        if(cyui::dangerButton("Restore selected file to working tree...")) invoke(customAction("RestoreNativeBranchFile","Restore selected working file with a safety backup","gitBranchInspector",{{"operation","restore"}},true));
                    }
                    ImGui::TableNextColumn();
                    cyui::heading("File preview",3);cyui::textDim("%s",value("BranchInspectorPreviewSummary").c_str());
                    {
                        cyui::ScopedDisabled disabled(busy() || flag("BranchInspectorBusy"));
                        if(cyui::button("Retrieve missing LFS preview")) send({{"command","gitBranchInspector"},{"operation","preview"}});
                        nextButton();if(cyui::button("Compare with current HEAD")) send({{"command","gitBranchInspector"},{"operation","diff"}});
                    }
                    imagePanel("BranchInspectorPreviewImage","branch-preview-image");
                    textPanel("BranchInspectorPreview","branch-preview");
                    ImGui::EndTable();
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Comparison")) {
                cyui::textDim("%s",value("BranchInspectorDiffSummary").c_str());
                diffPanel("BranchInspectorDiff","branch-comparison");ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Retrieval / restoration history")) {list("NativeBranchHistory");ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }
        cyui::textFaint("%s",value("BranchInspectorStatus").c_str());
    }
    ImGui::End();
    if(!open) {send({{"command","gitBranchInspector"},{"operation","close"}});ui.branchFiles.clear();ui.branchPaths.clear();ui.branchSearch.clear();ui.branchSkip=0;}
}

bool Workspace::gitView() {
    if(workspace_=="changes") {
        if(splitBegin("##changes-review",0.48f)) {
            ImGui::BeginChild("##commit-editor"); gitChangesPanel(); ImGui::EndChild();
            ImGui::TableNextColumn();
            cyui::heading("Diff preview",3);
            field("IsChangesDiffPreviewEnabled","Load selected file diff");
            cyui::textDim("%s",value("DiffPresentationSummary").c_str());
            diffPanel("DiffText","changes-diff");
            ImGui::EndTable();
        }
    } else if(workspace_=="history") {
        field("ExplorerSearch","Find commit by message, author or hash");
        selector("History","SelectedComparisonRevision","Compare with revision");
        command("CompareExplorerCommitsAsync","Compare revisions");nextButton();
        if(cyui::button("Compose restore...")) navigate("composition");nextButton();
        command("FetchAsync","Fetch remotes");nextButton();
        {cyui::ScopedDisabled disabled(!flag("IsExplorerAnyLoading"));if(cyui::button("Cancel loading")) send({{"command","cancel"}});}
        if(ImGui::BeginTable("##history-workspace",3,ImGuiTableFlags_Resizable|ImGuiTableFlags_BordersInnerV|ImGuiTableFlags_SizingStretchProp)) {
            ImGui::TableSetupColumn("Timeline",ImGuiTableColumnFlags_WidthStretch,0.8f);
            ImGui::TableSetupColumn("Files",ImGuiTableColumnFlags_WidthStretch,1.0f);
            ImGui::TableSetupColumn("Diff",ImGuiTableColumnFlags_WidthStretch,1.2f);
            ImGui::TableNextRow();ImGui::TableNextColumn();
            cyui::heading("Revision history",3);cyui::textDim("%s",value("HistoryScope").c_str());
            list("History");
            ImGui::TableNextColumn();cyui::heading("Files in selected revision",3);
            cyui::textDim("%s",value("ExplorerSummary").c_str());
            list("ExplorerFiles",ImVec2(0,std::max(cyui::dp(120),ImGui::GetContentRegionAvail().y*0.54f)));
            command("ExportSelectedExplorerFileAsync","Export this version...");
            cyui::sectionTitle("File history (Git / LFS)");list("ExplorerFileHistory");
            ImGui::TableNextColumn();cyui::heading("Selected file difference",3);
            cyui::textDim("%s",value("ExplorerDiffPresentationSummary").c_str());
            if(flag("IsExplorerAnyLoading")) cyui::progressBar(-1.0f,ImVec2(-1,cyui::dp(6)),"Reading revision and Git / LFS contents");
            diffPanel("ExplorerDiff","history-diff");
            ImGui::EndTable();
        }
    } else if(workspace_=="composition") {
        if(ImGui::BeginTabBar("##composition-tabs")) {
            if(ImGui::BeginTabItem("Restore selected versions")) {
                selector("History","MultiRestoreCommit","Source commit");
                {cyui::ScopedDisabled disabled(busy());if(cyui::button("Load selected revision")) send({{"command","gitComposition"},{"operation","loadRestore"}});}nextButton();
                if(cyui::button("Select all files")) send({{"command","invoke"},{"action","SelectAllMultiRestoreFiles"},{"parameters",{{"selected",true}}}});
                nextButton();if(cyui::button("Clear files")) send({{"command","invoke"},{"action","SelectAllMultiRestoreFiles"},{"parameters",{{"selected",false}}}});
                nextButton();command("PreviewMultiRestoreAsync","Safety preview");
                if(splitBegin("##compose-restore",0.56f)) {
                    cyui::heading("Files and restore points",3);
                    auto chosen=selectedRows("MultiRestoreFiles");
                    {
                        cyui::ScopedDisabled disabled(busy() || chosen.empty());
                        if(cyui::button("Use version before commit")) send({{"command","gitRows"},{"source","MultiRestoreFiles"},{"keys",rowKeys(chosen)},{"restorePoint","BeforeCommit"}});
                        nextButton();if(cyui::button("Use version at commit")) send({{"command","gitRows"},{"source","MultiRestoreFiles"},{"keys",rowKeys(chosen)},{"restorePoint","AtCommit"}});
                    }
                    list("MultiRestoreFiles",ImVec2(0,std::max(cyui::dp(140),ImGui::GetContentRegionAvail().y*0.52f)));
                    cyui::sectionTitle("Planned working-tree operations");list("MultiRestoreOperations",ImVec2(0,std::max(cyui::dp(80),ImGui::GetContentRegionAvail().y-cyui::dp(105))));
                    field("MultiRestoreOverwriteLocalChanges","Allow replacing local changes after safety backup");
                    cyui::textDim("%s",value("MultiRestoreSummary").c_str());
                    {cyui::ScopedDisabled disabled(!flag("CanApplyMultiRestore"));command("ApplyMultiRestoreAsync","Apply versions...",true);}
                    ImGui::TableNextColumn();cyui::heading("Selected version difference",3);diffPanel("MultiRestoreDiff","compose-restore-diff");
                    ImGui::EndTable();
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Compose commits (cherry-pick)")) {
                selector("BranchCompareSources","BranchCompareSource","Source branch");
                selector("BranchCompareTargets","BranchCompareTarget","Target branch");
                command("CompareBranchesForCompositionAsync","Compare branches");
                cyui::textDim("%s",value("BranchComparisonSummary").c_str());
                if(splitBegin("##compose-cherry",0.58f)) {
                    if(cyui::button("Select source-only commits")) send({{"command","invoke"},{"action","SelectAllSourceOnlyCommits"},{"parameters",{{"selected",true}}}});
                    nextButton();if(cyui::button("Clear commits")) send({{"command","invoke"},{"action","SelectAllSourceOnlyCommits"},{"parameters",{{"selected",false}}}});
                    nextButton();if(cyui::button("Move up")) send({{"command","invoke"},{"action","MoveSelectedCherryPickCommit"},{"parameters",{{"offset",-1}}}});
                    nextButton();if(cyui::button("Move down")) send({{"command","invoke"},{"action","MoveSelectedCherryPickCommit"},{"parameters",{{"offset",1}}}});
                    list("BranchComparisonCommits",ImVec2(0,std::max(cyui::dp(140),ImGui::GetContentRegionAvail().y-cyui::dp(200))));
                    field("SelectedCherryPickMode","Commit composition mode");
                    if(flag("CombinesCherryPickCommits")) field("CombinedCherryPickMessage","Combined commit message",true);
                    cyui::textDim("%s",value("CherryPickPlanSummary").c_str());
                    command("PreviewCherryPickAsync","Preview local operation");nextButton();
                    {cyui::ScopedDisabled disabled(!flag("CanApplyCherryPick"));command("ApplyCherryPickAsync","Apply commits locally...",true);}
                    ImGui::TableNextColumn();diffPanel("CherryPickDiff","cherry-pick-diff");
                    ImGui::EndTable();
                }
                ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
    } else if(workspace_=="branches") {
        if(splitBegin("##branches-workspace",0.47f)) {
            cyui::heading("Local and remote branches",3);
            cyui::textFaint("Remote references are read-only until a local branch is created or checked out.");
            field("BranchSearch","Search branch, remote or status");selector("BranchSortOptions","BranchSort","Sort branches");
            command("FetchAsync","Fetch remotes");
            list("FilteredBranches",ImVec2(0,std::max(cyui::dp(140),ImGui::GetContentRegionAvail().y-cyui::dp(146))));
            command("CheckoutSelectedBranchAsync","Switch / create local");nextButton();
            command("MergeSelectedBranchAsync","Merge from selected...");nextButton();command("MergeCurrentBranchIntoSelectedAsync","Merge current into selected...");
            auto branchKeys=rowKeys(selectedRows("FilteredBranches"));
            {cyui::ScopedDisabled disabled(busy() || branchKeys.empty());
                if(cyui::button("Check local removal safety")) send({{"command","gitBranches"},{"operation","analyze"},{"keys",branchKeys}});
                nextButton();if(cyui::dangerButton("Remove local branch(es)...")) invoke(customAction("RemoveNativeLocalBranches","Remove selected local branches; remote references stay untouched","gitBranches",{{"operation","remove"},{"keys",branchKeys}},true,
                    Json::array({parameter("force","Force removal even without a retained remote copy (may lose unpublished commits)","bool",false)})));
            }
            if(!value("BranchRemovalSummary").empty()) cyui::callout(cyui::Tone::warning,value("BranchRemovalSummary").c_str());
            ImGui::TableNextColumn();cyui::heading("Selected branch commits",3);
            cyui::textDim("%s",value("SelectedBranchSummary").c_str());
            if(cyui::beginProperties("##branch-details",145.0f)) {
                labelValue("Comparison base",value("BranchDetailsBase"));labelValue("Commits ahead",value("BranchDetailsAhead"));
                labelValue("Created by (inferred)",value("BranchDetailsCreator"));labelValue("Estimated creation",value("BranchDetailsCreated"));
                labelValue("Last update",value("BranchDetailsUpdated"));labelValue("Last commit",value("BranchDetailsCommit"));
                cyui::endProperties();
            }
            {cyui::ScopedDisabled disabled(busy());
                if(cyui::button("Browse branch files...")) send({{"command","gitBranchInspector"},{"operation","open"}});
                nextButton();command("CreateBranchAsync","Create branch...");
            }
            list("SelectedBranchHistory",ImVec2(0,std::max(cyui::dp(100),ImGui::GetContentRegionAvail().y*0.5f)));
            cyui::sectionTitle("Historical worktrees");cyui::textDim("%s",value("HistoricalWorktreeStatus").c_str());
            command("RefreshHistoricalWorktreesAsync","Refresh worktrees");nextButton();command("RemoveSelectedHistoricalWorktreeAsync","Remove worktree...");
            list("HistoricalWorktrees");ImGui::EndTable();
        }
    } else if(workspace_=="annotations") {
        if(splitBegin("##annotations-workspace",0.56f)) {
            field("GitAnnotationSearch","Search annotations");selector("GitAnnotationKindFilters","GitAnnotationKindFilter","Target kind");
            cyui::textDim("%s",value("GitAnnotationStatus").c_str());list("FilteredGitAnnotations");
            ImGui::TableNextColumn();cyui::heading("Local annotation",3);
            command("NewGitAnnotation","New");nextButton();command("UseSelectedCommitForAnnotation","Use selected commit");nextButton();command("UseSelectedBranchForAnnotation","Use selected branch");
            selector("GitAnnotationEditorKinds","GitAnnotationEditorKind","Kind");
            field("GitAnnotationTarget","Commit hash or branch");field("GitAnnotationTitle","Title");field("GitAnnotationNote","Note",true);field("GitAnnotationTags","Tags");
            command("SaveGitAnnotationAsync","Save local annotation",true);nextButton();command("DeleteSelectedGitAnnotationAsync","Delete annotation...");
            cyui::callout(cyui::Tone::info,"Annotations are stored in CyRevision. They never modify Git commits or branches.");
            ImGui::EndTable();
        }
    } else if(workspace_=="graphs") {
        gitGraphsPanel();
    } else if(workspace_=="locks") {
        cyui::textDim("%s",value("LfsLocksSummary").c_str());command("RefreshLfsLocksAsync","Refresh locks",true);
        if(ImGui::BeginTabBar("##lock-scopes")) {
            for(const auto& scope:std::vector<std::pair<const char*,const char*>>{{"Project locks","FilteredLfsLocks"},{"My locks","FilteredMyLfsLocks"}}) if(ImGui::BeginTabItem(scope.first)) {
                const bool ours=std::string(scope.second)=="FilteredMyLfsLocks";
                field(ours?"MyLfsLockSearch":"AllLfsLockSearch","Search file or owner");
                selector("LfsLockSortOptions",ours?"MyLfsLockSort":"AllLfsLockSort","Sort locks");
                auto selected=rowKeys(selectedRows(scope.second));
                {cyui::ScopedDisabled disabled(busy() || selected.empty());
                    if(cyui::button("Unlock selected...")) invoke(customAction("UnlockNativeLfsLocks","Unlock selected LFS files","gitRows",{{"source",scope.second},{"keys",selected}},true,
                        Json::array({parameter("force","Force unlock locks owned by another user (requires permission)","bool",false)})));
                }
                nextButton();{
                    cyui::ScopedDisabled disabled(busy());
                    if(cyui::button(ours?"Unlock all my locks...":"Unlock all locks...")) invoke(customAction("UnlockAllLfsLocksAsync","Unlock LFS files","invoke",{{"action","UnlockAllLfsLocksAsync"}},true,
                        Json::array({parameter("forceEveryLock","Force unlock locks owned by other users (requires permission)","bool",false)})));
                }
                list(scope.second);ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
    } else if(workspace_=="ignore") {
        selector("GitIgnoreSources","GitIgnoreSource","Ignore file");
        cyui::textDim("%s",value("GitIgnoreFilePath").c_str());
        command("LoadGitIgnoreAsync","Reload");nextButton();command("RefreshIgnoredFilesAsync","Scan ignored files");nextButton();command("SaveGitIgnoreAsync","Save rules",true);
        if(ImGui::BeginTabBar("##gitignore-tabs")) {
            if(ImGui::BeginTabItem("Edit rules")) {
                selector("GitIgnoreTemplates","GitIgnoreTemplate","Template");command("ApplyGitIgnoreTemplate","Append template");
                cyui::textDim("%s",value("GitIgnoreSummary").c_str());
                field("GitIgnoreContent","Ignore patterns",true);field("GitIgnoreTestPath","Test a relative file path");command("TestGitIgnorePathAsync","Test pattern matching");
                cyui::textDim("%s",value("GitIgnoreTestResult").c_str());ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Suggestions")) {
                cyui::textDim("%s",value("IgnoreSuggestionSummary").c_str());command("LoadIgnoreSuggestionsAsync","Refresh suggestions");nextButton();command("AppendSelectedSuggestionsToGitIgnore","Append checked suggestions");
                if(splitBegin("##ignore-suggestions")) {
                    field("IgnoreFolderSearch","Find folder");selector("IgnoreSuggestionFilters","IgnoreFolderFilter","Folder visibility");list("IgnoreFolderSuggestions");
                    ImGui::TableNextColumn();field("IgnoreFileTypeSearch","Find file type");selector("IgnoreSuggestionFilters","IgnoreFileTypeFilter","Type visibility");list("IgnoreFileTypeSuggestions");
                    ImGui::EndTable();
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Parsed rules")) {list("GitIgnoreRules");ImGui::EndTabItem();}
            if(ImGui::BeginTabItem("Ignored files")) {list("IgnoredFiles");ImGui::EndTabItem();}
            ImGui::EndTabBar();
        }
    } else if(workspace_=="lfs") {
        if(ImGui::BeginTabBar("##lfs-workspaces")) {
            if(ImGui::BeginTabItem("Time machine")) {
                if(ImGui::BeginTable("##lfs-timeline",3,ImGuiTableFlags_Resizable|ImGuiTableFlags_BordersInnerV|ImGuiTableFlags_SizingStretchProp)) {
                    ImGui::TableSetupColumn("Files",ImGuiTableColumnFlags_WidthStretch,0.9f);ImGui::TableSetupColumn("Versions",ImGuiTableColumnFlags_WidthStretch,1.2f);ImGui::TableSetupColumn("Selected version",ImGuiTableColumnFlags_WidthStretch,0.9f);
                    ImGui::TableNextRow();ImGui::TableNextColumn();
                    field("LfsPattern","Track file pattern");command("TrackLfsPatternAsync","Add LFS rule");command("LoadLfsInventoryAsync","Load LFS files",true);
                    if(ImGui::CollapsingHeader("Tracked patterns")) list("LfsPatterns",cyui::dp(0,140));
                    cyui::heading("Versioned files",3);
                    if(ImGui::BeginTabBar("##lfs-file-presentations")) {
                        if(ImGui::BeginTabItem("List")) {list("LfsFiles");ImGui::EndTabItem();}
                        if(ImGui::BeginTabItem("Folders")) {gitLfsFolders();ImGui::EndTabItem();}
                        ImGui::EndTabBar();
                    }
                    ImGui::TableNextColumn();cyui::heading("LFS versions",3);cyui::textDim("%s",value("LfsTimelineSummary").c_str());list("LfsVersions");
                    ImGui::TableNextColumn();cyui::heading("Selected object",3);
                    imagePanel("LfsPreview","lfs-object-preview");
                    cyui::textDim("%s",value("PeerLfsTransferSummary").c_str());
                    command("RequestSelectedLfsVersionFromPeerAsync","Request from verified peer");
                    command("ExportSelectedLfsVersionAsync","Export version...");command("RestoreSelectedLfsVersionAsync","Restore working file...");
                    cyui::callout(cyui::Tone::info,"Local objects can be exported or restored. Missing objects are not silently downloaded. Restoration changes only the working file and does not stage it.");
                    ImGui::EndTable();
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Storage and safe cleanup")) {
                ImGui::BeginChild("##lfs-storage-scroll");
                cyui::heading("External Git LFS storage and retention policy",3);
                cyui::callout(cyui::Tone::info,"Current checkouts, worktrees and retained local branches stay protected. Removing cached objects requires a verified remote, peer or archive copy. Analysis never deletes files.");
                if(ImGui::CollapsingHeader("Storage locations and retention",ImGuiTreeNodeFlags_DefaultOpen)) {
                    field("LfsExternalStoragePath","External LFS storage directory");field("LfsManagementArchivePath","Verified archive directory");field("LfsCleanupRemoteName","Verification remote");
                    field("LfsVerifyRemote","Verify remote copy");field("LfsRemoteVerificationTimeoutSeconds","Remote verification time budget (seconds)");
                    field("LfsRequiredCopies","Required verified copies");field("LfsCleanupGraceDays","Grace period (days)");field("LfsPeerProofMaximumAgeHours","Peer proof maximum age (hours)");
                    field("LfsTrimRemoteBackedHistory","Trim old remote-backed asset versions (opt-in)");
                    field("LfsRecentVersionsPerFile","Keep recent versions per file");field("LfsRecentVersionExtensions","Asset types (for example .uasset;.umap)");
                    command("SaveLfsManagementAsync","Save policy");
                }
                cyui::textDim("%s",value("LfsStorageSummary").c_str());
                {
                    cyui::ScopedDisabled disabled(busy() || flag("IsLfsAnalysisRunning"));
                    if(cyui::button("Preview git lfs prune")) send({{"command","gitLfsPrune"},{"dryRun",true}});
                    nextButton(); if(cyui::dangerButton("Run verified git lfs prune...")) invoke(customAction("RunNativeLfsPruneAsync","Run git lfs prune after remote verification","gitLfsPrune",{{"dryRun",false}},true));
                    nextButton(); command("AnalyzeLfsStorageAsync","Advanced analysis");
                }
                if(flag("IsLfsAnalysisRunning")) {
                    if(cyui::button("Cancel analysis")) send({{"command","cancel"}});
                    float percent=0;try {percent=std::stof(value("LfsAnalysisPercent","0"))/100.0f;} catch(...) {}
                    cyui::progressBar(percent,ImVec2(-1,cyui::dp(7)),value("LfsAnalysisStage").c_str());
                }
                command("ArchiveLfsCandidatesAsync","Archive verified candidates");nextButton();command("ExecuteLfsCleanupAsync","Clean verified objects...");
                if(ImGui::CollapsingHeader("Repository disk usage",ImGuiTreeNodeFlags_DefaultOpen)) {
                    cyui::textDim("%s",value("LfsRepositorySizeSummary").c_str());command("AnalyzeRepositoryStorageAsync","Quick size scan");
                    if(splitBegin("##lfs-disk-usage",0.38f)) {list("LfsStorageAreas",cyui::dp(0,200));ImGui::TableNextColumn();list("LfsLargestFiles",cyui::dp(0,200));ImGui::EndTable();}
                }
                if(ImGui::CollapsingHeader("Object decisions and verification",ImGuiTreeNodeFlags_DefaultOpen)) {
                    list("LfsCleanupItems",cyui::dp(0,250));
                    cyui::sectionTitle("Remote verification report");ImGui::BeginChild("##lfs-verification-report",cyui::dp(0,180));textPanel("LfsRemoteVerification","lfs-remote-verification");ImGui::EndChild();
                }
                field("LfsRemoveOriginalAfterRelocation","After successful verification, remove the old object cache");
                command("RelocateLfsStorageAsync","Relocate and activate external storage...");
                ImGui::EndChild();ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
    } else return false;
    return true;
}

void releaseGitViewState(const Workspace* workspace,const std::string& scope){gitStates.erase({workspace,scope});}
} // namespace cyrevision
