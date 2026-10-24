#include "Workspace.h"
#include "StringInput.h"
#include <shobjidl.h>
#include <wrl/client.h>

namespace cyrevision {
namespace {
std::string choosePreferencesFolder() {
    Microsoft::WRL::ComPtr<IFileOpenDialog> dialog;
    if(FAILED(CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog))))return {};
    DWORD flags{};dialog->GetOptions(&flags);dialog->SetOptions(flags|FOS_PICKFOLDERS|FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST|FOS_NOCHANGEDIR);
    dialog->SetTitle(L"Choose the dedicated CyRevision cache folder");
    if(FAILED(dialog->Show(GetActiveWindow())))return {};
    Microsoft::WRL::ComPtr<IShellItem> item;if(FAILED(dialog->GetResult(&item)))return {};
    PWSTR path{};if(FAILED(item->GetDisplayName(SIGDN_FILESYSPATH,&path)))return {};
    auto result=utf8(path);CoTaskMemFree(path);return result;
}
}

void Workspace::preferences() {
    ImGui::SetNextWindowSize(cyui::dp(930,740),ImGuiCond_FirstUseEver);
    bool open=showSettings_;
    bool visible=ImGui::Begin("Preferences",&open,ImGuiWindowFlags_NoCollapse);
    auto& encoded=edits_["prefs.draft"];
    auto snapshot=state_.value("preferences",Json::object());
    if(!snapshot.is_object())snapshot=Json::object();
    const auto revision=std::to_string(snapshot.value("revision",0LL));
    auto initialise=[&]() {
        encoded=Json{{"application",snapshot.value("application",Json::object())},{"desktop",snapshot.value("desktop",Json::object())}}.dump();
        edits_["prefs.revision"]=revision;edits_["prefs.initialTheme"]=cyui::currentThemeKey();
        edits_["prefs.initialDensity"]=cyui::density()==cyui::Density::comfortable ? "comfortable" : "compact";
    };
    if(!snapshot.empty() && (encoded.empty() || edits_["prefs.revision"]!=revision)) {
        bool saved=!encoded.empty();initialise();if(saved)cyui::notify("Preferences saved",cyui::Tone::success);
    }
    Json draft=Json::parse(encoded,nullptr,false);
    if(!draft.is_object())draft=Json{{"application",Json::object()},{"desktop",Json::object()}};
    auto& app=draft["application"];auto& desktop=draft["desktop"];
    auto capabilities=snapshot.value("capabilities",Json::object());
    bool cacheBusy=snapshot.value("busy",false);
    auto check=[&](Json& object,const char* name,const char* label,bool enabled=true) {
        cyui::ScopedID id(name);
        if(cyui::beginProperties("##check",280.0f)) {
            cyui::property(label);bool checked=object.value(name,false);cyui::ScopedDisabled disabled(!enabled || busy());
            if(cyui::toggleSwitch("##value",&checked))object[name]=checked;
            cyui::endProperties();
        }
    };
    auto input=[&](Json& object,const char* name,const char* label) {
        cyui::ScopedID id(name);
        if(cyui::beginProperties("##input",220.0f)) {
            cyui::property(label);std::string content=text(object,name);cyui::ScopedDisabled disabled(busy());
            if(ImGui::InputText("##value",&content))object[name]=content;
            cyui::endProperties();
        }
    };
    auto revertAppearance=[&]() {
        auto theme=edits_["prefs.initialTheme"];if(!theme.empty())cyui::setTheme(theme.c_str());
        cyui::setDensity(edits_["prefs.initialDensity"]=="comfortable"?cyui::Density::comfortable:cyui::Density::compact);
    };
    if(visible) {
        cyui::heading("Application preferences",2);
        cyui::textDim("Application behavior, project defaults, cache, colors and keyboard shortcuts are saved for this computer.");
        if(snapshot.empty()) {
            cyui::progressBar(-1.0f,cyui::dp(-1,6),"Loading preferences",cyui::Tone::accent);
            if(edits_["prefs.requested"]!="1") {send({{"command","prefs.get"}});edits_["prefs.requested"]="1";lastPoll_=0;}
        } else {
            float footer=cyui::dp(48);
            ImGui::BeginChild("##preferencePages",ImVec2(0,-footer));
            if(ImGui::BeginTabBar("##preferenceTabs")) {
                if(ImGui::BeginTabItem("General")) {
                    ImGui::BeginChild("##general");cyui::sectionTitle("Application");
                    check(app,"confirmBeforeExit","Confirm before quitting");check(app,"automaticRepositoryRefresh","Automatic repository refresh");
                    cyui::textDim("Automatic refresh is debounced and excludes generated build/cache folders.");
                    cyui::sectionTitle("Desktop integration");
                    check(desktop,"launchAtLogin","Launch at system login");
                    check(desktop,"showTrayIcon","Show system tray icon",capabilities.value("nativeTray",false));
                    check(desktop,"startHiddenAtLogin","Start hidden at login",capabilities.value("startHidden",false));
                    check(desktop,"closeToTray","Close main window to tray",capabilities.value("closeToTray",false));
                    if(capabilities.value("nativeTray",false))cyui::callout(cyui::Tone::info,"Closing the main window keeps CyRevision and its services running in the tray. Use the tray menu to reopen the window or quit completely.");
                    ImGui::EndChild();ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Project defaults")) {
                    ImGui::BeginChild("##projectDefaults");cyui::sectionTitle("Default tab layout by project mode");
                    cyui::textDim("Used only when a project is first added; each project can still customize its visible tabs.");
                    if(!app.contains("defaultWorkspacePresetByMode") || !app["defaultWorkspacePresetByMode"].is_object())app["defaultWorkspacePresetByMode"]=Json::object();
                    auto& presets=app["defaultWorkspacePresetByMode"];
                    for(const auto& mode:snapshot.value("modes",Json::array())) {
                        auto id=text(mode,"id");cyui::ScopedID row(id.c_str());
                        if(cyui::beginProperties("##mode",240.0f)) {
                            cyui::property(text(mode,"name",id).c_str(),text(mode,"description").c_str());auto selected=text(presets,id.c_str(),"Full workspace");
                            if(ImGui::BeginCombo("##preset",selected.c_str())) {
                                for(const auto& option:snapshot.value("workspacePresetNames",Json::array())) {
                                    auto name=display(option);if(ImGui::Selectable(name.c_str(),selected==name))presets[id]=name;
                                }ImGui::EndCombo();
                            }cyui::endProperties();
                        }
                    }ImGui::EndChild();ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Notifications")) {
                    ImGui::BeginChild("##notificationPreferences");cyui::sectionTitle("Project defaults");
                    check(app,"defaultProjectNotificationsEnabled","Enable notifications for new projects");
                    cyui::textDim("Existing projects keep their own Overview / Notifications switch.");
                    cyui::sectionTitle("Completed operations");check(app,"notifyOnFailures","Notify failed operations");check(app,"notifyOnWarnings","Notify warnings / cancellations");check(app,"notifyOnSuccesses","Notify successful important operations");
                    cyui::textDim("The activity center always keeps running tasks. These preferences only control promoted notifications.");
                    ImGui::EndChild();ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Storage")) {
                    ImGui::BeginChild("##storagePreferences");cyui::sectionTitle("Active cache");
                    auto active=text(snapshot,"activeCacheDirectory");ImGui::TextWrapped("%s",active.c_str());
                    auto usage=snapshot.value("cacheUsage",Json());if(usage.is_object())cyui::heading(text(usage,"summary").c_str(),3);
                    auto cacheStatus=text(snapshot,"cacheStatus");if(!cacheStatus.empty())ImGui::TextWrapped("%s",cacheStatus.c_str());
                    if(cacheBusy) {
                        cyui::progressBar(-1.0f,cyui::dp(-1,6),"Working on cache",cyui::Tone::accent);
                        if(cyui::button("Cancel cache operation"))send({{"command","prefs.cancel"}});
                    } else {
                        cyui::ScopedDisabled disabled(busy());
                        if(cyui::button("Measure cache")) {pendingAction_=true;send({{"command","prefs.measureCache"}});}
                        ImGui::SameLine();
                        if(cyui::dangerButton("Purge active cache..."))invoke({{"name","prefs.purgeCache"},{"rpc","prefs.purgeCache"},{"label","Purge the active CyRevision cache?"},{"description","Generated previews, inspections and temporary downloads under this exact folder will be removed:\n\n"+active+"\n\nRepository files and persistent application data are not part of this operation."},{"confirmation",true},{"parameters",Json::array()},{"context",{{"expectedRoot",active}}}});
                    }
                    cyui::sectionTitle("Cache location");input(app,"cacheDirectory","Cache directory (blank = default)");
                    {cyui::ScopedDisabled disabled(busy());if(cyui::button("Browse...")){auto path=choosePreferencesFolder();if(!path.empty())app["cacheDirectory"]=path;}ImGui::SameLine();if(cyui::button("Use default"))app["cacheDirectory"]="";}
                    cyui::textDim("Default: %s",text(snapshot,"defaultCacheDirectory").c_str());
                    cyui::callout(cyui::Tone::info,"Changing the path leaves current files untouched. Schedule a verified move to copy and verify them before old cache files are removed at the next launch. Use a dedicated empty folder.");
                    {cyui::ScopedDisabled disabled(busy());if(cyui::button("Schedule cache move...")){
                        auto destination=text(app,"cacheDirectory");if(destination.empty())destination=text(snapshot,"defaultCacheDirectory");
                        invoke({{"name","prefs.scheduleCacheMove"},{"rpc","prefs.scheduleCacheMove"},{"label","Schedule verified cache migration?"},{"description","At the next launch, copy and verify the current cache before removing its old files.\n\nFrom:\n"+active+"\n\nTo:\n"+destination},{"confirmation",true},{"parameters",Json::array()},{"context",{{"expectedRoot",active},{"destination",destination}}}});
                    }}
                    if(snapshot.value("pendingRestart",false))cyui::badge("Cache change applies on next launch",cyui::Tone::warning);
                    ImGui::EndChild();ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Appearance")) {
                    ImGui::BeginChild("##appearancePreferences");cyui::sectionTitle("Native interface theme");
                    if(cyui::beginProperties("##nativeAppearance",180.0f)) {
                        cyui::property("Color preset");
                        if(ImGui::BeginCombo("##nativeTheme",cyui::currentThemeKey().c_str())){
                            for(const auto& theme:cyui::builtInThemes())if(ImGui::Selectable(theme.name.c_str(),cyui::currentThemeKey()==theme.key))cyui::setTheme(theme.key.c_str());
                            ImGui::EndCombo();
                        }
                        cyui::property("Density");bool comfortable=cyui::density()==cyui::Density::comfortable;if(cyui::toggleSwitch("Comfortable",&comfortable))cyui::setDensity(comfortable?cyui::Density::comfortable:cyui::Density::compact);
                        cyui::endProperties();
                    }
                    cyui::textDim("Preview applies immediately. Cancel restores the previous native theme and density.");
                    if(cyui::beginSection("Classic interface compatibility preset",false)) {
                        auto current=text(app,"themePreset");if(ImGui::BeginCombo("##classicTheme",current.c_str())){
                            for(const auto& option:snapshot.value("themePresets",Json::array())){auto id=text(option,"id");if(ImGui::Selectable(text(option,"name").c_str(),id==current))app["themePreset"]=id;}ImGui::EndCombo();
                        }cyui::endSection();
                    }
                    ImGui::EndChild();ImGui::EndTabItem();
                }
                if(ImGui::BeginTabItem("Keyboard shortcuts")) {
                    ImGui::BeginChild("##shortcutPreferences");cyui::textDim("Use Ctrl+O, Ctrl+Shift+F, Alt+1 or F5. Shortcuts must be valid and unique.");
                    if(!app.contains("keyboardShortcuts") || !app["keyboardShortcuts"].is_object())app["keyboardShortcuts"]=Json::object();auto& bindings=app["keyboardShortcuts"];
                    for(const auto& shortcut:snapshot.value("shortcuts",Json::array())) {
                        auto id=text(shortcut,"id");cyui::ScopedID row(id.c_str());
                        if(cyui::beginProperties("##binding",230.0f)){cyui::property(text(shortcut,"name").c_str(),text(shortcut,"description").c_str());std::string gesture=text(bindings,id.c_str(),text(shortcut,"defaultGesture"));if(ImGui::InputText("##gesture",&gesture))bindings[id]=gesture;cyui::endProperties();}
                    }
                    if(cyui::button("Reset shortcut draft"))for(const auto& shortcut:snapshot.value("shortcuts",Json::array()))bindings[text(shortcut,"id")]=text(shortcut,"defaultGesture");
                    ImGui::EndChild();ImGui::EndTabItem();
                }ImGui::EndTabBar();
            }ImGui::EndChild();
            ImGui::Separator();
            {cyui::ScopedDisabled disabled(busy() || cacheBusy);if(cyui::primaryButton("Save preferences")){
                edits_["prefs.initialTheme"]=cyui::currentThemeKey();edits_["prefs.initialDensity"]=cyui::density()==cyui::Density::comfortable?"comfortable":"compact";
                submitAction({{"name","prefs.save"},{"rpc","prefs.save"},{"context",{{"application",app},{"desktop",desktop}}}},Json::object(),false);
            }}ImGui::SameLine();
            if(cyui::button("Cancel")){revertAppearance();open=false;}
        }
    }
    encoded=draft.dump();
    ImGui::End();
    if(!open){revertAppearance();showSettings_=false;edits_.erase("prefs.draft");edits_.erase("prefs.revision");edits_.erase("prefs.requested");}
}
}
