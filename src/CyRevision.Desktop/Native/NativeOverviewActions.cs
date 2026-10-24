using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.SystemIntegration;
using CyRevision.Core.Updates;
using CyRevision.Git;

namespace CyRevision.Desktop.Native;

public static class NativeOverviewActions
{
    private sealed class OverviewState
    {
        public Guid? ProjectId;
        public object? Initialization;
        public CancellationTokenSource? Operation;
        public int ModeRevision;
        public bool ModePending;
        public string PreparedPreset = "";
        public string PreparedPresetName = "";
        public bool ModeNeedsIgnore;
        public string ModeIgnoreContent = "";
    }
    private static readonly ConditionalWeakTable<MainWindowViewModel,OverviewState> States = new();
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "overview.openFolder","overview.openRemote","overview.removeRemote","overview.removeProject",
        "overview.serviceStartup","overview.saveLicense","overview.prepareInitialization","overview.initialize",
        "overview.prepareMode","overview.applyMode","overview.cancelMode","overview.installUpdate"
    };
    public static bool Handles(string command) => Commands.Contains(command);
    private static OverviewState State(MainWindowViewModel model)
    {
        OverviewState state = States.GetValue(model,_ => new());
        if (state.ProjectId != model.SelectedProject?.Id)
        {
            state.ProjectId=model.SelectedProject?.Id;
            state.ModePending=false;
        }
        return state;
    }
    public static object Snapshot(MainWindowViewModel model)
    {
        OverviewState state=State(model);
        return new { initialization=state.Initialization,mode=new { revision=state.ModeRevision,pending=state.ModePending,
            needsIgnore=state.ModeNeedsIgnore,ignoreContent=state.ModeIgnoreContent,presetName=state.PreparedPresetName } };
    }
    public static void Cancel(MainWindowViewModel model)
    {
        if(!States.TryGetValue(model,out var state))return;
        try{state.Operation?.Cancel();}catch(ObjectDisposedException){}
    }
    public static async Task<object?> ExecuteAsync(MainWindowViewModel model,JsonElement request,Func<string,IList?> listResolver)
    {
        _=listResolver;
        string command=Text(request,"command");
        if(!Handles(command))throw new ArgumentException("Unknown overview command.");
        OverviewState state=State(model);
        using CancellationTokenSource operation=new();state.Operation=operation;
        try
        {
            switch(command)
            {
                case "overview.openFolder":
                    string folder=model.SelectedProject?.RootPath??throw new InvalidOperationException("Select a project first.");
                    if(!Directory.Exists(folder))throw new DirectoryNotFoundException("The project folder is unavailable.");
                    Process.Start(new ProcessStartInfo{FileName=folder,UseShellExecute=true});
                    break;
                case "overview.openRemote":
                    if(model.SelectedGitRemote is not {CanOpenWebPage:true} remote ||
                        !Uri.TryCreate(remote.WebUrl,UriKind.Absolute,out Uri? uri) || uri.Scheme is not ("http" or "https"))
                        throw new InvalidOperationException("Select a remote with a supported browser page.");
                    Process.Start(new ProcessStartInfo{FileName=uri.AbsoluteUri,UseShellExecute=true});
                    break;
                case "overview.removeRemote":
                    NativePullRequestActions.ValidateConfirmation(request);
                    if(model.SelectedGitRemote?.Name!=Text(request,"remote"))
                        throw new InvalidOperationException("The selected remote changed. Review it again.");
                    await model.RemoveSelectedGitRemoteAsync();
                    break;
                case "overview.removeProject":
                    NativePullRequestActions.ValidateConfirmation(request);
                    await model.RemoveSelectedProjectAsync(Flag(request,"removeGeneratedCaches"));
                    break;
                case "overview.serviceStartup":
                    bool? sync=NullableFlag(request,"sync"),vpn=NullableFlag(request,"vpn");
                    if(sync is null && vpn is null)throw new ArgumentException("Choose a service preference.");
                    await model.SetProjectServiceAutoStartAsync(sync,vpn);
                    break;
                case "overview.saveLicense":
                    NativePullRequestActions.ValidateConfirmation(request);
                    if(!model.CanSaveProjectLicense || model.ProjectLicenseFileName!=Text(request,"fileName"))
                        throw new InvalidOperationException("The license draft or target changed. Review it again.");
                    await model.SaveProjectLicenseAsync(Flag(request,"overwrite"));
                    break;
                case "overview.prepareMode":
                    if(model.SelectedProject is not {} selected || model.SelectedPreset is not {} preset)
                        throw new InvalidOperationException("Select a project mode first.");
                    state.PreparedPreset=PresetKey(model);state.PreparedPresetName=preset.Name;
                    state.ModeNeedsIgnore=preset.Features.GitEnabled && !File.Exists(Path.Combine(selected.RootPath,".gitignore"));
                    state.ModeIgnoreContent=state.ModeNeedsIgnore ? (await Task.Run(()=>GitIgnoreRecommendationService.Build(selected.RootPath),operation.Token)).Content : "";
                    state.ModePending=true;state.ModeRevision++;
                    break;
                case "overview.cancelMode":state.ModePending=false;break;
                case "overview.applyMode":
                    NativePullRequestActions.ValidateConfirmation(request);
                    if(!state.ModePending || state.PreparedPreset!=PresetKey(model) ||
                        !request.TryGetProperty("revision",out var revision) || revision.GetInt32()!=state.ModeRevision)
                        throw new InvalidOperationException("The selected mode changed. Review it again.");
                    string? ignore=null;
                    if(state.ModeNeedsIgnore)
                    {
                        string choice=Text(request,"ignoreChoice");
                        if(choice is not ("create" or "continue"))throw new ArgumentException("Choose whether to create the recommended .gitignore.");
                        if(choice=="create")ignore=request.TryGetProperty("ignoreContent",out var content)?content.GetString():state.ModeIgnoreContent;
                    }
                    await model.ApplySelectedPresetAsync(ignore);
                    state.ModePending=false;
                    break;
                case "overview.prepareInitialization":
                {
                    string root=ValidateDirectory(Text(request,"path"));
                    bool lfsOnly=Flag(request,"lfsOnly");
                    bool repository=HasGit(root);
                    GitToolAvailability tools=await model.GetGitToolAvailabilityAsync(operation.Token);
                    var recommendation=await Task.Run(()=>new{ignore=GitIgnoreRecommendationService.Build(root),lfs=GitLfsRecommendationService.Build(root)},operation.Token);
                    IReadOnlyList<string> existing=await model.GetGitAttributeLfsPatternsAsync(root,operation.Token);
                    string ignorePath=Path.Combine(root,".gitignore");
                    bool hasIgnore=File.Exists(ignorePath);
                    string ignoreContent=hasIgnore ? await ReadSmallText(ignorePath,operation.Token) : recommendation.ignore.Content;
                    GitInitializationFilePreview preview=await ScanPreviewAsync(root,operation.Token);
                    state.Initialization=new{path=root,requestedPath=Text(request,"path"),lfsOnly,hasRepository=repository,hasGitIgnore=hasIgnore,gitIgnoreContent=ignoreContent,
                        tools,detected=recommendation.ignore.DetectionSummary,lfsPatterns=recommendation.lfs.Patterns,existingLfsPatterns=existing,preview};
                    break;
                }
                case "overview.installUpdate":
                {
                    NativePullRequestActions.ValidateConfirmation(request);
                    if(!request.TryGetProperty("nativeProcessId",out var processId) || !processId.TryGetInt32(out int parent) || parent<=0)
                        throw new ArgumentException("The native application process is unavailable.");
                    using(Process native=Process.GetProcessById(parent))
                    {
                        if(native.HasExited || !string.Equals(Path.GetFileName(native.MainModule?.FileName),"CyRevision.Native.exe",StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The native application changed. Retry the update from its current window.");
                    }
                    string package=await model.DownloadAvailableUpdateAsync(operation.Token)
                        ?? throw new InvalidOperationException(model.UpdateStatus);
                    (UpdatePlatform platform,_)=ApplicationUpdateService.DetectCurrentPlatform();
                    ApplicationUpdateLaunchPlan plan=ApplicationUpdateLauncher.CreatePlan(package,parent,platform);
                    await File.WriteAllTextAsync(plan.ScriptPath,plan.ScriptContents,new UTF8Encoding(false),operation.Token);
                    // The frontend, not this engine, starts the helper outside
                    // its kill-on-close job, then closes both application processes.
                    return new{message=model.UpdateStatus,installPlan=new{plan.ExecutablePath,plan.Arguments,plan.ScriptPath,parentProcessId=parent,packagePath=package}};
                }
                case "overview.initialize":
                {
                    NativePullRequestActions.ValidateConfirmation(request);
                    string root=ValidateDirectory(Text(request,"path"));
                    bool lfsOnly=Flag(request,"lfsOnly");
                    GitInitializationOptions options=ReadInitializationOptions(request.GetProperty("options"));
                    ValidateInitialization(root,options,lfsOnly);
                    GitToolAvailability tools=await model.GetGitToolAvailabilityAsync(operation.Token);
                    if(!tools.GitAvailable || !tools.LfsAvailable)throw new InvalidOperationException("Git and Git LFS must both be installed before continuing.");
                    if(lfsOnly)await model.ApplyGitLfsRecommendationsAsync(root,options.LfsPatterns.ToArray(),operation.Token);
                    else await model.CreateGitRepositoryAsync(root,options,operation.Token);
                    state.Initialization=null;
                    break;
                }
            }
            return new{message=model.StatusMessage};
        }
        finally{if(ReferenceEquals(state.Operation,operation))state.Operation=null;}
    }
    private static string PresetKey(MainWindowViewModel model)=>model.SelectedPreset is {} preset
        ? $"{preset.Kind}|{preset.ProviderPluginId}|{preset.PluginModeId}|{preset.Name}" : "";
    private static bool HasGit(string root)=>Directory.Exists(Path.Combine(root,".git")) || File.Exists(Path.Combine(root,".git"));
    private static string ValidateDirectory(string path)
    {
        if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Choose a project folder.");
        string root=Path.GetFullPath(path);
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("The selected project folder does not exist.");
        return root;
    }
    public static GitInitializationOptions ReadInitializationOptions(JsonElement json)
    {
        if(json.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Enter the Git initialization options.");
        string[] patterns=json.TryGetProperty("lfsPatterns",out var supplied) && supplied.ValueKind==JsonValueKind.Array
            ? supplied.EnumerateArray().Select(item=>GitAttributesService.NormalizePattern(item.GetString()??"")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        if(patterns.Length>256)throw new ArgumentException("Choose no more than 256 LFS patterns.");
        string ignore=Text(json,"gitIgnoreContent");
        if(ignore.Length>2*1024*1024)throw new ArgumentException("The .gitignore draft exceeds the editor limit.");
        return new(Flag(json,"createGitIgnore"),ignore,patterns,Text(json,"userName").Trim(),Text(json,"userEmail").Trim(),
            Text(json,"remoteUrl").Trim(),Flag(json,"createInitialCommit"),Text(json,"initialCommitMessage").Trim());
    }
    public static void ValidateInitialization(string root,GitInitializationOptions options,bool lfsOnly)
    {
        bool git=HasGit(root);
        if(!lfsOnly && git)throw new InvalidOperationException("The selected folder is already a Git repository. Open it instead of initializing it again.");
        if(lfsOnly && !git)throw new InvalidOperationException("Open an existing Git repository before applying LFS recommendations.");
        if(!lfsOnly && options.CreateGitIgnore && File.Exists(Path.Combine(root,".gitignore")))
            throw new InvalidOperationException("A .gitignore already exists. It will not be overwritten; review the existing file instead.");
        if(string.IsNullOrWhiteSpace(options.UserName)!=string.IsNullOrWhiteSpace(options.UserEmail))
            throw new ArgumentException("Enter both an author name and email, or leave both blank.");
        if(options.CreateInitialCommit && string.IsNullOrWhiteSpace(options.InitialCommitMessage))
            throw new ArgumentException("Enter an initial commit message.");
    }
    private static async Task<string> ReadSmallText(string path,CancellationToken cancellation)
    {
        if(new FileInfo(path).Length>2*1024*1024)throw new InvalidOperationException("The existing configuration file exceeds the editor limit. It has not been modified.");
        return await File.ReadAllTextAsync(path,cancellation);
    }
    private static Task<GitInitializationFilePreview> ScanPreviewAsync(string root,CancellationToken cancellation)=>Task.Run(()=>
    {
        HashSet<string> excluded=new(StringComparer.OrdinalIgnoreCase){".git",".cyrevision",".vs",".idea","Binaries","DerivedDataCache","Intermediate","Saved","Library","Temp","Obj","node_modules","bin"};
        Stack<string> pending=new();pending.Push(root);List<string> sample=[];int count=0;long bytes=0;
        Stopwatch budget=Stopwatch.StartNew();bool limited=false;
        while(pending.Count>0 && !limited)
        {
            cancellation.ThrowIfCancellationRequested();string directory=pending.Pop();
            if(budget.Elapsed>TimeSpan.FromSeconds(3)){limited=true;break;}
            try
            {
                foreach(string child in Directory.EnumerateDirectories(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(budget.Elapsed>TimeSpan.FromSeconds(3)){limited=true;break;}
                    if(!excluded.Contains(Path.GetFileName(child)) && (File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
                }
                foreach(string file in Directory.EnumerateFiles(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    count++;try{bytes+=new FileInfo(file).Length;}catch(IOException){}
                    if(sample.Count<300)sample.Add(Path.GetRelativePath(root,file).Replace('\\','/'));
                    if(count>=50_000 || budget.Elapsed>TimeSpan.FromSeconds(3)){limited=true;break;}
                }
            }
            catch(UnauthorizedAccessException){}catch(IOException){}
        }
        return new GitInitializationFilePreview(count,bytes,sample,limited);
    },cancellation);
    private static string Text(JsonElement value,string name)=>value.TryGetProperty(name,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()??"":"";
    private static bool Flag(JsonElement value,string name)=>NullableFlag(value,name)==true;
    private static bool? NullableFlag(JsonElement value,string name)=>value.TryGetProperty(name,out var item)&&item.ValueKind is JsonValueKind.True or JsonValueKind.False?item.GetBoolean():null;
}
