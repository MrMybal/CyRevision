using System.Collections;
using System.Reflection;
using System.Text.Json;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

public sealed partial class NativeModelAdapter(MainWindowViewModel model,string? configurationDirectory=null)
{
    private static readonly Dictionary<string, PropertyInfo> Properties = typeof(MainWindowViewModel)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
        .ToDictionary(property => property.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, MethodInfo> Actions = typeof(MainWindowViewModel)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName && method.Name is not ("InitializeAsync" or "DisposeAsync") && !method.Name.Contains("Native", StringComparison.Ordinal) &&
            (method.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(method.ReturnType)) &&
            method.GetParameters().All(parameter => parameter.ParameterType == typeof(CancellationToken) ||
                NativeWorkspaceCatalog.IsScalar(parameter.ParameterType)))
        .GroupBy(method => method.Name).Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    private readonly Dictionary<string, string> _selectionNames = new(StringComparer.Ordinal)
    {
        ["PreparedChanges"]="SelectedChange", ["LocalOnlyChanges"]="SelectedChange",
        ["History"]="SelectedExplorerRevision", ["FilteredBranches"]="SelectedBranch", ["Presets"]="SelectedPreset",
        ["FilteredPullRequests"]="SelectedPullRequest", ["PullRequestFiles"]="SelectedPullRequestFile",
        ["CiRuns"]="SelectedCiRun", ["CiWorkflows"]="SelectedCiWorkflow", ["CiJobs"]="SelectedCiJob",
        ["Plugins"]="SelectedPlugin", ["Backups"]="SelectedBackup", ["CodeFileList"]="SelectedCodeNode",
        ["CodeFileSearchResults"]="SelectedCodeFileSearchResult", ["CodeSearchResults"]="SelectedCodeSearchResult",
        ["SyncConflicts"]="SelectedSyncConflict", ["SyncCommits"]="SelectedSyncCommit",
        ["LfsFiles"]="SelectedLfsFile", ["LfsVersions"]="SelectedLfsVersion", ["LfsLocks"]="SelectedLfsLock",
        ["ExplorerFiles"]="SelectedExplorerFile", ["SelectedBranchHistory"]="SelectedBranchRevision",
        ["GitArchiveCandidates"]="SelectedGitArchiveCandidate", ["ArchivedGitBranches"]="SelectedArchivedGitBranch",
        ["UnrealBuildEngines"]="SelectedUnrealBuildEngine", ["UnrealBuildTargets"]="SelectedUnrealBuildTarget",
        ["UnrealBuildPresets"]="SelectedUnrealBuildPreset",
        ["NativeCodeFiles"]="SelectedCodeNode",
        ["PullRequestCiWorkflows"]="SelectedPullRequestCiWorkflow",["PullRequestCiRuns"]="SelectedPullRequestCiRun",["PullRequestCiJobs"]="SelectedPullRequestCiJob",
    };
    private bool _busy;
    private string _activeCommand="",_activeAction="",_activeWorkspace="";
    private bool _initialized;
    private IList _nativeCodeFiles = Array.Empty<object>();
    private IList _nativeFileHistory = Array.Empty<object>();
    private string _codeDirectory = "";
    private string _fileHistoryPath="";
    private readonly NativeNavigationState _navigation=new(configurationDirectory);
    public NativePreferencesActions? Preferences {get;set;}
    public void MarkInitialized() => _initialized = true;
    private static string Text(JsonElement request, string name, string fallback = "") =>
        request.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    public object Catalog() => new
    {
        protocol = 1,
        actions = Actions.Values.OrderBy(method => method.Name).Select(DescribeAction).ToArray(),
        version = typeof(NativeModelAdapter).Assembly.GetName().Version?.ToString(3) ?? "2.0.0",
        workspaces = NativeWorkspaceCatalog.All.Select(workspace => new
        {
            workspace.Id, workspace.Label, workspace.Category,
            legacyId=workspace.LoadTab,
            tables = workspace.Tables.Where(name => name.StartsWith("Native", StringComparison.Ordinal) || Properties.ContainsKey(name)).ToArray()
        }),
    };

    public object State(JsonElement request) => BuildState(request);

    private object LegacyState(JsonElement request)
    {
        NativeWorkspace workspace = NativeWorkspaceCatalog.Find(Text(request, "workspace", "changes"));
        string source = Text(request, "table", workspace.Tables.FirstOrDefault(name => Properties.ContainsKey(name)) ?? "");
        int skip = request.TryGetProperty("skip", out var skipValue) ? skipValue.GetInt32() : 0;
        int take = request.TryGetProperty("take", out var takeValue) ? takeValue.GetInt32() : 100;
        bool descending = request.TryGetProperty("descending", out var descendingValue) && descendingValue.GetBoolean();
        object? table = workspace.Tables.Contains(source) && TryList(source) is IList list
            ? NativeTable.Page(list, source, skip, take, Text(request, "filter"), Text(request, "sort"), descending) : null;
        return new
        {
            workspaceId = workspace.Id,
            ready = _initialized,
            busy = _busy || model.IsBusy || model.IsProjectLoading,
            status = model.StatusMessage,
            projectId = model.SelectedProject?.Id.ToString() ?? "",
            project = model.SelectedProject?.Name ?? "No project selected",
            branch = model.CurrentBranch,
            mode = model.SelectedProject?.ModeAndServices ?? "",
            codeDirectory = _codeDirectory,
            projects = model.Projects.Select(project => new { id=project.Id, project.Name, project.RootPath, project.BranchName, project.Mode, project.IsLoading }),
            operations = model.ActiveOperations.Concat(model.RecentOperations.Take(15))
                .Select(operation => new { operation.Id, operation.Title, state=operation.State.ToString(), operation.Detail, operation.DurationText }),
            properties = Properties.Values.Where(property =>
                (NativeWorkspaceCatalog.IsScalar(property.PropertyType)) &&
                workspace.Prefixes.Any(prefix => property.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                !property.Name.EndsWith("Color", StringComparison.Ordinal) &&
                !property.Name.EndsWith("Brush", StringComparison.Ordinal))
                .Select(property => DescribeProperty(property)).ToArray(),
            actions = Actions.Values.Where(method => NativeWorkspaceCatalog.GroupFor(method.Name) == workspace.Id)
                .OrderBy(method => method.Name).Select(DescribeAction).ToArray(),
            table,
            preview = workspace.Id switch
            {
                "changes" => model.DiffText,
                "code" => model.CodePreviewText,
                "history" => model.ExplorerDiff,
                "assets" => model.AssetDiffReport,
                _ => ""
            }
        };
    }

    private object DescribeProperty(PropertyInfo property) => new
    {
        name = property.Name,
        label = NativeWorkspaceCatalog.Label(property.Name),
        kind = property.PropertyType.IsEnum ? "enum" : property.PropertyType == typeof(bool) ? "bool" : "text",
        editable = property.SetMethod?.IsPublic == true,
        secret = NativeWorkspaceCatalog.IsSecret(property.Name),
        value = NativeWorkspaceCatalog.IsSecret(property.Name) ? null : NativeTable.Value(NativeTable.Read(property, model), 262144),
        options = property.PropertyType.IsEnum ? Enum.GetNames(property.PropertyType) : []
    };

    private static object DescribeAction(MethodInfo method) => new
    {
        name = method.Name,
        label = NativeWorkspaceCatalog.Label(method.Name),
        confirmation = RequiresConfirmation(method.Name),
        requiresProjectReady = !NativeActivity.CanRunDuringProjectScan(method.Name),
        parameters = method.GetParameters().Where(parameter => parameter.ParameterType != typeof(CancellationToken)).Select(parameter => new
        {
            name = parameter.Name,
            label = NativeWorkspaceCatalog.Label(parameter.Name ?? ""),
            kind = parameter.ParameterType == typeof(bool) ? "bool" : parameter.ParameterType.IsEnum ? "enum" : "text",
            optional = parameter.IsOptional,
            value = parameter.IsOptional && parameter.DefaultValue is not DBNull ? parameter.DefaultValue : null,
            options = parameter.ParameterType.IsEnum ? Enum.GetNames(parameter.ParameterType) : []
        })
    };

    public static bool RequiresConfirmation(string name) => new[] { "Delete", "Remove", "Rollback", "ExecuteLfsCleanup", "RunNativeLfsPrune", "Relocate", "Restore", "Merge", "Revert", "ApplyCherryPick", "ApplySelectedSyncCommit", "SubmitPerforce", "ApplyMultiRestore", "Unlock", "ResolveSelectedSyncConflict", "CleanExpiredSyncConflictBackups", "ArchiveOldBackups", "ArchiveSelectedGitBranch", "ApplyPerforceReconcile", "ApplyPerforceSync", "PushLore", "SwitchLoreBranch", "ApplyVpnFirewall", "ApplySwarmDns", "Rotate", "RevokeSelectedPeer", "UpdateSelectedPeerRole", "ApprovePeerJoinRequest", "ImportMembershipGrant", "EmergencyBlockAiMcp", "EmergencyUnblockAiMcp" }
        .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    public async Task<object?> ExecuteAsync(JsonElement request)
    {
        string command = Text(request, "command");
        if (command == "catalog") return Catalog();
        if (command == "state") return State(request);
        if(command is "prefs.get" or "prefs.cancel")return Preferences is {} preferences?await preferences.ExecuteAsync(request):throw new InvalidOperationException("Preferences are unavailable.");
        if (command == "cancel")
        {
            model.CancelNativeOperation();
            NativePullRequestActions.Cancel(model);
            NativeOverviewActions.Cancel(model);
            Preferences?.Cancel();
            return new { message="Cancellation requested" };
        }
        if(command=="gitBranchInspector" && Text(request,"operation")=="cancel")
            return await NativeGitActions.ExecuteAsync(model,request,TryList);
        if (!_initialized) throw new InvalidOperationException("The application is still loading.");
        if (command is not "project" && request.TryGetProperty("projectId", out var projectId) &&
            projectId.GetString() != (model.SelectedProject?.Id.ToString() ?? ""))
            throw new InvalidOperationException("The project changed. Refresh the view before continuing.");
        // Selection is local, versioned and cancellable. It must not disappear just
        // because another panel is loading. It must not release the operation gate.
        if(NativeActivity.IsInteractive(command))
        {
            if(command=="select")Select(request);
            else if(command=="tools.tree.select")return await NativeToolsActions.ExecuteAsync(model,request,TryList);
            else _navigation.Update(model,request);
            return new { message=model.StatusMessage };
        }
        if (_busy || model.IsBusy) throw new InvalidOperationException("An operation is already running.");
        if(model.IsProjectLoading && command is not ("project" or "workspace" or "directory" or "fileHistory" or "set" or "select" or "include" or "itemSet") &&
            !(command=="invoke" && NativeActivity.CanRunDuringProjectScan(Text(request,"action"))))
            throw new InvalidOperationException("Wait for the project scan before modifying the repository.");
        _busy = true;
        _activeCommand=command;_activeAction=Text(request,"action");_activeWorkspace=Text(request,"workspace",Text(request,"viewWorkspace"));
        try
        {
            if(command is not ("project" or "navigation" or "select" or "set" or "include" or "itemSet") && request.TryGetProperty("edits",out var pendingEdits) && pendingEdits.ValueKind==JsonValueKind.Object)
                foreach(var edit in pendingEdits.EnumerateObject())SetField(edit.Name,edit.Value);
            switch (command)
            {
                case "project":
                    Guid targetId=NativeRequestTargets.Project(request);
                    var target=model.Projects.FirstOrDefault(project=>project.Id==targetId)
                        ?? throw new ArgumentException("Unknown project.");
                    await NativeGitActions.DisposeAsync(model);
                    model.SelectedProject=target;
                    _nativeCodeFiles = Array.Empty<object>();
                    _nativeFileHistory = Array.Empty<object>();
                    _fileHistoryPath="";
                    _codeDirectory = "";
                    break;
                case "workspace":
                    var workspace = NativeWorkspaceCatalog.Find(Text(request,"workspace"));
                    if(!NativeWorkspaceCatalog.IsAvailable(workspace,model)) throw new InvalidOperationException("This workspace is unavailable in the project mode.");
                    if(!request.TryGetProperty("detached",out var detached) || detached.ValueKind!=JsonValueKind.True)_navigation.Navigate(model,workspace);
                    if (workspace.LoadTab is not null) await model.EnsureWorkspaceDataLoadedAsync(workspace.LoadTab);
                    if (workspace.Id == "code")
                    {
                        await model.EnsureCodeWorkspaceLoadedAsync();
                        _nativeCodeFiles = (await model.LoadNativeCodeDirectoryAsync(_codeDirectory)).ToArray();
                    }
                    break;
                case "navigation":
                    _navigation.Update(model,request);
                    break;
                case "directory":
                    _codeDirectory = Text(request,"path");
                    _nativeCodeFiles = (await model.LoadNativeCodeDirectoryAsync(_codeDirectory)).ToArray();
                    break;
                case "fileHistory":
                    var project = model.SelectedProject ?? throw new InvalidOperationException("Select a project first.");
                    _nativeFileHistory = NativeToolsActions.ProjectFileHistory(await model.LoadProjectFileHistoryAsync(project,Text(request,"path")));
                    _fileHistoryPath=Text(request,"path");
                    break;
                case "set":
                    SetProperty(request);
                    break;
                case "select":
                    Select(request);
                    break;
                case "include":
                    string path = Text(request,"path");
                    var change = model.Changes.FirstOrDefault(item => item.Path == path)
                        ?? throw new InvalidOperationException("This file is no longer in the changes list.");
                    change.IsIncluded = request.GetProperty("included").GetBoolean();
                    break;
                case "itemSet":
                    var items=TryList(Text(request,"source"))??throw new ArgumentException("Unknown list.");
                    int itemIndex=request.GetProperty("index").GetInt32();
                    if(itemIndex<0 || itemIndex>=items.Count || NativeTable.Key(items[itemIndex])!=Text(request,"key"))throw new InvalidOperationException("The list changed. Select the item again.");
                    string itemProperty=Text(request,"property");
                    if(itemProperty is not ("IsIncluded" or "IsSelected"))throw new ArgumentException("This item field cannot be edited.");
                    var itemField=items[itemIndex]!.GetType().GetProperty(itemProperty);
                    if(itemField?.SetMethod?.IsPublic!=true || itemField.PropertyType!=typeof(bool))throw new ArgumentException("This item is read-only.");
                    itemField.SetValue(items[itemIndex],request.GetProperty("value").GetBoolean());
                    break;
                case "invoke":
                    if(request.TryGetProperty("edits",out var edits) && edits.ValueKind==JsonValueKind.Object)
                        foreach(var edit in edits.EnumerateObject()) SetField(edit.Name,edit.Value);
                    await InvokeAsync(request);
                    break;
                default:
                    if(command.StartsWith("prefs.",StringComparison.Ordinal))return Preferences is {} prefs?await prefs.ExecuteAsync(request):throw new InvalidOperationException("Preferences are unavailable.");
                    if(NativeOverviewActions.Handles(command))return await NativeOverviewActions.ExecuteAsync(model,request,TryList);
                    if(NativeGitActions.Handles(command)) return await NativeGitActions.ExecuteAsync(model,request,TryList);
                    if(NativePullRequestActions.Handles(command)) return await NativePullRequestActions.ExecuteAsync(model,request,TryList);
                    if(NativeToolsActions.Handles(command)) return await NativeToolsActions.ExecuteAsync(model,request,TryList);
                    throw new ArgumentException("Unknown command.");
            }
            return new { message = model.StatusMessage };
        }
        finally { _busy = false;_activeCommand="";_activeAction="";_activeWorkspace=""; }
    }

    private void SetProperty(JsonElement request)
    {
        string name = Text(request,"property");
        SetField(name,request.GetProperty("value"));
    }

    private void Select(JsonElement request)
    {
        string name = Text(request,"source");
        IList list = TryList(name) ?? throw new ArgumentException("Unknown list.");
        int index = request.GetProperty("index").GetInt32();
        if (index < 0 || index >= list.Count || NativeTable.Key(list[index]) != Text(request,"key"))
            throw new InvalidOperationException("The list changed. Select the item again.");
        object? item = list[index];
        if(name=="ExplorerFileHistory" && item is CyRevision.Git.GitFileRevision fileRevision)
        {model.SelectedExplorerRevision=fileRevision.Revision;return;}
        string? propertyName = Text(request,"selection",_selectionNames.GetValueOrDefault(name)??"");
        if(string.IsNullOrEmpty(propertyName))propertyName=_selectionNames.GetValueOrDefault(name);
        PropertyInfo? property = !string.IsNullOrEmpty(propertyName) ? Properties.GetValueOrDefault(propertyName):null;
        if(property is null && item is not null && !NativeWorkspaceCatalog.IsScalar(item.GetType()))
        {
            var candidates=Properties.Values.Where(candidate=>candidate.Name.StartsWith("Selected",StringComparison.Ordinal) && candidate.SetMethod?.IsPublic==true && candidate.PropertyType==item.GetType()).ToArray();
            if(candidates.Length==1)property=candidates[0];
        }
        if(property?.SetMethod?.IsPublic==true){property.SetValue(model,item);return;}
        if(!string.IsNullOrEmpty(propertyName) && TryResolve(propertyName,out var owner,out var nested) && nested.SetMethod?.IsPublic==true)
        {nested.SetValue(owner,item);return;}
        // Read-only reports and primitive lists have no selection side effect.
    }

    private async Task InvokeAsync(JsonElement request)
    {
        if (!Actions.TryGetValue(Text(request,"action"), out var method)) throw new ArgumentException("Unknown action.");
        if (RequiresConfirmation(method.Name) && (!request.TryGetProperty("confirmed", out var confirmed) || !confirmed.GetBoolean()))
            throw new InvalidOperationException("Confirm this action first.");
        JsonElement parameters = request.TryGetProperty("parameters", out var supplied) ? supplied : default;
        object?[] values = method.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(parameter.Name!, out var value))
                return ConvertValue(value,parameter.ParameterType);
            if (parameter.IsOptional) return parameter.DefaultValue;
            throw new ArgumentException($"Enter {parameter.Name}.");
        }).ToArray();
        try
        {
            object? result = method.Invoke(model,values);
            if (result is Task task) await task;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static object? ConvertValue(JsonElement value, Type type)
    {
        Type? nullableType = Nullable.GetUnderlyingType(type);
        if (value.ValueKind == JsonValueKind.Null && (nullableType is not null || !type.IsValueType)) return null;
        type = nullableType ?? type;
        if (type == typeof(string)) return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        if (type.IsEnum)
        {
            object result = Enum.Parse(type,value.GetString() ?? "",ignoreCase:true);
            return Enum.IsDefined(type,result) ? result : throw new ArgumentException("Invalid option.");
        }
        if (value.ValueKind == JsonValueKind.String && type != typeof(Guid))
            return Convert.ChangeType(value.GetString(),type,System.Globalization.CultureInfo.InvariantCulture);
        return JsonSerializer.Deserialize(value.GetRawText(),type);
    }

    private IList? TryList(string name)
    {
        if (name == "NativeCodeFiles") return _nativeCodeFiles;
        if (name == "NativeFileHistory") return _nativeFileHistory;
        if(NativeGitActions.GetList(model,name) is IList gitList) return gitList;
        if(NativePullRequestActions.TryList(model,name) is IList workList) return workList;
        if(NativeToolsActions.GetList(model,name) is IList toolsList) return toolsList;
        if(NativeWorkspaceCatalog.IsSecret(name) || !Properties.TryGetValue(name,out var property)) return null;
        object? value = NativeTable.Read(property,model);
        return value as IList ?? (value as IEnumerable)?.Cast<object>().Take(200_000).ToArray();
    }
}
