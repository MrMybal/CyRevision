#pragma once
#include "Client.h"
#include "TextViewer.h"
#include <cyui/Core.h>
#include <cyui/modules/Inspector.h>
#include <cyui/modules/DocumentViews.h>
#include <cyui/modules/Images.h>
#include <imgui.h>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <deque>
#include <functional>

namespace cyrevision {
std::string text(const Json& object,const char* key,const std::string& fallback="");
std::string display(const Json& value);
ImU32 statusColour(const std::string& value);
class Workspace {
    friend struct WorkspaceTestAccess;
    EngineConnection& client_;
    std::wstring backend_;
    Json catalog_=Json::object(), state_=Json::object(), action_=Json::object();
    std::unordered_map<unsigned long long,std::string> pending_;
    std::unordered_map<unsigned long long,Json> pendingRequests_;
    std::unordered_map<std::string,Json> queuedRefreshes_;
    std::unordered_map<unsigned long long,Json> submittedDrafts_;
    std::unordered_map<std::string,std::string> edits_;
    std::unordered_set<std::string> selected_;
    Json parameters_=Json::object();
    TextViewer preview_, detailView_;
    std::string detail_, workspace_="changes", table_="PreparedChanges", filter_, sort_, searchActions_;
    std::string error_, projectId_, lastProject_;
    std::string queuedProject_,openingProject_;
    bool descending_=false, openWorkspace_=false, layoutChecked_=false, resetLayout_=false, selectTable_=true, showSettings_=false, themeEditor_=false;
    bool requestingState_=false, pendingAction_=false, done_=false;
    bool updateOpen_=false, offerUpdateAfterCheck_=false;
    std::string requestedUpdateRevision_;
    std::string seenUpdateVersion_;
    std::string lastDocumentationLanguage_, lastDocumentationLink_;
    std::function<bool(const std::string&)> documentationOpener_;
    int skip_=0, anchor_=-1;
    unsigned long long lastPoll_=0;
    struct TableView {std::string filter,sort,selection,awaitingSelection;int skip=0,take=200,anchor=-1;bool descending=false,multiple=false;std::unordered_set<std::string> selected;Json remembered=Json::object();};
    std::unordered_map<std::string,TableView> tableViews_;
    mutable std::unordered_set<std::string> requestedFields_;
    Json requestedTables_=Json::object(),draftValues_=Json::object();
    std::unordered_map<std::string,TextViewer> textViews_;
    std::unordered_map<std::string,std::unique_ptr<SourceEditor>> sourceEditors_;
    struct PropertyView {cyui::InspectorState state;Json server;std::uint64_t revision=1;};
    std::unordered_map<std::string,PropertyView> propertyViews_;
    struct DocumentView {cyui::Document document;cyui::DocumentState state;};
    std::unordered_map<std::string,DocumentView> documents_;
    std::unordered_map<std::string,cyui::TranscriptState> transcripts_;
    struct ImageView {cyui::ImageState state;std::string path;ImTextureID texture=0;};
    std::unordered_map<std::string,ImageView> imageViews_;
    struct ConflictDraft {std::string key,project,server,result,pending,retention="30";int skip=0;bool dirty=false,focused=false;} conflictDraft_;
    std::unordered_map<std::string,bool> enlargedText_;
    std::string category_="Overview";
    bool showInspector_=false;
    bool showOperations_=false;
    struct DetachedPane {unsigned long long id=0;std::string workspace,kind,target,title;bool open=true,needsLoad=true,floating=true,focus=true,dock=false,undock=false;};
    std::deque<DetachedPane> detachedPanes_;
    unsigned long long nextPane_=1;
    std::string activePane_,reattachWorkspace_;
    ImGuiID mainDock_=0;
    unsigned long long send(Json request);
    bool busy() const;
    bool actionRunning(const std::string& name) const;
    bool selectionPending(const std::string& source) const;
    bool loading(const std::string& workspace) const;
    std::string loadingLabel(const std::string& workspace) const;
    bool activityButton(const char* label,bool running,bool primary=false);
    void loadingIndicator(const std::string& label);
    void activityStatus(const std::string& label,bool running,float width=-1.0f);
    void textToolbar(TextViewer& viewer,const std::string& content,const char* property,bool diff);
    std::string pendingDocumentLink_;
    void workspaceToolbar();
    bool canSwitchProject() const;
    void invoke(const Json& action);
    void actions();
    void inspector();
    void dataTable();
    void workspaceBody();
    void detachedWindows();
    void loadDetachedWorkspaces();
    void openDetached(const std::string& workspace,const std::string& kind="workspace",const std::string& target="",const std::string& title="");
    void tabDetachMenu(const std::string& workspace);
    bool workspaceAvailable(const std::string& workspace) const;
    std::string workspaceLabel(const std::string& workspace) const;
    std::string tableKey(const std::string& source) const;
    const Json& viewTable(const std::string& source,const std::string& slot="") const;
    void workspacePanel();
    void operations();
    void sidebar();
    void topBar();
    void menu();
    void modals();
    void defaultLayout(ImGuiID id);
    Json action(const std::string& name) const;
    void submitAction(const Json& item,const Json& parameters,bool confirmed);
    std::string value(const std::string& name,const std::string& fallback="") const;
    bool flag(const std::string& name,bool fallback=false) const;
    void field(const char* name,const char* label=nullptr,bool multiline=false);
    void selector(const char* source,const char* selection,const char* label=nullptr);
    void command(const char* actionName,const char* label=nullptr,bool primary=false);
    void list(const char* source,ImVec2 size=ImVec2(0,0));
    void textPanel(const char* propertyName,const char* id);
    void diffPanel(const char* propertyName,const char* id);
    void imagePanel(const char* propertyName,const char* id);
    void imageView(const std::string& path,const char* id,ImVec2 size={});
    void documentPanel(const char* propertyName,const char* id);
    void transcriptPanel(const char* source,const char* id,ImVec2 size={});
    void documentLink(const std::string& link);
    void documentationPanel();
    bool toolColumns(const char* id,float left=0.44f);
    void openDocumentation(const std::string& topic="getting-started",const std::string& anchor="",const std::string& media="");
    SourceEditor& sourceEditor(const std::string& id);
    void navigate(const std::string& workspaceId);
    Json selectedRows(const std::string& source) const;
    void requestTable(const char* source,int take=200,int skip=0,const std::string& filter="",const std::string& slot="");
    bool gitView();
    void gitChangesPanel();
    void gitBranchInspector();
    void gitGraphsPanel();
    void gitLfsFolders();
    bool pullRequestView();
    bool ciView();
    void ciLogPanel(bool pullRequest,bool detached=false);
    void ciJobContext(const std::string& source,const Json& row);
    void workItemPicker();
    void reviewLogWindows();
    bool toolsView();
    bool overviewView();
    void preferences();
    void fileHistoryWindow();
    void shortcuts();
    void openGitInitialization(const std::string& path="",bool lfsOnly=false);
    void gitInitializationWizard();
    void checkForUpdates();
    void updateStatus();
    void gitConflictEditor();
public:
    bool requestClose=false;
    bool requestHide=false;
    explicit Workspace(EngineConnection& client,std::wstring backend,std::function<bool(const std::string&)> documentationOpener={});
    void tick(bool background=false);
    void frame();
    Json desktopPreferences()const{return state_.value("preferences",Json::object()).value("desktop",Json::object());}
    unsigned long long stateGeneration()const{return stateGeneration_;}
    bool done() const { return done_; }
    bool ready() const { return state_.value("ready",false); }
    bool settled() const {return ready() && !busy() && !openWorkspace_;}
    bool captureReady() const{return settled() && !state_.value("projectLoading",false) && !loading(workspace_) && (workspace_!="changes" || !flag("IsWorkingTreeDiffLoading"));}
    bool captureSplitPreview(){auto view=textViews_.find("changes-diff");if(view==textViews_.end() || !view->second.document().diff)return false;view->second.setSplit(true);return true;}
    Json captureSnapshot() const{return {{"workspace",workspace_},{"ready",captureReady()},{"selectedDiffPath",workspace_=="changes"?value("NativeWorkingDiffPath"):""},{"diffCharacters",workspace_=="changes"?value("DiffText").size():0}};}
    bool operationsVisible()const{return showOperations_;}
    void setOperationsVisible(bool visible){showOperations_=visible;}
    void openWorkspaceWindow(const std::string& id){openDetached(id);}
    void showWorkspace(const std::string& id){navigate(id);selectTable_=true;}
    Json windowLayout() const;
    void restoreWindowLayout(const Json& layout);
    bool hasAuxiliaryWindows()const;
    size_t detachedPlatformWindowCount()const;
private:
    unsigned long long stateGeneration_=0;
};
}
