#include "Workspace.h"
#include "StringInput.h"
#include "Assets.h"
#include <algorithm>
#include <cctype>
#include <sstream>

namespace cyrevision {
namespace {
const Json empty=Json::object();
std::unordered_map<std::string,int> diffModes,diffHunks;
std::string words(const std::string& name){std::string out;for(size_t i=0;i<name.size();++i){if(i && std::isupper(static_cast<unsigned char>(name[i])) && std::islower(static_cast<unsigned char>(name[i-1])))out+=' ';out+=name[i];}return out;}
bool matches(std::string input,std::string query){std::transform(input.begin(),input.end(),input.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});std::transform(query.begin(),query.end(),query.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});return input.find(query)!=std::string::npos;}
}
std::string Workspace::value(const std::string& name,const std::string& fallback)const{
    requestedFields_.insert(name);
    if(draftValues_.contains(name))return display(draftValues_[name]);
    if(state_.contains("fields") && state_["fields"].contains(name))return display(state_["fields"][name].value("value",Json()));
    return fallback;
}
bool Workspace::flag(const std::string& name,bool fallback)const{
    requestedFields_.insert(name);
    if(draftValues_.contains(name) && draftValues_[name].is_boolean())return draftValues_[name].get<bool>();
    if(state_.contains("fields") && state_["fields"].contains(name)){
        auto item=state_["fields"][name].value("value",Json());if(item.is_boolean())return item.get<bool>();
    }return fallback;
}
void Workspace::field(const char* name,const char* label,bool multiline){
    requestedFields_.insert(name);cyui::ScopedID id(name);
    const auto& fields=state_.contains("fields")?state_["fields"]:empty;
    const auto& item=fields.contains(name)?fields[name]:empty;
    if(!cyui::beginProperties("##field",190.0f))return;
    cyui::property(label?label:words(name).c_str());
    if(item.empty()){cyui::textFaint("Loading…");cyui::endProperties();return;}
    auto current=item.value("value",Json());bool editable=item.value("editable",false);
    cyui::ScopedDisabled disabled(busy() || !editable);
    if(text(item,"kind")=="bool"){
        bool checked=flag(name);if(cyui::toggleSwitch("##value",&checked)){draftValues_[name]=checked;send({{"command","set"},{"property",name},{"value",checked}});lastPoll_=0;}
    }else if(text(item,"kind")=="enum"){
        auto selected=value(name);if(ImGui::BeginCombo("##value",selected.c_str())){for(auto& option:item.value("options",Json::array())){auto choice=display(option);if(ImGui::Selectable(choice.c_str(),choice==selected)){draftValues_[name]=choice;send({{"command","set"},{"property",name},{"value",choice}});lastPoll_=0;}}ImGui::EndCombo();}
    }else if(!editable){ImGui::TextWrapped("%s",display(current).c_str());}
    else{
        auto found=edits_.find(name);if(found==edits_.end())found=edits_.emplace(name,display(current)).first;
        auto flags=item.value("secret",false)?ImGuiInputTextFlags_Password:ImGuiInputTextFlags_None;
        bool changed=multiline?ImGui::InputTextMultiline("##value",&found->second,ImVec2(-1,cyui::dp(84)),flags):ImGui::InputText("##value",&found->second,flags);
        if(changed)draftValues_[name]=found->second;
        if(ImGui::IsItemDeactivatedAfterEdit()){send({{"command","set"},{"property",name},{"value",found->second}});lastPoll_=0;}
    }
    cyui::tooltip(label?label:words(name).c_str());cyui::endProperties();
}
void Workspace::requestTable(const char* source,int take,int skip,const std::string& filter,const std::string& slot){
    auto key=slot.empty()?tableKey(source):slot;auto& view=tableViews_[key];
    requestedTables_[key]={{"source",source},{"key",key},{"take",take},{"skip",skip},{"filter",filter},{"sort",view.sort},{"descending",view.descending},{"selection",view.selection}};
    const auto& snapshot=viewTable(source,key);bool unavailable=state_.contains("tables") && state_["tables"].contains(key) && state_["tables"][key].is_null();
    if(!unavailable && (snapshot.empty() || snapshot.value("filter",filter)!=filter || snapshot.value("sort",view.sort)!=view.sort || snapshot.value("descending",view.descending)!=view.descending || snapshot.value("skip",skip)!=skip))lastPoll_=0;
}
void Workspace::selector(const char* source,const char* selection,const char* label){
    // A picker and a list can read the same source with different destinations.
    // Never change the list's selection binding from a separate picker.
    cyui::ScopedID id(selection);auto slot=tableKey(source)+":picker:"+selection;tableViews_[slot].selection=selection;requestTable(source,200,0,"",slot);
    requestedFields_.insert(selection);
    const auto& table=viewTable(source,slot);
    std::string selected,key;
    if(state_.contains("selections") && state_["selections"].contains(selection)){selected=text(state_["selections"][selection],"text");key=text(state_["selections"][selection],"key");}
    if(!cyui::beginProperties("##selection",190.0f))return;
    cyui::property(label?label:words(selection).c_str());
    cyui::ScopedDisabled disabled(!ready() || !client_.alive());
    if(ImGui::BeginCombo("##value",selected.empty()?"Select…":selected.c_str())){
        if(table.contains("rows"))for(const auto& row:table["rows"]){const auto& values=row["values"];std::string title;for(auto property:{"DisplayName","Label","Name","Title","Subject","Value","Path","Id"})if(values.contains(property)){title=display(values[property]);break;}if(values.contains("ShortHash"))title=display(values["ShortHash"])+" · "+title;if(title.empty())title="Item "+display(row["index"]);
            auto rowKey=text(row,"key");cyui::ScopedID rowId(rowKey.c_str());
            if(ImGui::Selectable(title.c_str(),rowKey==key)){send({{"command","select"},{"source",source},{"selection",selection},{"index",row["index"]},{"key",rowKey}});lastPoll_=0;}
        }
        if(table.value("total",0)>200)cyui::textDim("First 200 options shown; filter the source list for more.");
        ImGui::EndCombo();
    }cyui::endProperties();
}
bool Workspace::activityButton(const char* label,bool running,bool primary){
    std::string caption=(running?"    ":"")+std::string(label)+"###command";
    bool clicked=cyui::button(caption.c_str(),primary?cyui::Variant::primary:cyui::Variant::secondary,ImVec2(cyui::buttonWidth(label)+cyui::dp(18),0));
    if(running){auto min=ImGui::GetItemRectMin(),max=ImGui::GetItemRectMax();auto* draw=ImGui::GetWindowDrawList();float radius=cyui::dp(4),angle=static_cast<float>(ImGui::GetTime())*6;
        draw->PathArcTo(ImVec2(min.x+cyui::dp(9),(min.y+max.y)*.5f),radius,angle,angle+4.5f,16);draw->PathStroke(primary?cyui::palette().accentText:cyui::palette().accent,0,cyui::dp(1.5f));}
    return clicked;
}
void Workspace::loadingIndicator(const std::string& label){
    cyui::spinner("##loading",5);ImGui::SameLine();cyui::textColoured(cyui::palette().accent,"%s",label.c_str());
}
void Workspace::command(const char* name,const char* label,bool primary){
    auto item=action(name);cyui::ScopedID id(name);bool available=!item.value("missing",false);
    std::string method=name;if(method.size()>5 && method.substr(method.size()-5)=="Async")method.resize(method.size()-5);
    auto capability="Can"+method;requestedFields_.insert(capability);
    if(method=="Commit")available=available && flag("CanCommitPreparedChanges");
    if(state_.contains("fields") && state_["fields"].contains(capability))available=available && flag(capability,true);
    if(method=="Fetch" || method=="Pull" || method=="Push")available=available && flag("AreGitActionsEnabled");
    bool cancel=std::string(name).find("Cancel")!=std::string::npos;
    bool running=!cancel && actionRunning(name);
    bool scanBlocked=!cancel && state_.value("projectLoading",false) && item.value("requiresProjectReady",false);
    bool canQueue=method.rfind("Refresh",0)==0;
    cyui::ScopedDisabled disabled(!available || (!cancel && ((busy() && !canQueue) || running || scanBlocked)));
    std::string title=label?label:text(item,"label",name);
    bool clicked=activityButton(title.c_str(),running,primary);
    cyui::tooltip(queuedRefreshes_.count(name)?"Refresh queued for this project until the current operation finishes.":running?"This action is already running. Use Cancel to stop a cancellable operation.":scanBlocked?"Available when the project scan finishes.":!available?"This command is unavailable in the current context.":!cancel && busy()?canQueue?"Queue this refresh after the current operation.":"Another operation is in progress. Its activity is shown above.":text(item,"description",text(item,"label",name)).c_str());
    if(clicked){if(cancel)send({{"command","cancel"}});else invoke(item);}
}
Json Workspace::selectedRows(const std::string& source)const{
    Json rows=Json::array();auto view=tableViews_.find(tableKey(source));if(view==tableViews_.end())return rows;
    for(const auto& key:view->second.selected)if(view->second.remembered.contains(key))rows.push_back(view->second.remembered[key]);
    return rows;
}
void Workspace::list(const char* source,ImVec2 size){
    cyui::ScopedID scope(source);auto& view=tableViews_[tableKey(source)];requestTable(source,view.take,view.skip,view.filter);
    const auto& table=viewTable(source);int total=table.value("total",0);
    if(table.contains("rows") && view.skip>=total && view.skip>0){view.skip=total?((total-1)/view.take)*view.take:0;lastPoll_=0;}
    if(state_.contains("tableSelections") && state_["tableSelections"].contains(tableKey(source))){
        auto selected=text(state_["tableSelections"],tableKey(source).c_str());
        if(selected==view.awaitingSelection)view.awaitingSelection.clear();
        if(!selected.empty() && view.awaitingSelection.empty() && !selectionPending(source) && !view.multiple){view.selected.clear();view.selected.insert(selected);if(table.contains("rows"))for(const auto& row:table["rows"])if(text(row,"key")==selected)view.remembered[selected]=row;}
    }
    bool logs=std::string(source).find("LogLines")!=std::string::npos || std::string(source).find("LogRows")!=std::string::npos;
    ImGui::SetNextItemWidth(cyui::dp(220));if(ImGui::InputTextWithHint("##filter","Search this list…",&view.filter)){view.skip=0;lastPoll_=0;}
    if(!logs){ImGui::SameLine();{cyui::ScopedDisabled off(view.skip==0);if(cyui::ghostButton("‹")){view.skip=std::max(0,view.skip-view.take);lastPoll_=0;}}
    ImGui::SameLine();{cyui::ScopedDisabled off(view.skip+view.take>=total);if(cyui::ghostButton("›")){view.skip+=view.take;lastPoll_=0;}}}
    ImGui::SameLine();cyui::textFaint("%d–%d / %d",total?view.skip+1:0,std::min(view.skip+view.take,total),total);
    ImGui::SameLine();if(cyui::ghostButton("↗###openList"))openDetached(workspace_,"list",source,words(source));cyui::tooltip("Open this list in a separate window. Filters and columns are independent.");
    if(!table.contains("rows") || !table.contains("columns")){if(state_.contains("tables") && state_["tables"].contains(tableKey(source)) && state_["tables"][tableKey(source)].is_null())cyui::textDim("This list is not available in the current view.");else loadingIndicator("Loading list…");return;}
    if(table.value("filter",view.filter)!=view.filter || table.value("sort",view.sort)!=view.sort || table.value("descending",view.descending)!=view.descending || table.value("skip",view.skip)!=view.skip){ImGui::SameLine();loadingIndicator("Updating list…");}
    const auto& rows=table["rows"];const auto& columns=table["columns"];
    if(columns.empty()){cyui::textDim("No items.");return;}
    if(logs)ImGui::PushFont(cyui::monoFont());
    auto flags=ImGuiTableFlags_Resizable|ImGuiTableFlags_Reorderable|ImGuiTableFlags_Hideable|ImGuiTableFlags_Sortable|ImGuiTableFlags_RowBg|ImGuiTableFlags_BordersInnerV|ImGuiTableFlags_ScrollX|ImGuiTableFlags_ScrollY;
    if(size.y==0)size.y=std::max(cyui::dp(90),ImGui::GetContentRegionAvail().y);
    if(ImGui::BeginTable("##list",static_cast<int>(columns.size()),flags,size)){
        for(size_t i=0;i<columns.size();++i){auto name=columns[i].get<std::string>();float width=name=="Text"?900.0f:(name=="Path" || name=="RelativePath" || name=="Message" || name=="Title")?360.0f:name=="Number"?70.0f:150.0f;
            if(logs && name=="Text")for(const auto& row:rows)width=std::max(width,ImGui::CalcTextSize(text(row["values"],"Text").c_str()).x/cyui::dpi()+12.0f);
            if(logs && name=="Text")width=std::max(width,table.value("maxTextColumns",0)*ImGui::CalcTextSize("M").x/cyui::dpi()+12.0f);
            ImGui::TableSetupColumn(words(name).c_str(),ImGuiTableColumnFlags_WidthFixed|(i>7?ImGuiTableColumnFlags_DefaultHide:0),cyui::dp(width),static_cast<ImGuiID>(i+1));
        }ImGui::TableSetupScrollFreeze(0,1);ImGui::TableHeadersRow();
        if(auto specs=ImGui::TableGetSortSpecs();specs && specs->SpecsDirty){if(specs->SpecsCount){view.sort=columns[specs->Specs[0].ColumnIndex].get<std::string>();view.descending=specs->Specs[0].SortDirection==ImGuiSortDirection_Descending;view.skip=0;lastPoll_=0;}specs->SpecsDirty=false;}
        ImGuiListClipper clip;float rowHeight=ImGui::GetTextLineHeight()+2*ImGui::GetStyle().CellPadding.y;clip.Begin(logs?total:static_cast<int>(rows.size()),logs?rowHeight:-1.0f);
        while(clip.Step()){
            if(logs){int target=std::max(0,clip.DisplayStart-16);if(view.skip!=target){view.skip=target;lastPoll_=0;}requestTable(source,200,view.skip,view.filter);}
            for(int visible=clip.DisplayStart;visible<clip.DisplayEnd;++visible){int index=logs?visible-table.value("skip",0):visible;
            if(index<0 || index>=static_cast<int>(rows.size())){ImGui::TableNextRow(0,rowHeight);ImGui::TableSetColumnIndex(0);ImGui::TextUnformatted("…");continue;}
            const auto& row=rows[index];const auto& values=row["values"];auto key=text(row,"key");cyui::ScopedID rowId(key.c_str());ImGui::TableNextRow();bool clicked=false;
            for(size_t column=0;column<columns.size();++column){if(!ImGui::TableSetColumnIndex(static_cast<int>(column)))continue;auto name=columns[column].get<std::string>();auto cell=values.value(name,Json());
                if(column==0){clicked=ImGui::Selectable("##row",view.selected.count(key)!=0,ImGuiSelectableFlags_SpanAllColumns|ImGuiSelectableFlags_AllowOverlap);
                    if(ImGui::BeginPopupContextItem("##context")){
                        if(ImGui::MenuItem("Copy item"))ImGui::SetClipboardText(values.dump(2).c_str());
                        if(std::string(source)=="CiJobs" || std::string(source)=="PullRequestCiJobs")ciJobContext(source,row);
                        if(std::string(source)=="PullRequestCommitRevisions" && ImGui::MenuItem("Inspect this commit…",nullptr,false,!busy())){submitAction(action("SelectExplorerCommitAsync"),{{"commitHash",text(values,"Hash")}},false);navigate("history");}
                        if((std::string(source)=="CodeFileList" || std::string(source)=="CodeFileSearchResults" || std::string(source)=="NativeSolutionTreeRows") && !values.value("IsDirectory",false)){
                            if(ImGui::MenuItem("Git / LFS file history…",nullptr,false,flag("IsGitProject") && !busy())){pendingAction_=true;send({{"command","fileHistory"},{"path",text(values,"RelativePath",text(values,"Path"))}});enlargedText_["File history"]=true;lastPoll_=0;}
                        }ImGui::EndPopup();
                    }ImGui::SameLine(0,0);
                }
                if((name=="IsIncluded" || name=="IsSelected") && cell.is_boolean()){
                    bool checked=cell.get<bool>();cyui::ScopedDisabled disabled(busy());if(ImGui::Checkbox(("##"+name).c_str(),&checked)){send({{"command","itemSet"},{"source",source},{"key",key},{"index",row["index"]},{"property",name},{"value",checked}});lastPoll_=0;}
                }else{
                    auto content=display(cell);ImU32 colour=(name.find("State")!=std::string::npos || name=="Status" || name=="Level")?statusColour(content):cyui::palette().text;
                    if(logs){if(values.value("IsError",false))colour=cyui::palette().error;else if(values.value("IsWarning",false))colour=cyui::palette().warning;}
                    cyui::ScopedColor color(ImGuiCol_Text,colour);
                    if(column==0 && values.contains("Depth")){ImGui::Indent(cyui::dp(values.value("Depth",0)*16.0f));}
                    if(column==0 && std::string(source)=="NativeSolutionTreeRows" && values.value("IsDirectory",false)){
                        if(ImGui::SmallButton(values.value("Expanded",false)?"v":">")){send({{"command","tools.tree.toggle"},{"key",key},{"path",text(values,"RelativePath")}});lastPoll_=0;}ImGui::SameLine();
                    }
                    ImGui::TextUnformatted(content.c_str());if(content.size()>50)cyui::tooltip(content.c_str());
                    if(column==0 && values.contains("Depth"))ImGui::Unindent(cyui::dp(values.value("Depth",0)*16.0f));
                }
            }
            if(clicked){auto& io=ImGui::GetIO();view.multiple=io.KeyCtrl || io.KeyShift;if(!view.multiple){view.selected.clear();view.remembered=Json::object();}if(io.KeyShift && view.anchor>=0){for(int i=std::min(index,view.anchor);i<=std::max(index,view.anchor) && i<static_cast<int>(rows.size());++i){auto rangeKey=text(rows[i],"key");view.selected.insert(rangeKey);view.remembered[rangeKey]=rows[i];}}else if(io.KeyCtrl && view.selected.count(key)){view.selected.erase(key);view.remembered.erase(key);}else{view.selected.insert(key);view.remembered[key]=row;}view.anchor=index;view.awaitingSelection=key;
                if(ready() && client_.alive()){if(std::string(source)=="NativeSolutionTreeRows")send({{"command","tools.tree.select"},{"key",key},{"path",text(values,"RelativePath")}});else send({{"command","select"},{"source",source},{"selection",view.selection},{"key",key},{"index",row["index"]}});lastPoll_=0;}
            }
        }}ImGui::EndTable();
    }if(logs)ImGui::PopFont();
}
void Workspace::textPanel(const char* property,const char* id){
    cyui::ScopedID scope(id);
    if(std::string(property)=="ExplorerDiff" && (selectionPending("History") || selectionPending("ExplorerFiles") || flag("IsExplorerLoading"))){loadingIndicator("Loading selected revision / file…");return;}
    auto content=value(property);auto key=tableKey(id);auto& query=textSearch_[key];
    ImGui::SetNextItemWidth(cyui::dp(260));ImGui::InputTextWithHint("##find","Search text…",&query);ImGui::SameLine();if(cyui::ghostButton("Copy"))ImGui::SetClipboardText(content.c_str());ImGui::SameLine();if(cyui::ghostButton("Enlarge ↗"))openDetached(workspace_,"text",property,words(property));
    if(!query.empty()){std::istringstream stream(content);std::string line,filtered;int number=0;while(std::getline(stream,line)){++number;if(matches(line,query))filtered+=std::to_string(number)+"  "+line+"\n";}content=std::move(filtered);}
    textViews_[key].draw("##content",content);
}
void Workspace::diffPanel(const char* property,const char* id){
    cyui::ScopedID scope(id);
    if(std::string(property)=="ExplorerDiff" && (selectionPending("History") || selectionPending("ExplorerFiles") || flag("IsExplorerLoading"))){loadingIndicator("Loading selected revision / file…");return;}
    if(cyui::ghostButton("Open diff in window ↗"))openDetached(workspace_,"diff",property,"Diff · "+words(property));
    auto key=tableKey(id);int& mode=diffModes[key];const char* options[]={"Unified","Side by side","Images / heatmap"};cyui::segmented("##diffMode",options,3,&mode);
    if(mode==2){auto imageKey=std::string(property);if(state_.contains("images") && state_["images"].contains(imageKey)){const auto& images=state_["images"][imageKey];for(const auto& path:images){auto texture=nativeTexture(display(path));if(texture){float width=ImGui::GetContentRegionAvail().x;float ratio=std::min(1.0f,width/texture->width);ImGui::Image(ImTextureRef(texture->id),ImVec2(texture->width*ratio,texture->height*ratio));}}}else cyui::textDim("No rendered binary image is available for this comparison.");return;}
    auto content=value(property);int count=0;for(size_t at=0;(at=content.find("\n@@",at))!=std::string::npos;at+=3)++count;
    int& hunk=diffHunks[key];if(cyui::ghostButton("‹ Hunk"))hunk=std::max(0,hunk-1);ImGui::SameLine();if(cyui::ghostButton("Hunk ›"))hunk=std::min(count,hunk+1);ImGui::SameLine();cyui::textDim("%d / %d",hunk,count);
    if(hunk>0){size_t start=0;for(int n=0;n<hunk && start!=std::string::npos;++n){start=content.find("\n@@",start+1);}if(start!=std::string::npos)content=content.substr(start+1);}
    if(mode==0 && hunk==0){textPanel(property,id);return;}
    if(mode==0){textViews_[key].draw("##hunk",content);return;}
    std::string before,after,line;std::istringstream stream(content);while(std::getline(stream,line)){if(line.empty() || line[0]!='+')before+=line+"\n";if(line.empty() || line[0]!='-')after+=line+"\n";}
    if(ImGui::BeginTable("##sideBySide",2,ImGuiTableFlags_Resizable|ImGuiTableFlags_BordersInnerV)){ImGui::TableSetupColumn("Before");ImGui::TableSetupColumn("After");ImGui::TableHeadersRow();ImGui::TableNextRow();ImGui::TableNextColumn();textViews_[key+"-before"].draw("##before",before);ImGui::TableNextColumn();textViews_[key+"-after"].draw("##after",after);ImGui::EndTable();}
}
void Workspace::imagePanel(const char* property,const char* id){
    cyui::ScopedID scope(id);auto key=std::string(property);bool shown=false;
    if(cyui::ghostButton("Open preview in window ↗"))openDetached(workspace_,"image",property,"Preview · "+words(property));
    if(state_.contains("images") && state_["images"].contains(key))for(const auto& path:state_["images"][key]){auto image=nativeTexture(display(path));if(image){auto available=ImGui::GetContentRegionAvail();float scale=std::min(1.0f,std::min(available.x/image->width,available.y/image->height));ImGui::Image(ImTextureRef(image->id),ImVec2(image->width*scale,image->height*scale));shown=true;}}
    if(!shown)cyui::textDim("No image preview is available for the selected file.");
}
void Workspace::navigate(const std::string& id){
    if(!activePane_.empty()){for(auto& pane:detachedPanes_)if(activePane_=="view-"+std::to_string(pane.id)){if(pane.workspace!=id){pane.workspace=id;pane.needsLoad=true;pane.title.clear();}workspace_=id;lastPoll_=0;return;}}
    if(workspace_==id)return;workspace_=id;openWorkspace_=true;selectTable_=true;lastPoll_=0;edits_.clear();draftValues_=Json::object();requestedFields_.clear();requestedTables_=Json::object();
    if(state_.contains("navigation"))for(const auto& tab:state_["navigation"].value("tabs",Json::array()))if(text(tab,"id")==id){category_=text(tab,"category");break;}
}
void releaseWidgetViewState(const std::string& prefix){for(auto it=diffModes.begin();it!=diffModes.end();)if(it->first.rfind(prefix,0)==0)it=diffModes.erase(it);else++it;for(auto it=diffHunks.begin();it!=diffHunks.end();)if(it->first.rfind(prefix,0)==0)it=diffHunks.erase(it);else++it;}
}
