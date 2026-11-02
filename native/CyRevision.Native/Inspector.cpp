#include "Workspace.h"
#include "StringInput.h"
#include <shobjidl.h>
#include <shellapi.h>
#include <wrl/client.h>
#include <algorithm>

namespace cyrevision {
static std::string browseFolder() {
    Microsoft::WRL::ComPtr<IFileOpenDialog> dialog;
    if(FAILED(CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog)))) return {};
    DWORD options{};
    dialog->GetOptions(&options);
    dialog->SetOptions(options|FOS_PICKFOLDERS|FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST);
    if(FAILED(dialog->Show(GetActiveWindow()))) return {};
    Microsoft::WRL::ComPtr<IShellItem> item;
    if(FAILED(dialog->GetResult(&item))) return {};
    PWSTR path{};
    if(FAILED(item->GetDisplayName(SIGDN_FILESYSPATH,&path))) return {};
    auto result=utf8(path);CoTaskMemFree(path);
    return result;
}
static bool contains(const std::string& value,const std::string& query) {
    std::string first=value,second=query;
    std::transform(first.begin(),first.end(),first.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});
    std::transform(second.begin(),second.end(),second.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});
    return first.find(second)!=std::string::npos;
}
void Workspace::actions() {
    ImGui::InputTextWithHint("##actions","Find an action...",&searchActions_);
    if(!state_.contains("actions")) return;
    for(const auto& item:state_["actions"]) {
        auto title=text(item,"label");
        if(!contains(title,searchActions_)) continue;
        cyui::ScopedID id(text(item,"name").c_str());
        cyui::ScopedDisabled disabled(busy());
        if(item.value("confirmation",false)) {
            if(cyui::dangerButton(title.c_str(),ImVec2(-1,0))) invoke(item);
        } else if(cyui::button(title.c_str(),cyui::Variant::secondary,ImVec2(-1,0))) invoke(item);
    }
}
void Workspace::inspector() {
    if(ImGui::Begin("Inspector")) {
        if(ImGui::BeginTabBar("##inspectorTabs")) {
            if(ImGui::BeginTabItem("Preview")) {
                if(ImGui::BeginTabBar("##previewTabs")) {
                    if(ImGui::BeginTabItem("File / diff")) {
                        auto content=text(state_,"preview");
                        if(content.empty()) cyui::emptyState("No preview","Select a file or revision in the workspace.",cyui::icon::document);
                        else preview_.draw("##previewText",content);
                        ImGui::EndTabItem();
                    }
                    if(ImGui::BeginTabItem("Selected item")) {
                        detailView_.draw("##detail",detail_);
                        ImGui::EndTabItem();
                    }
                    ImGui::EndTabBar();
                }
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Configuration")) {
                ImGui::BeginChild("##configuration");
                if(state_.contains("properties"))for(const auto& property:state_["properties"]){
                    auto name=text(property,"name");if(name=="CommitMessage" && workspace_=="changes")continue;
                    auto current=property.value("value",Json());if(!property.value("editable",false) && (current.is_null() || display(current).empty()))continue;
                    field(name.c_str(),text(property,"label").c_str());
                }
                ImGui::EndChild();
                ImGui::EndTabItem();
            }
            if(ImGui::BeginTabItem("Actions")) {
                ImGui::BeginChild("##actionList");actions();ImGui::EndChild();
                ImGui::EndTabItem();
            }
            ImGui::EndTabBar();
        }
    }
    ImGui::End();
}
void Workspace::modals() {
    if(requestClose)pendingDocumentLink_.clear();
    bool linkOpen=!pendingDocumentLink_.empty() && action_.empty() && !requestClose;
    if(linkOpen && cyui::beginModal(cyui::trLabel("Open external link?").c_str(),&linkOpen,540)){
        ImGui::TextWrapped("%s",pendingDocumentLink_.c_str());
        auto result=cyui::modalButtons(cyui::trLabel("Open in browser").c_str(),cyui::trLabel("Cancel").c_str());
        if(result==1){ShellExecuteW(nullptr,L"open",wide(pendingDocumentLink_).c_str(),nullptr,nullptr,SW_SHOWNORMAL);pendingDocumentLink_.clear();}
        else if(result==2)pendingDocumentLink_.clear();
        cyui::endModal();
    }
    if(!linkOpen && action_.empty())pendingDocumentLink_.clear();
    if(requestClose && state_.contains("preferences") && state_["preferences"].is_object() && !state_["preferences"].value("application",Json::object()).value("confirmBeforeExit",true)){done_=true;requestClose=false;return;}
    // CyUI opens a modal whenever its flag is true. Derive it from an actual
    // request: unconditional flags create competing, invisible input blockers.
    // Quit takes priority and discards an unsubmitted action form.
    if(requestClose) { action_=Json::object();parameters_=Json::object(); }
    bool open=!action_.empty();
    if(open && cyui::beginModal("Action",&open,580.0f)) {
        cyui::heading(text(action_,"label","Action").c_str(),2);
        if(!text(action_,"description").empty())ImGui::TextWrapped("%s",text(action_,"description").c_str());
        if(action_.value("confirmation",false))
            cyui::callout(cyui::Tone::warning,"This action changes project files or references. Confirm the selected item and parameters.");
        if(cyui::beginProperties("##parameters",170.0f)) {
            for(const auto& parameter:action_.value("parameters",Json::array())) {
                auto name=text(parameter,"name");cyui::ScopedID scope(name.c_str());
                cyui::property(text(parameter,"label").c_str());
                if(text(parameter,"kind")=="bool") {
                    bool value=parameters_.value(name,false);
                    if(cyui::toggleSwitch("##value",&value)) parameters_[name]=value;
                } else if(text(parameter,"kind")=="enum") {
                    auto selected=display(parameters_.value(name,Json()));
                    if(ImGui::BeginCombo("##value",selected.c_str())) {
                        for(const auto& option:parameter.value("options",Json::array())) {
                            auto choice=option.get<std::string>();
                            if(ImGui::Selectable(choice.c_str(),choice==selected)) parameters_[name]=choice;
                        }
                        ImGui::EndCombo();
                    }
                } else {
                    auto key="parameter:"+name;
                    auto found=edits_.find(key);
                    if(found==edits_.end()) found=edits_.emplace(key,display(parameters_.value(name,Json()))).first;
                    if(ImGui::InputText("##value",&found->second)) parameters_[name]=found->second;
                    if(contains(name,"path") || contains(name,"directory") || name=="path") {
                        ImGui::SameLine();
                        if(cyui::ghostButton("...")) {
                            auto folder=browseFolder();
                            if(!folder.empty()) { found->second=folder;parameters_[name]=folder; }
                        }
                    }
                }
            }
            cyui::endProperties();
        }
        auto result=cyui::modalButtons(action_.value("confirmation",false) ? "Confirm" : "Run","Cancel",action_.value("confirmation",false));
        if(result==1) {
            pendingAction_=true;
            submitAction(action_,parameters_,true);
            action_=Json::object();ImGui::CloseCurrentPopup();
        } else if(result==2) {action_=Json::object();ImGui::CloseCurrentPopup();}
        cyui::endModal();
    }
    if(!open) action_=Json::object();
    bool quitOpen=requestClose;
    if(quitOpen && cyui::beginModal("Quit CyRevision?",&quitOpen,490.0f)) {
        ImGui::TextWrapped("%s",busy() ? "An operation is active. Quit CyRevision and stop its background services?" : "Close CyRevision and its background services?");
        auto result=cyui::modalButtons("Quit","Cancel");
        if(result==1) {done_=true;requestClose=false;ImGui::CloseCurrentPopup();}
        else if(result==2) {requestClose=false;ImGui::CloseCurrentPopup();}
        cyui::endModal();
    }
    if(!quitOpen) requestClose=false;
}
}
