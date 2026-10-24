using System.Text.Json;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;

namespace CyRevision.Desktop.Native;

internal sealed class NativeNavigationState(string? configurationDirectory)
{
    private readonly ProjectWorkspaceStateStore? _store=configurationDirectory is null?null:new(configurationDirectory);
    private readonly Dictionary<Guid,ProjectWorkspaceState> _sessions=[];
    private ProjectWorkspaceState Read(Guid id)=>_sessions.TryGetValue(id,out var state)?state:_store?.Get(id)??new(id,"ProjectWorkspaceTab","Low · 5 min",TabVisibilityPreset:"Full workspace");
    private void Save(ProjectWorkspaceState state){_sessions[state.ProjectId]=state;_store?.Save(state);}
    private static string CategoryKey(string category)=>category switch {"Overview"=>"Overview","Git"=>"Git","Code & Assets"=>"Code","Backup"=>"Backup","Sync"=>"Sync","Team & Network"=>"Network","Extensions & Help"=>"Extensions",_=>"PluginMode"};
    public object Snapshot(MainWindowViewModel model)
    {
        var project=model.SelectedProject;
        var state=project is null?null:Read(project.Id);
        var hidden=state?.HiddenTabs is {} saved?saved.ToHashSet(StringComparer.Ordinal):NativeWorkspacePresets.Hidden(state?.TabVisibilityPreset??"Full workspace");
        var active=NativeWorkspaceCatalog.All.FirstOrDefault(item=>item.LoadTab==state?.ActiveTab && NativeWorkspaceCatalog.IsAvailable(item,model) && !hidden.Contains(item.LoadTab))??NativeWorkspaceCatalog.Find("overview");
        return new {
            activeWorkspace=active.Id,activeCategory=NativeWorkspaceCatalog.CategoryFor(active,model),
            preset=state?.TabVisibilityPreset??"Full workspace",
            presets=NativeWorkspacePresets.Presets.Keys.ToArray(),
            tabs=NativeWorkspaceCatalog.All.Select(item=>new {item.Id,item.Label,item.LoadTab,category=NativeWorkspaceCatalog.CategoryFor(item,model),available=NativeWorkspaceCatalog.IsAvailable(item,model),visible=item.Id is "overview" or "visible" || !hidden.Contains(item.LoadTab)}),
            categoryTabs=state?.CategoryTabs??new Dictionary<string,string>()
        };
    }
    public void Navigate(MainWindowViewModel model,NativeWorkspace workspace)
    {
        if(model.SelectedProject is not {} project)return;
        var old=Read(project.Id);
        var categories=new Dictionary<string,string>(old.CategoryTabs??[]){[CategoryKey(NativeWorkspaceCatalog.CategoryFor(workspace,model))]=workspace.LoadTab};
        Save(old with{ActiveTab=workspace.LoadTab,CategoryTabs=categories});
    }
    public void Update(MainWindowViewModel model,JsonElement request)
    {
        if(model.SelectedProject is not {} project)throw new InvalidOperationException("Select a project first.");
        string operation=request.TryGetProperty("operation",out var op)?op.GetString()??"":"";
        var old=Read(project.Id);var hidden=old.HiddenTabs is {} saved?saved.ToHashSet(StringComparer.Ordinal):NativeWorkspacePresets.Hidden(old.TabVisibilityPreset);
        if(operation=="showAll")hidden.Clear();
        else if(operation=="preset")
        {
            string name=request.GetProperty("name").GetString()??"";
            if(!NativeWorkspacePresets.Presets.ContainsKey(name))throw new ArgumentException("Unknown workspace preset.");
            Save(old with{HiddenTabs=NativeWorkspacePresets.Hidden(name).ToList(),TabVisibilityPreset=name});return;
        }
        else if(operation=="visibility")
        {
            string id=NativeRequestTargets.Workspace(request);var workspace=NativeWorkspaceCatalog.Find(id);
            if(workspace.Id is "overview" or "visible")return;
            if(request.GetProperty("visible").GetBoolean())hidden.Remove(workspace.LoadTab);else hidden.Add(workspace.LoadTab);
        }
        else throw new ArgumentException("Unknown navigation action.");
        Save(old with{HiddenTabs=hidden.ToList(),TabVisibilityPreset=hidden.Count==0?"Full workspace":"Custom"});
    }
}
