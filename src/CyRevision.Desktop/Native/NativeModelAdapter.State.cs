using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace CyRevision.Desktop.Native;

public sealed partial class NativeModelAdapter
{
    private object BuildState(JsonElement request)
    {
        var workspace=NativeWorkspaceCatalog.Find(Text(request,"workspace","overview"));
        var workspaces=NativeViewRequests.Workspaces(request,workspace);
        var names=Properties.Values.Where(property=>NativeWorkspaceCatalog.IsScalar(property.PropertyType) &&
            workspaces.Any(view=>view.Prefixes.Any(prefix=>property.Name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))).Select(property=>property.Name).ToHashSet(StringComparer.Ordinal);
        foreach(string common in new[]{"CommitMessage","CurrentProjectName","RepositoryPath","CurrentBranch","ChangeSummaryBase","GitAheadSummary","GitBehindSummary","GitConnectionKind","ToolStatus","ProjectLoadStage","ProjectLoadProgress","IsProjectLoading","IsBusy","IsGitProject","AreGitActionsEnabled","HasSelectedProject","CanCommitPreparedChanges","CanMoveSelectedProjectUp","CanMoveSelectedProjectDown","ActivePluginOperatingModeName","ActivePluginOperatingModeSummary","HasActivePluginOperatingMode","ProjectDiskSummary","ProjectDiskWarning","ProjectDiskFreePercent"}) names.Add(common);
        if(request.TryGetProperty("fields",out var requested) && requested.ValueKind==JsonValueKind.Array)
            foreach(var item in requested.EnumerateArray().Take(512))if(item.ValueKind==JsonValueKind.String)names.Add(item.GetString()!);
        var fields=new Dictionary<string,object?>();
        foreach(string name in names)
            if(TryResolve(name,out var owner,out var property) && NativeWorkspaceCatalog.IsScalar(property.PropertyType))
                fields[name]=Field(name,owner,property);
        foreach(var view in workspaces)
        foreach(var extra in NativeGitActions.GetFields(model,view.Id))
            fields[extra.Key]=new{name=extra.Key,label=NativeWorkspaceCatalog.Label(extra.Key),kind=extra.Value is bool?"bool":"text",editable=false,secret=false,value=NativeTable.Value(extra.Value,2*1024*1024),options=Array.Empty<string>()};

        string source=Text(request,"table",workspace.Tables.FirstOrDefault()??"");
        object? primary=Page(request,source);
        var tables=new Dictionary<string,object?>();
        var selections=new Dictionary<string,object?>();
        var tableSelections=new Dictionary<string,string>();
        foreach(string name in names.Where(name=>name.StartsWith("Selected",StringComparison.Ordinal) || name is "MultiRestoreCommit"))
            if(TryResolve(name,out var selectionOwner,out var selectionProperty) && !NativeWorkspaceCatalog.IsScalar(selectionProperty.PropertyType))
            {var selected=NativeTable.Read(selectionProperty,selectionOwner);selections[name]=new{key=SelectionKey(selected),text=Display(selected)};}
        if(request.TryGetProperty("tables",out var tableRequests) && tableRequests.ValueKind==JsonValueKind.Array)
            foreach(var table in tableRequests.EnumerateArray().Take(64))
            {
                string name=Text(table,"source");
                if(name.Length==0)continue;
                string tableKey=NativeViewRequests.TableKey(table,name);
                if(tables.ContainsKey(tableKey))continue;
                tables[tableKey]=Page(table,name);
                string selection=Text(table,"selection");
                if(selection.Length==0)selection=_selectionNames.GetValueOrDefault(name)??"";
                if(selection.Length>0 && TryResolve(selection,out var selectedOwner,out var selectedProperty))
                {
                    object? value=NativeTable.Read(selectedProperty,selectedOwner);
                    var key=SelectionKey(value,TryList(name));
                    selections[selection]=new{key,text=Display(value)};
                    tableSelections[tableKey]=key;
                }
            }
        return new{
            workspaceId=workspace.Id,ready=_initialized,busy=_busy||model.IsBusy||model.IsProjectLoading,
            operationBusy=_busy||model.IsBusy,projectLoading=model.IsProjectLoading,
            loading=NativeActivity.Snapshot(model),activity=new{command=_activeCommand,action=_activeAction,workspace=_activeWorkspace},
            status=model.IsProjectLoading?model.ProjectLoadStage:model.StatusMessage,progress=model.ProjectLoadProgress,
            projectId=model.SelectedProject?.Id.ToString()??"",project=model.SelectedProject?.Name??"No project selected",
            branch=model.CurrentBranch,mode=model.SelectedProject?.ModeAndServices??"",codeDirectory=_codeDirectory,fileHistoryPath=_fileHistoryPath,
            projects=model.Projects.Select(project=>new{
                id=project.Id,project.Name,project.RootPath,project.BranchName,project.Mode,project.ModeMaturity,project.ModeAndServices,
                project.SidebarGroup,project.AccentColor,project.StatusColor,project.IsLoading,project.LoadProgress,project.LoadDetail,
                project.ChangeCount,project.AheadCount,project.BehindCount,project.HasRemote,
                project.UnreadNotificationCount,project.NotificationBadgeText,project.IsSyncRunning,project.IsSyncPaused,project.IsVpnRunning,
                git=project.Definition.Features.GitEnabled,sync=project.Definition.Features.PeerSyncEnabled,backup=project.Definition.Features.BackupEnabled,
                plugin=project.Definition.PluginOperatingModeProviderId??""
            }).ToArray(),
            operations=model.ActiveOperations.Concat(model.RecentOperations.Take(30)).Select(operation=>new{operation.Id,operation.Title,operation.State,operation.Detail,operation.DurationText,operation.ProjectName,operation.IsRead}),
            navigation=_navigation.Snapshot(model),fields,selections,tableSelections,tables,table=primary,images=Images(workspaces.Select(view=>view.Id)),
            properties=fields.Values.ToArray(),actions=Actions.Values.Where(method=>NativeWorkspaceCatalog.GroupFor(method.Name)==workspace.Id).OrderBy(method=>method.Name).Select(DescribeAction).ToArray(),
            pullRequestContext=NativePullRequestActions.Snapshot(model),toolsContext=NativeToolsActions.Snapshot(model),overviewContext=NativeOverviewActions.Snapshot(model),preferences=Preferences?.Snapshot(),
            preview=workspace.Id switch{"changes"=>model.DiffText,"code"=>model.CodePreviewText,"history"=>model.ExplorerDiff,"assets"=>model.AssetDiffReport,_=>""}
        };
    }
    private object Images(IEnumerable<string> workspaces)
    {
        var result=new Dictionary<string,string[]>();
        void Export(string property,params string[] slots){var paths=slots.Select(slot=>model.ExportNativeImage(NativeGitActions.GetImage(model,slot),slot)).Where(path=>path.Length>0).ToArray();if(paths.Length>0)result[property]=paths;}
        foreach(string workspace in workspaces){
        if(workspace=="changes")Export("DiffText","DiffPreviewImage");
        if(workspace=="history")Export("ExplorerDiff","ExplorerDiffPreviewImage");
        if(workspace=="composition")Export("MultiRestoreDiff","MultiRestoreDiffPreviewImage");
        if(workspace=="lfs")Export("LfsPreview","LfsPreview");
        if(workspace=="branches"){
            Export("BranchInspectorPreview","BranchInspectorPreviewImage");Export("BranchInspectorPreviewImage","BranchInspectorPreviewImage");
            Export("BranchInspectorDiff","BranchInspectorDiffBaselineImage","BranchInspectorDiffCandidateImage","BranchInspectorDiffHeatmapImage");
        }
        }
        return result;
    }
    private object? Page(JsonElement request,string source)
    {
        if(TryList(source) is not IList list)return null;
        int skip=request.TryGetProperty("skip",out var skipValue)&&skipValue.TryGetInt32(out var start)?start:0;
        int take=request.TryGetProperty("take",out var takeValue)&&takeValue.TryGetInt32(out var count)?count:100;
        bool descending=request.TryGetProperty("descending",out var direction)&&direction.ValueKind==JsonValueKind.True;
        return NativeTable.Page(list,source,skip,take,Text(request,"filter"),Text(request,"sort"),descending);
    }
    private bool TryResolve(string path,out object owner,out PropertyInfo property)
    {
        owner=model;property=null!;
        string[] parts=path.Split('.');if(parts.Length>5)return false;
        for(int index=0;index<parts.Length;index++)
        {
            var candidate=owner.GetType().GetProperty(parts[index],BindingFlags.Public|BindingFlags.Instance);
            if(candidate?.GetMethod is null || candidate.GetIndexParameters().Length>0)return false;
            property=candidate;
            if(index==parts.Length-1)return true;
            if(NativeTable.Read(candidate,owner) is not {} child)return false;
            owner=child;
        }
        return false;
    }
    private object Field(string name,object owner,PropertyInfo property)
    {
        Type type=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;
        return new{name,label=NativeWorkspaceCatalog.Label(name),kind=type.IsEnum?"enum":type==typeof(bool)?"bool":type==typeof(string)?"text":"number",
            editable=property.SetMethod?.IsPublic==true,secret=NativeWorkspaceCatalog.IsSecret(name),
            value=NativeWorkspaceCatalog.IsSecret(name)?null:NativeTable.Value(NativeTable.Read(property,owner),2*1024*1024),
            options=type.IsEnum?Enum.GetNames(type):[]};
    }
    private void SetField(string name,JsonElement value)
    {
        if(!TryResolve(name,out var owner,out var property) || property.SetMethod?.IsPublic!=true || !NativeWorkspaceCatalog.IsScalar(property.PropertyType))
            throw new ArgumentException("This field is read-only or no longer selected.");
        property.SetValue(owner,ConvertValue(value,property.PropertyType));
    }
    private static string Display(object? value)
    {
        if(value is null)return "";
        if(value is CyRevision.Git.GitRevision revision)return $"{revision.ShortHash} · {revision.Subject}";
        if(NativeWorkspaceCatalog.IsScalar(value.GetType()))return value.ToString()??"";
        foreach(string name in new[]{"DisplayName","Name","Title","Label","Path","Id"})
            if(value.GetType().GetProperty(name) is {} property && NativeTable.Read(property,value) is {} text)return text.ToString()??"";
        return value.ToString()??"";
    }
    private string SelectionKey(object? value,IList? source=null)
    {
        // Refreshes replace immutable records with equal instances. Resolve the
        // selected record to the current list without weakening stale-row checks.
        int index=value is null?-1:source?.IndexOf(value)??-1;
        if(index>=0)return NativeTable.Key(source![index]);
        if(value is CyRevision.Git.GitRevision revision)
            return NativeTable.Key(model.History.FirstOrDefault(item=>item.Hash==revision.Hash)??revision);
        return NativeTable.Key(value);
    }
}
