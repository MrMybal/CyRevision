using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CyRevision.Automation;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

/// <summary>Project-scoped SDK bindings to the real live UI command layer, never to arbitrary method names.</summary>
internal sealed partial class DesktopAutomation(MainWindowViewModel model,RevisionAutomationEngine engine,NativePreferencesActions? preferences,NativeModelAdapter adapter,Action<string>? navigateClassic)
{
    private static readonly JsonSerializerOptions Json=CyMcpRuntime.JsonOptions;
    private static readonly PropertyInfo[] Fields=typeof(MainWindowViewModel).GetProperties().Where(DesktopAutomationCatalog.IsAllowedField)
        .Where(property=>!Regex.IsMatch(property.Name,"^(CodePreview|ExplorerDiff|DiffText|AssetDiff|ApplicationLog|RepositoryConsole|ProjectLoadStage)|Diff.*(Text|Content)|^(MultiRestoreDiff|PullRequestPatch)")).ToArray();
    private static readonly PropertyInfo[] Selected=typeof(MainWindowViewModel).GetProperties().Where(property=>property.Name.StartsWith("Selected",StringComparison.Ordinal)&&property.GetMethod is not null&&
        !NativeWorkspaceCatalog.IsScalar(property.PropertyType)&&!DesktopAutomationCatalog.IsSensitive(property.Name)&&!property.Name.Contains("Ai",StringComparison.Ordinal)).ToArray();
    private static readonly HashSet<string> Tables=NativeWorkspaceCatalog.All.Where(view=>view.Id is not ("ai" or "mcp" or "console" or "diagnostics"))
        .SelectMany(view=>view.Tables).Where(name=>!DesktopAutomationCatalog.IsSensitive(name)&&!name.Contains("LogEntries",StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate=new(1,1);
    private static JsonObject Text(int max=4096)=>new(){["type"]="string",["maxLength"]=max};
    private static JsonObject Choice(IEnumerable<string> choices)=>new(){["type"]="string",["enum"]=new JsonArray(choices.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    private static JsonObject Number(int max=200000)=>new(){["type"]="integer",["minimum"]=0,["maximum"]=max};
    private static JsonObject Schema(Dictionary<string,JsonObject>? fields=null,params string[] required)=>new(){["type"]="object",["properties"]=fields is null?new JsonObject():new JsonObject(fields.Select(pair=>KeyValuePair.Create<string,JsonNode?>(pair.Key,pair.Value))),["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray()),["additionalProperties"]=false};
    private static JsonObject Output()=>new(){["type"]="object",["properties"]=new JsonObject{["value"]=new JsonObject()},["required"]=new JsonArray("value"),["additionalProperties"]=false};
    private static JsonObject TargetSchema()=>Schema(new(){["projectId"]=Text(36),["project"]=Text(256),["branch"]=Text(256),["selections"]=new(){["type"]="object"}},"projectId","project","branch","selections");
    private static Dictionary<string,JsonObject> GuardFields()=>new(){["stateToken"]=new(){["type"]="string",["minLength"]=64,["maxLength"]=64},["target"]=TargetSchema()};
    private static string String(JsonElement args,string name,string fallback="")=>args.TryGetProperty(name,out var value)?value.GetString()??fallback:fallback;
    private static int Int(JsonElement args,string name,int fallback=0)=>args.TryGetProperty(name,out var value)?value.GetInt32():fallback;
    private static JsonObject TypeSchema(Type type)
    {
        var underlying=Nullable.GetUnderlyingType(type);if(underlying is not null)type=underlying;
        JsonObject schema=type.IsEnum?Choice(Enum.GetNames(type)):type==typeof(bool)?new(){["type"]="boolean"}:type==typeof(string)?Text(1024*1024):type==typeof(Guid)||type==typeof(DateTime)||type==typeof(DateTimeOffset)||type==typeof(TimeSpan)?Text(256):new(){["type"]="number"};
        if(underlying is not null)return new(){["anyOf"]=new JsonArray(schema,new JsonObject{["type"]="null"})};return schema;
    }
    internal void Register()
    {
        engine.Add(new("app.capabilities","List the reviewed live application modules, exact actions, required grants and explicit host-only exclusions.",Schema(),Output(),true,Handler:async(_,token)=>await Ui(()=>Capabilities(),token)));
        engine.Add(new("app.context","Read this module's actual fields, stable selected targets and stateToken. Refresh this immediately before preparing an action; never invent a target/token.",Schema(new(){["workspace"]=Choice(NativeWorkspaceCatalog.All.Select(view=>view.Id))},"workspace"),Output(),true,Handler:async(args,token)=>await Ui(()=>Context(String(args,"workspace")),token)));
        engine.Add(new("app.table.read","Read a filtered page from a real live project table. Returned stable row keys are scoped to the source, never numeric row positions.",Schema(new(){["source"]=Choice(Tables.Order()),["offset"]=Number(),["limit"]=new(){["type"]="integer",["minimum"]=1,["maximum"]=200},["filter"]=Text(),["sort"]=Text(128),["descending"]=new(){["type"]="boolean"}},"source"),Output(),true,Handler:async(args,token)=>await Ui(()=>ReadTable(args),token)));
        var selectionFields=GuardFields();selectionFields["source"]=Choice(Tables.Order());selectionFields["key"]=Text();
        engine.Add(new("app.table.select","Select the exact stable row in its real UI table after confirmation. Use app.context for that table's workspace; selection may start the normal detail loader.",Schema(selectionFields,"source","key","stateToken","target"),Output(),true,Handler:(args,token)=>Mutate(args,WorkspaceFor(String(args,"source")),()=>{adapter.AutomationSelect(String(args,"source"),FindRow(args));return Task.FromResult<object>(new{selected=true,key=String(args,"key")});},token),RequiredCapabilities:AutomationCapability.Navigation,ConfirmationRequired:true));
        var flagFields=GuardFields();flagFields["source"]=Choice(Tables.Order());flagFields["key"]=Text();flagFields["property"]=Choice(["IsIncluded","IsSelected"]);flagFields["value"]=new(){["type"]="boolean"};
        engine.Add(new("app.table.setFlag","Set an existing row's inclusion/selection checkbox, never an arbitrary nested property. Then reread app.context before committing/restoring.",Schema(flagFields,"source","key","property","value","stateToken","target"),Output(),true,Handler:(args,token)=>Mutate(args,WorkspaceFor(String(args,"source")),()=>{var row=FindRow(args);var field=row.GetType().GetProperty(String(args,"property"));if(field?.PropertyType!=typeof(bool)||field.SetMethod?.IsPublic!=true)throw new AutomationException("AccessDenied","This row has no editable selection flag.");field.SetValue(row,args.GetProperty("value").GetBoolean());return Task.FromResult<object>(new{selected=true});},token),RequiredCapabilities:AutomationCapability.Navigation,ConfirmationRequired:true));
        engine.Add(new("app.projects.list","List registered project IDs, labels and operating modes without exporting local project configuration files.",Schema(),Output(),true,Handler:async(_,token)=>await Ui(()=>new{items=model.Projects.Select(project=>new{project.Id,project.Name,project.Mode,selected=project.Id==model.SelectedProject?.Id}).ToArray()},token)));
        var switchFields=GuardFields();switchFields["projectId"]=Text(36);
        engine.Add(new("app.projects.select","Select a registered project by ID. This cancels the old project session, clears its grants and requires reconnecting the assistant/MCP to the new scope.",Schema(switchFields,"projectId","stateToken","target"),Output(),true,Handler:(args,token)=>Mutate(args,"overview",()=>{var project=model.Projects.SingleOrDefault(project=>project.Id==Guid.Parse(String(args,"projectId")))??throw new ArgumentException("Unknown registered project.");model.SelectedProject=project;return Task.FromResult<object>(new{selected=project.Id,reconnectRequired=true});},token),RequiredCapabilities:AutomationCapability.Navigation|AutomationCapability.Administration,ConfirmationRequired:true));
        foreach(var workspace in NativeWorkspaceCatalog.All.Where(view=>view.Id is not ("ai" or "mcp" or "console")))
        {
            var captured=workspace;
            engine.Add(new("app."+workspace.Id+".open","Open the existing "+workspace.Label+" view and its normal data loader. Requires a fresh context for this workspace; unavailable modes/plugins remain unavailable.",Schema(GuardFields(),"stateToken","target"),Output(),true,Network:DesktopAutomationCatalog.Network(workspace.Id,""),Handler:(args,token)=>Mutate(args,captured.Id,async()=>{await adapter.AutomationOpenAsync(captured);navigateClassic?.Invoke(captured.LoadTab);return new{opened=captured.Id};},token),RequiredCapabilities:AutomationCapability.Navigation,ConfirmationRequired:true,Available:()=>NativeWorkspaceCatalog.IsAvailable(captured,model)));
        }
        foreach(var (workspace,name) in DesktopAutomationCatalog.Actions)
        {
            if(!NativeModelAdapter.AutomationMethods.TryGetValue(name,out var method))throw new InvalidOperationException("Review stale automation action: "+name);
            var inputs=GuardFields();var parameters=new Dictionary<string,JsonObject>();var required=new List<string>();
            foreach(var parameter in method.GetParameters().Where(parameter=>parameter.ParameterType!=typeof(CancellationToken))){parameters[parameter.Name!]=TypeSchema(parameter.ParameterType);if(!parameter.IsOptional)required.Add(parameter.Name!);}
            inputs["parameters"]=Schema(parameters,required.ToArray());
            var needed=new List<string>{"stateToken","target"};if(required.Count>0)needed.Add("parameters");
            bool readOnly=DesktopAutomationCatalog.ReadOnly(name);
            engine.Add(new("app."+workspace+"."+name,"Run CyRevision's existing "+NativeWorkspaceCatalog.Label(name)+" workflow on its reviewed live target. Prepare fields/selection, read app.context, then pass its exact target and stateToken. Query app.context/table.read for results; failures are not successful tool responses.",Schema(inputs,needed.ToArray()),Output(),readOnly,Network:DesktopAutomationCatalog.Network(workspace,name),Stage:!readOnly&&(name.Contains("Stage",StringComparison.Ordinal)||name=="CommitAsync"),Commit:new[]{"PullAsync","ExchangeGitAsync","CommitAsync","CommitLoreAsync","CreateSyncCommitAsync","ApplySelectedSyncCommitAsync","ApplyCherryPickAsync","MergeSelectedBranchAsync","MergeCurrentBranchIntoSelectedAsync","MergeSelectedPullRequestAsync","SubmitPerforceAsync"}.Contains(name,StringComparer.Ordinal),Handler:(args,token)=>Mutate(args,workspace,()=>Invoke(name,workspace,args,token),token),RequiredCapabilities:DesktopAutomationCatalog.Capabilities(workspace,name),Available:()=>NativeWorkspaceCatalog.IsAvailable(NativeWorkspaceCatalog.Find(workspace),model)));
        }
        foreach(var field in Fields.Where(field=>field.SetMethod?.IsPublic==true&&!field.Name.StartsWith("Is",StringComparison.Ordinal)&&!Regex.IsMatch(field.Name,"Endpoint|BaseUrl|ServerAddress|ApiUrl",RegexOptions.IgnoreCase)))
        {
            string workspace=NativeWorkspaceCatalog.GroupFor(field.Name);bool presentation=DesktopAutomationCatalog.IsPresentation(field.Name);var inputs=GuardFields();inputs["value"]=TypeSchema(field.PropertyType);
            engine.Add(new("app.fields.set."+field.Name,"Edit the existing "+NativeWorkspaceCatalog.Label(field.Name)+" field only; this does not apply/save its workflow. Read app.context after the edit and invoke its normal save/apply action separately.",Schema(inputs,"value","stateToken","target"),Output(),presentation,Network:DesktopAutomationCatalog.Network(workspace,field.Name),Handler:(args,token)=>Mutate(args,workspace,()=>Task.FromResult<object>(EditField(field,args)),token),RequiredCapabilities:AutomationCapability.Navigation|(presentation?0:AutomationCapability.Administration|DesktopAutomationCatalog.Capabilities(workspace,"Set"+field.Name)),ConfirmationRequired:true,Available:()=>NativeWorkspaceCatalog.IsAvailable(NativeWorkspaceCatalog.Find(workspace),model)));
        }
        RegisterTasks();
        if(preferences is not null)RegisterPreferences();
        if(adapter.AutomationNativeFrontend){
            engine.Add(new("app.appearance.read","Read the native frontend's acknowledged appearance and its available theme keys. A queued change is not acknowledged until the actual frontend applies it.",Schema(),Output(),true,Handler:async(_,token)=>await Ui(()=>adapter.AutomationAppearance,token)));
            var appearance=GuardFields();appearance["theme"]=Choice(["mid-grey","light-grey","amber-night","violet","navy"]);appearance["density"]=Choice(["compact","comfortable"]);appearance["locale"]=Choice(["en","fr"]);
            engine.Add(new("app.appearance.set","Apply a native CyUI theme/density/language on the actual rendering thread, after confirmation. Only supplied appearance fields change; read app.appearance.read for acknowledgement.",Schema(appearance,"stateToken","target"),Output(),false,Handler:(args,token)=>Mutate(args,"overview",()=>{var requested=JsonNode.Parse(args.GetRawText())!.AsObject();requested.Remove("stateToken");requested.Remove("target");if(requested.Count==0)throw new ArgumentException("Choose a theme, density or locale.");adapter.AutomationSetAppearance(JsonSerializer.SerializeToElement(requested,Json));return Task.FromResult<object>(new{queued=true});},token),RequiredCapabilities:AutomationCapability.Administration|AutomationCapability.Navigation));
        }
    }
    private object Capabilities()=>new
    {
        liveInstance=true,scope=model.SelectedProject?.Id,
        modules=NativeWorkspaceCatalog.All.Select(view=>new{view.Id,view.Label,available=NativeWorkspaceCatalog.IsAvailable(view,model),tables=view.Tables.Where(Tables.Contains).ToArray(),actions=DesktopAutomationCatalog.Actions.Where(action=>action.Workspace==view.Id).Select(action=>new{name="app."+view.Id+"."+action.Method,capabilities=DesktopAutomationCatalog.Capabilities(view.Id,action.Method).ToString(),network=DesktopAutomationCatalog.Network(view.Id,action.Method),readOnly=DesktopAutomationCatalog.ReadOnly(action.Method)}).ToArray()}).ToArray(),
        excluded=DesktopAutomationCatalog.Excluded.Select(pair=>new{action=pair.Key,reason=pair.Value}).ToArray(),
        blockedFields="Assistant grants/approvals, credentials, certificates, private identity/invitation material, executable/command trust and personal raw diagnostics remain host-only."
    };
    private static bool Matches(string name,string workspace)
    {
        if(NativeWorkspaceCatalog.GroupFor(name)==workspace)return true;
        foreach(string prefix in new[]{"Selected","New","Is","Can"})if(name.StartsWith(prefix,StringComparison.Ordinal)){name=name[prefix.Length..];break;}
        return NativeWorkspaceCatalog.Find(workspace).Prefixes.Any(prefix=>name.StartsWith(prefix,StringComparison.Ordinal));
    }
    private object Target(string workspace)=>new{projectId=model.SelectedProject?.Id.ToString()??"",project=model.SelectedProject?.Name??"",branch=model.CurrentBranch,
        selections=Selected.Where(property=>Matches(property.Name,workspace)).ToDictionary(property=>property.Name,property=>DescribeSelection(NativeTable.Read(property,model)))};
    private static object? DescribeSelection(object? item)=>item is null?null:new{key=StableKey(item),label=string.Join(" · ",new[]{"Number","Name","Title","ShortHash","RelativePath"}.Select(name=>item.GetType().GetProperty(name)).Where(property=>property is not null).Select(property=>NativeTable.Value(NativeTable.Read(property!,item),120))).Trim()};
    private string StateToken(string workspace)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(object? value)=>hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(value,Json));
        Add(new{project=model.SelectedProject?.Id,model.CurrentBranch});
        foreach(var field in Fields.Where(field=>field.SetMethod?.IsPublic==true&&Matches(field.Name,workspace)).OrderBy(field=>field.Name,StringComparer.Ordinal)){Add(field.Name);Add(NativeTable.Value(NativeTable.Read(field,model),1024*1024));}
        foreach(var field in Selected.Where(field=>Matches(field.Name,workspace)).OrderBy(field=>field.Name,StringComparer.Ordinal)){Add(field.Name);var item=NativeTable.Read(field,model);Add(item is null?null:StableKey(item));}
        foreach(string source in (workspace=="changes"?new[]{"PreparedChanges"}:workspace=="composition"?new[]{"MultiRestoreFiles","BranchComparisonCommits"}:workspace=="lfs"?new[]{"LfsCleanupItems"}:Array.Empty<string>()))
            if(adapter.AutomationList(source) is {} items)foreach(var item in items){if(item is null)continue;foreach(var flag in new[]{"IsIncluded","IsSelected"})if(item.GetType().GetProperty(flag) is {PropertyType:var type} property&&type==typeof(bool)&&NativeTable.Read(property,item) is true){Add(source);Add(StableKey(item));Add(flag);}}
        if(workspace is "pullrequests" or "changes")Add(adapter.AutomationList("NativeWorkItemSearch")?.Cast<object>().Select(item=>NativeTable.Properties(item.GetType()).Where(field=>!DesktopAutomationCatalog.IsSensitive(field.Name)).Select(field=>SafeValue(NativeTable.Read(field,item))).ToArray()).ToArray());
        if(workspace=="overview"){if(preferences is not null)Add(preferences.Snapshot());Add(adapter.AutomationAppearance);}
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private object Context(string workspace)
    {
        var view=NativeWorkspaceCatalog.Find(workspace);
        return new{workspace,stateToken=StateToken(workspace),target=Target(workspace),available=NativeWorkspaceCatalog.IsAvailable(view,model),busy=Busy(workspace),
            fields=Fields.Where(field=>Matches(field.Name,workspace)).Select(field=>new{name=field.Name,value=SafeValue(NativeTable.Read(field,model)),editable=field.SetMethod?.IsPublic==true,setter=field.SetMethod?.IsPublic==true&&engine.Operations.Any(op=>op.Name=="app.fields.set."+field.Name)?"app.fields.set."+field.Name:null}).ToArray(),
            tables=view.Tables.Where(Tables.Contains).Select(source=>new{source,count=adapter.AutomationList(source)?.Count??0}).ToArray(),status=Redact(model.StatusMessage)};
    }
    private object EditField(PropertyInfo field,JsonElement args)
    {
        var value=args.GetProperty("value");
        if(field.Name is "AssetBaselinePath" or "AssetCandidatePath" or "UnrealProjectPath" or "UnityProjectPath" or "GodotProjectPath" or "LoreProjectPath" or "TeamChatAttachmentPath")
            if(value.ValueKind==JsonValueKind.String&&value.GetString() is {Length:>0} path)
                value=JsonSerializer.SerializeToElement(engine.Resolve(Path.IsPathRooted(path)?Path.GetRelativePath(engine.Root,path):path,field.Name.EndsWith("ProjectPath",StringComparison.Ordinal)),Json);
        adapter.AutomationSet(field.Name,value);return new{field=field.Name,updated=true};
    }
    private static string StableKey(object item)
    {
        if(NativeWorkspaceCatalog.IsScalar(item.GetType()))return "value:"+Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item,Json))).ToLowerInvariant();
        if(item is IEnumerable collection)return "items:"+Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(collection.Cast<object>().Select(StableKey).ToArray(),Json))).ToLowerInvariant();
        foreach(string name in new[]{"Id","Number","Hash","SnapshotId","RelativePath","Path","Name"})
            if(item.GetType().GetProperty(name) is {} property&&NativeWorkspaceCatalog.IsScalar(property.PropertyType)&&NativeTable.Read(property,item) is {} value&&!string.IsNullOrWhiteSpace(value.ToString()))return name+":"+value;
        return "row:"+NativeTable.Key(item);
    }
    private string WorkspaceFor(string source)=>NativeWorkspaceCatalog.All.First(view=>view.Tables.Contains(source,StringComparer.Ordinal)).Id;
    private IList List(string source)
    {
        if(!Tables.Contains(source))throw new AutomationException("AccessDenied","This table is not exposed.");
        if(!NativeWorkspaceCatalog.IsAvailable(NativeWorkspaceCatalog.Find(WorkspaceFor(source)),model))throw new AutomationException("AccessDenied","This project mode/plugin does not expose the table.");
        return adapter.AutomationList(source)??Array.Empty<object>();
    }
    private object FindRow(JsonElement args)
    {
        string key=String(args,"key");var items=List(String(args,"source")).Cast<object>().Where(item=>ExposedRow(String(args,"source"),item)&&StableKey(item)==key).Take(2).ToArray();
        return items.Length==1?items[0]:throw new AutomationException("StaleState","The row is missing or ambiguous. Read the table again.");
    }
    private object ReadTable(JsonElement args)
    {
        string source=String(args,"source");IList rows=List(source);int offset=Int(args,"offset"),limit=Int(args,"limit",100);string filter=String(args,"filter"),sort=String(args,"sort");
        if(sort.Length>0&&rows.Count>0&&!NativeTable.Properties(rows[0]!.GetType()).Any(field=>field.Name==sort&&!DesktopAutomationCatalog.IsSensitive(field.Name)))throw new ArgumentException("This sort column is not exposed.");
        var indexes=rows.Cast<object>().Select((row,index)=>(row,index)).Where(pair=>ExposedRow(source,pair.row)).Where(pair=>string.IsNullOrWhiteSpace(filter)||NativeTable.Properties(pair.row.GetType()).Where(field=>!DesktopAutomationCatalog.IsSensitive(field.Name)).Any(field=>NativeTable.Read(field,pair.row)?.ToString()?.Contains(filter,StringComparison.OrdinalIgnoreCase)==true));
        if(sort.Length>0)indexes=args.TryGetProperty("descending",out var descending)&&descending.GetBoolean()?indexes.OrderByDescending(pair=>pair.row.GetType().GetProperty(sort)?.GetValue(pair.row),NativeTable.ScalarComparer.Instance):indexes.OrderBy(pair=>pair.row.GetType().GetProperty(sort)?.GetValue(pair.row),NativeTable.ScalarComparer.Instance);
        var matching=indexes.ToArray();return new{source,total=matching.Length,offset,limit,nextOffset=offset+limit<matching.Length?(int?)(offset+limit):null,rows=matching.Skip(offset).Take(limit).Select(pair=>new{key=StableKey(pair.row),values=NativeWorkspaceCatalog.IsScalar(pair.row.GetType())?new Dictionary<string,object?>{{"Value",SafeValue(pair.row)}}:NativeTable.Properties(pair.row.GetType()).Where(field=>!DesktopAutomationCatalog.IsSensitive(field.Name)).ToDictionary(field=>field.Name,field=>SafeValue(NativeTable.Read(field,pair.row)))}).ToArray()};
    }
    private bool Busy(string workspace)=>model.IsBusy||model.IsProjectLoading||typeof(MainWindowViewModel).GetProperties().Any(field=>field.PropertyType==typeof(bool)&&NativeActivity.IsLoadingFlag(field.Name)&&field.Name!="IsAiSdkBusy"&&NativeActivity.WorkspaceFor(field.Name)==workspace&&NativeTable.Read(field,model) is true);
    private bool ExposedRow(string source,object row)
    {
        if(source is not ("CodeSearchResults" or "CodeHistory" or "CodeFileSearchResults" or "CodeFileList" or "NativeCodeFiles" or "ExplorerFiles" or "ExplorerFileHistory" or "PreparedChanges" or "NativeGitChanges"))return true;
        var path=row.GetType().GetProperty("RelativePath")??row.GetType().GetProperty("Path");
        if(path is null||NativeTable.Read(path,row) is not string text||text.Length==0)return true;
        try{engine.Resolve(text);return true;}catch(UnauthorizedAccessException){return false;}
    }
    private void Guard(JsonElement args,string workspace)
    {
        if(!engine.Permissions.ScopeValid())throw new AutomationException("AccessDenied","The selected project or host permissions changed.");
        if(String(args,"stateToken")!=StateToken(workspace)||!JsonNode.DeepEquals(JsonNode.Parse(args.GetProperty("target").GetRawText()),JsonSerializer.SerializeToNode(Target(workspace),Json)))throw new AutomationException("StaleState","The UI target, draft, selection or preferences changed. Read app.context and confirm again.");
        if(Busy(workspace))throw new AutomationException("Busy","This module is still loading or another business operation is active.");
    }
    private async Task<object> Mutate(JsonElement args,string workspace,Func<Task<object>> action,CancellationToken token)
    {
        if(!await _gate.WaitAsync(0,token))throw new AutomationException("Busy","Another live automation action is active.");
        try{var pending=await Dispatcher.UIThread.InvokeAsync<Task<object>>(()=>{token.ThrowIfCancellationRequested();Guard(args,workspace);return action();},DispatcherPriority.Normal,token);return await pending;}
        finally{_gate.Release();}
    }
    private static async Task<object> Ui(Func<object> action,CancellationToken token)=>await Dispatcher.UIThread.InvokeAsync(action,DispatcherPriority.Normal,token);
    private async Task<object> Invoke(string name,string workspace,JsonElement args,CancellationToken token)
    {
        if(name is "StageAllAsync" or "CommitAsync" or "RollbackIncludedChangesAsync")
            foreach(var change in model.Changes.Where(change=>change.IsIncluded&&!change.IsLocalOnly))engine.Resolve(change.Path);
        if(name=="RollbackSelectedChangeAsync"&&model.SelectedChange is {} selectedChange)engine.Resolve(selectedChange.Path);
        if(name=="CommitAsync"&&model.SelectedProject?.Definition.Features.PeerSyncEnabled==true&&
           (!engine.Permissions.Network||(engine.Permissions.Capabilities&(AutomationCapability.Publication|AutomationCapability.Services))!=(AutomationCapability.Publication|AutomationCapability.Services)))
            throw new AutomationException("AccessDenied","In this project's Git + Sync mode, committing can exchange revisions. Network, publication and service grants are also required before committing.");
        string can="Can"+name.Replace("Async","",StringComparison.Ordinal);
        if(typeof(MainWindowViewModel).GetProperty(can) is {PropertyType:var type} field&&type==typeof(bool)&&NativeTable.Read(field,model) is false)throw new AutomationException("AccessDenied","The application currently disables this action.");
        var known=model.RecentOperations.Concat(model.ActiveOperations).Select(operation=>operation.Id).ToHashSet();
        var knownErrors=model.ApplicationLogEntries.Where(entry=>entry.Level==CyRevision.Desktop.Diagnostics.ApplicationLogLevel.Error).ToHashSet();
        using var cancellation=token.Register(()=>Dispatcher.UIThread.Post(()=>{model.CancelNativeOperation();NativePullRequestActions.Cancel(model);NativeOverviewActions.Cancel(model);preferences?.Cancel();}));
        var parameters=args.TryGetProperty("parameters",out var supplied)?supplied:JsonSerializer.SerializeToElement(new{});
        if(name is "SetTeamChatAttachment" or "SetAssetBaseline" or "SetAssetCandidate" or "SetUnrealProjectPath" or "SetUnityProjectPath" or "SetGodotProjectPath" or "SetLoreProjectPath" or "ImportProjectLicenseDraftAsync")
            if(parameters.TryGetProperty("path",out var input)&&input.GetString() is {} file){var normalized=JsonNode.Parse(parameters.GetRawText())!.AsObject();normalized["path"]=engine.Resolve(Path.IsPathRooted(file)?Path.GetRelativePath(engine.Root,file):file,name.EndsWith("ProjectPath",StringComparison.Ordinal));parameters=JsonSerializer.SerializeToElement(normalized,Json);}
        var result=await adapter.AutomationInvokeAsync(name,parameters,token);
        var failed=model.RecentOperations.Concat(model.ActiveOperations).FirstOrDefault(operation=>!known.Contains(operation.Id)&&operation.State is "Failed" or "Cancelled");
        if(failed is not null)throw new AutomationException(failed.State=="Cancelled"?"Cancelled":"BusinessError",Redact(failed.Detail));
        if(model.ApplicationLogEntries.Any(entry=>entry.Level==CyRevision.Desktop.Diagnostics.ApplicationLogLevel.Error&&!knownErrors.Contains(entry)))
            throw new AutomationException("BusinessError","The application reported an error during this workflow. Read the module context/status for details; the operation has not been reported as successful.");
        if(result is false)throw new AutomationException("BusinessError",Redact(model.StatusMessage));
        token.ThrowIfCancellationRequested();return new{completed=true,action=name,status=Redact(model.StatusMessage),result=SafeValue(result)};
    }
    private static object? SafeValue(object? value)=>value is string text?Redact(text.Length>32768?text[..32768]+" … [truncated; use paginated data tools]":text):value is null||NativeWorkspaceCatalog.IsScalar(value.GetType())?NativeTable.Value(value):new{type=value.GetType().Name,detail="Read the module context/table for the updated result."};
    private static string Redact(string text)
    {
        text=Regex.Replace(text,@"(?i)(Bearer\s+)[A-Za-z0-9_.+/=\-]+","$1[redacted]");
        text=Regex.Replace(text,@"(?i)(https?://)[^/\s:@]+:[^/@\s]+@","$1[redacted]@");
        text=Regex.Replace(text,@"(?i)([?&](?:token|key|password|secret|access_token)=)[^&\s]+","$1[redacted]");
        text=Regex.Replace(text,@"(?:gh[pousr]_|github_pat_|sk-proj-)[A-Za-z0-9_\-]{20,}","[redacted]");
        return Regex.Replace(text,@"(?im)([A-Za-z_]*(?:PASSWORD|SECRET|API_KEY|PRIVATE_KEY|ACCESS_TOKEN)[A-Za-z_]*\s*[:=]\s*)[^\r\n]+","$1[redacted]");
    }
    private void RegisterPreferences()
    {
        engine.Add(new("app.preferences.read","Read application UI/cache/shortcut preferences, not account credentials. Apply changes with the existing validated preference service.",Schema(),Output(),true,Handler:async(_,token)=>await Ui(()=>preferences!.Snapshot(),token)));
        var fields=GuardFields();fields["application"]=Schema(new(){["themePreset"]=Text(128),["cacheDirectory"]=Text(),["confirmBeforeExit"]=new(){["type"]="boolean"},["automaticRepositoryRefresh"]=new(){["type"]="boolean"},["defaultProjectNotificationsEnabled"]=new(){["type"]="boolean"},["notifyOnFailures"]=new(){["type"]="boolean"},["notifyOnWarnings"]=new(){["type"]="boolean"},["notifyOnSuccesses"]=new(){["type"]="boolean"},["keyboardShortcuts"]=new(){["type"]="object",["additionalProperties"]=Text(128)},["defaultWorkspacePresetByMode"]=new(){["type"]="object",["additionalProperties"]=Text(128)}});
        fields["desktop"]=Schema(new(){["launchAtLogin"]=new(){["type"]="boolean"},["showTrayIcon"]=new(){["type"]="boolean"},["closeToTray"]=new(){["type"]="boolean"},["startHiddenAtLogin"]=new(){["type"]="boolean"}});
        engine.Add(new("app.preferences.save","Persist UI theme, cache location, shortcuts, notifications and tray preferences with the same validation and callbacks as Edit > Preferences. Requires Administration and confirmation.",Schema(fields,"stateToken","target"),Output(),false,Handler:(args,token)=>Mutate(args,"overview",async()=>await preferences!.ExecuteAsync(PreferenceRequest("prefs.save",args))??new{},token),RequiredCapabilities:AutomationCapability.Administration));
        foreach(var command in new[]{"prefs.measureCache","prefs.purgeCache","prefs.scheduleCacheMove","prefs.resetShortcuts"})
        {
            var input=GuardFields();input["expectedRoot"]=Text();if(command=="prefs.scheduleCacheMove")input["destination"]=Text();
            var required=command=="prefs.scheduleCacheMove"?new[]{"stateToken","target","expectedRoot","destination"}:command=="prefs.purgeCache"?new[]{"stateToken","target","expectedRoot"}:new[]{"stateToken","target"};
            engine.Add(new("app.preferences."+command[6..],"Run the existing "+command+" workflow on the reviewed cache/preferences target. Purge requires the exact active root; moving cache is scheduled for restart.",Schema(input,required),Output(),command=="prefs.measureCache",Handler:(args,token)=>Mutate(args,"overview",async()=>{using var cancellation=token.Register(()=>preferences!.Cancel());return await preferences!.ExecuteAsync(PreferenceRequest(command,args))??new{};},token),RequiredCapabilities:AutomationCapability.Administration|(command=="prefs.purgeCache"?AutomationCapability.Destructive:0)));
        }
    }
    private static JsonElement PreferenceRequest(string command,JsonElement args)
    {
        var request=JsonNode.Parse(args.GetRawText())!.AsObject();request.Remove("stateToken");request.Remove("target");request["command"]=command;request["confirmed"]=true;return JsonSerializer.SerializeToElement(request,Json);
    }
}
