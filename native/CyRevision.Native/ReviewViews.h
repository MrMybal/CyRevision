#pragma once
#include "Workspace.h"
#include <algorithm>
#include <cctype>

namespace cyrevision {
struct ReviewUiState {
    std::string project;
    bool ciLogWindow=false, prLogWindow=false, prPatchWindow=false, taskPicker=false;
    bool ciLogTopmost=false, prLogTopmost=false;
    bool prefixTaskTitle=true;
    float prSplit=.36f, fileSplit=.42f, ciRunsSplit=.49f;
    std::string provider, taskQuery, taskBaseUrl, taskScope, taskAccount, taskTokenEnvironment, taskSessionToken;
    std::string loadedTaskProvider;
};
inline ReviewUiState& reviewUi(const std::string& project) {
    static ReviewUiState ui;
    if(ui.project!=project) { ui=ReviewUiState{}; ui.project=project; }
    return ui;
}
inline const Json& reviewContext(const Json& state) {
    static const Json empty=Json::object();
    auto found=state.find("pullRequestContext");
    return found!=state.end() && found->is_object() ? *found : empty;
}
inline const Json& reviewObject(const Json& parent,const char* key) {
    static const Json empty=Json::object();
    auto found=parent.find(key);
    return found!=parent.end() && found->is_object() ? *found : empty;
}
inline Json reviewRpc(const char* label,const char* rpc,const Json& context,const char* warning=nullptr,bool confirm=true) {
    Json result={{"name",rpc},{"label",label},{"rpc",rpc},{"context",context},{"confirmation",confirm},{"parameters",Json::array()}};
    if(warning) result["description"]=warning;
    return result;
}
inline float reviewSplitWidth(const char* id,float& ratio,float available,float minimum=180.0f) {
    float grip=cyui::dp(7);
    float min=std::min(cyui::dp(minimum),available*.42f);
    float width=std::clamp(available*ratio,min,std::max(min,available-min-grip));
    ImVec2 start=ImGui::GetCursorScreenPos();
    ImGui::SetCursorScreenPos(ImVec2(start.x+width,start.y));
    ImGui::InvisibleButton(id,ImVec2(grip,std::max(cyui::dp(32),ImGui::GetContentRegionAvail().y)));
    bool active=ImGui::IsItemActive();
    if(ImGui::IsItemHovered() || active) ImGui::SetMouseCursor(ImGuiMouseCursor_ResizeEW);
    if(active && available>0) ratio=std::clamp(ratio+ImGui::GetIO().MouseDelta.x/available,.12f,.88f);
    ImGui::GetWindowDrawList()->AddRectFilled(ImVec2(start.x+width,start.y),ImVec2(start.x+width+grip,start.y+ImGui::GetItemRectSize().y),active?cyui::palette().lineStrong:cyui::palette().line);
    ImGui::SetCursorScreenPos(start);
    return width;
}
inline void reviewStatus(const std::string& status) {
    std::string normalized=status;
    std::transform(normalized.begin(),normalized.end(),normalized.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});
    auto tone=normalized=="success" || normalized=="open" || normalized=="mergeable" ? cyui::Tone::success
        : normalized=="failure" || normalized=="closed" || normalized=="conflicts" || normalized=="timed_out" ? cyui::Tone::error
        : normalized=="queued" || normalized=="draft" || normalized=="action_required" ? cyui::Tone::warning
        : normalized=="merged" || normalized=="mine" ? cyui::Tone::accent : cyui::Tone::info;
    cyui::badge(status.empty()?"UNKNOWN":status.c_str(),tone);
}
}
