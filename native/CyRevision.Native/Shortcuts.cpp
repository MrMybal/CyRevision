#include "Workspace.h"
#include <imgui_internal.h>
#include <algorithm>
#include <sstream>
#include <map>
namespace cyrevision {
namespace {
ImGuiKey shortcutKey(std::string name){
    if(name.size()==2 && name[0]=='D' && name[1]>='0' && name[1]<='9')return static_cast<ImGuiKey>(ImGuiKey_0+name[1]-'0');
    if(name.size()==7 && name.substr(0,6)=="NumPad" && name[6]>='0' && name[6]<='9')return static_cast<ImGuiKey>(ImGuiKey_Keypad0+name[6]-'0');
    if(name.size()==1 && name[0]>='A' && name[0]<='Z')return static_cast<ImGuiKey>(ImGuiKey_A+name[0]-'A');
    if(name.size()==1 && name[0]>='0' && name[0]<='9')return static_cast<ImGuiKey>(ImGuiKey_0+name[0]-'0');
    if(name.size()>1 && name[0]=='F'){try{int number=std::stoi(name.substr(1));if(number>=1 && number<=24)return static_cast<ImGuiKey>(ImGuiKey_F1+number-1);}catch(...) {}}
    static const std::map<std::string,ImGuiKey> named={{"Tab",ImGuiKey_Tab},{"Enter",ImGuiKey_Enter},{"Return",ImGuiKey_Enter},{"Escape",ImGuiKey_Escape},{"Esc",ImGuiKey_Escape},{"Space",ImGuiKey_Space},{"Back",ImGuiKey_Backspace},{"Backspace",ImGuiKey_Backspace},{"Delete",ImGuiKey_Delete},{"Insert",ImGuiKey_Insert},{"Home",ImGuiKey_Home},{"End",ImGuiKey_End},{"PageUp",ImGuiKey_PageUp},{"PageDown",ImGuiKey_PageDown},{"Left",ImGuiKey_LeftArrow},{"Right",ImGuiKey_RightArrow},{"Up",ImGuiKey_UpArrow},{"Down",ImGuiKey_DownArrow},{"OemComma",ImGuiKey_Comma},{",",ImGuiKey_Comma},{"OemPeriod",ImGuiKey_Period},{".",ImGuiKey_Period},{"OemMinus",ImGuiKey_Minus},{"OemPlus",ImGuiKey_Equal},{"OemQuestion",ImGuiKey_Slash},{"OemSemicolon",ImGuiKey_Semicolon}};
    auto found=named.find(name);return found==named.end()?ImGuiKey_None:found->second;
}
bool pressed(const std::string& gesture){bool ctrl=false,shift=false,alt=false,super=false;ImGuiKey key=ImGuiKey_None;std::istringstream parts(gesture);std::string part;while(std::getline(parts,part,'+')){part.erase(std::remove(part.begin(),part.end(),' '),part.end());if(part=="Ctrl" || part=="Control")ctrl=true;else if(part=="Shift")shift=true;else if(part=="Alt")alt=true;else if(part=="Meta" || part=="Win" || part=="Windows" || part=="Cmd")super=true;else key=shortcutKey(part);}auto& io=ImGui::GetIO();return key!=ImGuiKey_None && io.KeyCtrl==ctrl && io.KeyShift==shift && io.KeyAlt==alt && io.KeySuper==super && ImGui::IsKeyPressed(key,false);}
}
void Workspace::shortcuts(){
    if(ImGui::GetTopMostPopupModal() || requestClose)return;
    const auto savedWorkspace=workspace_,savedPane=activePane_;
    if(auto* focused=ImGui::GetCurrentContext()->NavWindow)for(const auto& pane:detachedPanes_)if(pane.open){auto* window=ImGui::FindWindowByName(("###Detached-"+std::to_string(pane.id)).c_str());if(window && focused->RootWindow==window->RootWindow){workspace_=pane.workspace;activePane_="view-"+std::to_string(pane.id);break;}}
    Json bindings={{"preferences","Ctrl+,"},{"add-folder","Ctrl+Shift+O"},{"global-search","Ctrl+Shift+F"},{"refresh","F5"},{"open-repository","Ctrl+O"},{"clone-repository","Ctrl+Shift+N"},{"create-repository","Ctrl+N"},{"changes","Ctrl+1"},{"history","Ctrl+2"},{"branches","Ctrl+3"},{"pull-requests","Ctrl+4"},{"fetch","Ctrl+Alt+F"},{"pull","Ctrl+Alt+L"},{"push","Ctrl+Alt+P"},{"exit","Ctrl+Q"}};
    if(state_.contains("preferences") && state_["preferences"].is_object()){auto prefs=state_["preferences"].value("application",Json::object());if(prefs.contains("keyboardShortcuts"))for(auto& item:prefs["keyboardShortcuts"].items())bindings[item.key()]=item.value();}
    for(auto& binding:bindings.items())if(pressed(display(binding.value()))){auto id=binding.key();if(id=="preferences")showSettings_=true;else if(id=="exit")requestClose=true;else if(!busy()){
        if(id=="create-repository")openGitInitialization();else if(id=="global-search")navigate("search");else if(id=="changes" || id=="history" || id=="branches")navigate(id);else if(id=="pull-requests")navigate("pullrequests");
        else{std::string name=id=="add-folder"?"AddFolderProjectAsync":id=="open-repository"?"AddExistingRepositoryAsync":id=="clone-repository"?"CloneGitRepositoryAsync":id=="fetch"?"FetchAsync":id=="pull"?"PullAsync":id=="push"?"PushAsync":"RefreshAsync";if((id!="fetch" && id!="pull" && id!="push") || flag("AreGitActionsEnabled"))invoke(action(name));}
    }break;}
    workspace_=savedWorkspace;activePane_=savedPane;
}
}
