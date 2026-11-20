using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyRevision.Backup;
using CyRevision.Code;
using CyRevision.Core.Configuration;
using CyRevision.Git;
using static CyRevision.Automation.AutomationSchema;

namespace CyRevision.Automation;

/// <summary>Explicit, project-bound adapters to the same business services used by the desktop.</summary>
public sealed class RevisionAutomationEngine
{
    private readonly IGitRepositoryService _git;
    private readonly CodeWorkspaceService _code=new();
    private readonly List<AutomationOperation> _operations=[];
    private readonly SemaphoreSlim _writes=new(1,1);
    private readonly string _privateData;
    private readonly FileSystemBackupService _backup;
    private readonly Guid _projectId;
    public string Root {get;}
    public string PrivateData=>_privateData;
    public AutomationPermissions Permissions {get;}
    public IReadOnlyList<AutomationOperation> Operations=>_operations;
    public object Catalogue=>_operations.Select(operation=>new{operation.Name,
        description=operation.Description+(Permissions.ControlOperations?"":" Local access profile: "+Permissions.AccessProfile.Id()+"; no operation grant or confirmation is required. Argument and business validation still apply."),
        operation.InputSchema,operation.OutputSchema,operation.ReadOnly,capabilities=operation.RequiredCapabilities.ToString(),confirmation=Permissions.ControlOperations&&(operation.ConfirmationRequired||!operation.ReadOnly)});
    public event Action<string>? Changed;

    public RevisionAutomationEngine(string root,string privateData,AutomationPermissions permissions,IGitRepositoryService? git=null,string? backupStore=null,Guid? projectId=null)
    {
        Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!Directory.Exists(Root))throw new ArgumentException("The selected project folder does not exist.");
        if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Project roots must not be symbolic links.");
        _privateData=Path.GetFullPath(privateData);Permissions=permissions;_git=git??new GitCliRepositoryService();
        _projectId=projectId??new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(Root))[..16]);
        _backup=new(new BackupStoreOptions(backupStore??Path.Combine(_privateData,"backups")));
        Register();
    }
    public void Add(AutomationOperation operation){if(_operations.Any(op=>op.Name==operation.Name))throw new ArgumentException("Duplicate operation.");_operations.Add(operation);}
    private void Op(string name,string description,Dictionary<string,System.Text.Json.Nodes.JsonObject>? properties=null,string[]? required=null,bool write=false,bool network=false,bool stage=false,bool commit=false,AutomationCapability capabilities=0)
        =>Add(new(name,description,Object(properties,required),Output(),!write,network,stage,commit,RequiredCapabilities:capabilities));
    private static Dictionary<string,System.Text.Json.Nodes.JsonObject> Page()=>new(){["offset"]=Integer(0,10000),["limit"]=Integer(1,1000)};
    private void Register()
    {
        Op("project.info","Read the explicitly selected project folder and available automation features.");
        Op("project.refresh","Invalidate retained text snapshots and external discovery/read caches without replaying operations.");
        Op("git.tools","Check installed Git and LFS versions without changing the project.");
        Op("git.status","Read current branch and changed paths; paginate before requesting more files.",Page());
        Op("git.history","Read commit history, optionally across references. Use offset and limit for large histories.",new(Page()){["allReferences"]=Boolean()});
        Op("git.fileHistory","Read authors and Git/LFS versions for one exact project-relative file.",new(Page()){["path"]=String()},["path"]);
        Op("git.branches","List the real local and remote branches.",Page());
        Op("git.remotes","Read configured repository remotes. URLs containing credentials are masked.");
        Op("git.diff","Read the working-tree text difference for one project-relative file.",new(){["path"]=String()},["path"]);
        Op("git.commitDetails","Read one commit and its changed files.",new(){["revision"]=String(256)},["revision"]);
        Op("git.compare","Compare two exact Git revisions without applying changes.",new(){["from"]=String(256),["to"]=String(256)},["from","to"]);
        Op("git.conflicts","Inspect the current merge conflict state.");
        Op("git.mergeAnalysis","Analyze a possible merge without applying it.",new(){["base"]=String(256),["head"]=String(256)},["base","head"]);
        Op("git.graph","Read a bounded commit graph, not the complete repository object store.",new(){["limit"]=Integer(1,1000),["allReferences"]=Boolean()});
        Op("git.worktrees","Read historical worktrees created for this project.");
        Op("git.lfs.patterns","Read tracked LFS patterns.");
        Op("git.lfs.files","Read tracked LFS file metadata, with pagination.",Page());
        Op("git.lfs.history","Read retained LFS pointer versions for one file, without downloading payloads.",new(Page()){["path"]=String()},["path"]);
        Op("git.stage","Stage only the named paths. Requires write, stage permission and explicit host confirmation.",new(){["paths"]=Paths()},["paths"],write:true,stage:true);
        Op("git.unstage","Unstage only the named paths, retaining working files. Requires confirmation.",new(){["paths"]=Paths()},["paths"],write:true,stage:true);
        Op("git.commit","Create a real revision for the named paths. Requires commit permission and confirmation; never pushes.",new(){["paths"]=Paths(),["message"]=String(32768)},["paths","message"],write:true,stage:true,commit:true);
        Op("git.branch.create","Create a branch from an exact start reference, without checking it out.",new(){["name"]=String(256),["start"]=String(256)},["name","start"],write:true);
        Op("git.remote.set","Add/update a remote after confirmation; remote credentials must come from Git Credential Manager.",new(){["name"]=String(256),["url"]=String()},["name","url"],write:true);
        Op("git.fetch","Fetch remotes explicitly. Requires network/write permission and confirmation.",write:true,network:true);
        Op("git.pull","Pull the configured upstream through the actual Git workflow; unsaved changes remain subject to Git's safety checks.",write:true,network:true,commit:true);
        Op("git.push","Push the current branch to its configured remote, without force. Requires publication, network, write and exact confirmation.",write:true,network:true,capabilities:AutomationCapability.Publication);
        Op("git.checkout","Check out an existing branch without discarding local changes.",new(){["name"]=String(256)},["name"],write:true);
        Op("git.merge","Merge a named branch into the current branch, retaining Git conflict state when applicable.",new(){["name"]=String(256)},["name"],write:true,commit:true);
        Op("git.branch.analyzeRemoval","Inspect retention/worktrees/unpushed commits before removing a local branch.",new(){["name"]=String(256)},["name"]);
        Op("git.branch.remove","Remove only the named local branch through CyRevision's retention safety checks; forceUnretained must be explicitly confirmed.",new(){["name"]=String(256),["forceUnretained"]=Boolean()},["name"],write:true,capabilities:AutomationCapability.Destructive);
        Op("git.remote.remove","Remove the named local remote configuration, not the remote repository.",new(){["name"]=String(256)},["name"],write:true,capabilities:AutomationCapability.Destructive);
        Op("git.conflict.resolve","Resolve one project file using Manual/Base/Ours/Theirs through the real conflict service.",new(){["path"]=String(),["choice"]=new(){["type"]="string",["enum"]=new System.Text.Json.Nodes.JsonArray("Manual","Base","Ours","Theirs")},["text"]=String(1024*1024)},["path","choice"],write:true,stage:true,capabilities:AutomationCapability.Destructive);
        Op("git.conflict.continue","Continue the current resolved Git operation, preserving Git's validation and commit behavior.",write:true,commit:true);
        Op("git.conflict.abort","Abort the active Git conflict operation. Requires destructive permission and confirmation.",write:true,capabilities:AutomationCapability.Destructive);
        Op("git.lfs.track","Add an LFS tracking pattern after confirmation.",new(){["pattern"]=String(512)},["pattern"],write:true);
        Op("git.lfs.untrack","Remove an LFS tracking pattern after confirmation.",new(){["pattern"]=String(512)},["pattern"],write:true);
        Op("files.list","List one folder inside the host-selected project, excluding private data and links.",new(Page()){["path"]=String()});
        Op("file.read","Read a bounded byte range of a selected file. Base64 preserves exact bytes; follow nextOffset for complete content.",new(){["path"]=String(),["offset"]=Integer(0,int.MaxValue),["length"]=Integer(1,131072)},["path"]);
        Op("file.preview","Use CyRevision's actual language-aware file preview service.",new(){["path"]=String()},["path"]);
        Op("file.write","Write a UTF-8 project file after exact-hash verification, backup and confirmation. expectedSha256 must be 'missing' for a new file.",new(){["path"]=String(),["text"]=String(1024*1024),["expectedSha256"]=String(64)},["path","text","expectedSha256"],write:true);
        Op("code.search","Search project text through CyRevision's search service; native grep/ripgrep selection follows the host service.",new(){["text"]=String(4096),["caseSensitive"]=Boolean(),["limit"]=Integer(1,500)},["text"]);
        Op("backup.list","Read snapshots from the configured backup store.",Page());
        Op("backup.create","Create a deduplicated snapshot with permanent retention, without deleting older versions.",write:true);
        Op("backup.manifest","Read the manifest for an existing backup snapshot.",new(){["snapshotId"]=String(36)},["snapshotId"]);
        Op("backup.restoreFile","Restore one snapshot file to an absent project-relative destination; never overwrites.",new(){["snapshotId"]=String(36),["path"]=String(),["destination"]=String()},["snapshotId","path","destination"],write:true);
    }
    public bool IsAllowed(string name)
    {
        if(Permissions.Blocked||!Permissions.ScopeValid()||(Permissions.ControlOperations&&!Permissions.Read))return false;
        if(name.StartsWith("git.",StringComparison.Ordinal)&&!Permissions.GitEnabled()||name.StartsWith("backup.",StringComparison.Ordinal)&&!Permissions.BackupEnabled())return false;
        if(name is "resources.read" or "resources.grep")return true;
        var op=_operations.FirstOrDefault(value=>value.Name==name);if(op is null)return false;
        return (op.Available?.Invoke()??true)&&(!Permissions.ControlOperations||
            (op.ReadOnly||Permissions.Write)&&(!op.Network||Permissions.Network)&&(!op.Stage||Permissions.Stage)&&(!op.Commit||Permissions.Commit)&&
            (Permissions.Capabilities&op.RequiredCapabilities)==op.RequiredCapabilities);
    }
    public bool CanCallRemote(string name,bool readOnly)=>!Permissions.Blocked&&Permissions.ScopeValid()&&Permissions.RemoteAllowed(name)&&
        (!Permissions.ControlOperations||Permissions.RemoteTools&&Permissions.Read&&(readOnly||Permissions.Write));
    public async Task ConfirmRemoteAsync(string name,JsonElement arguments,bool readOnly,CancellationToken token)
    {
        if(!CanCallRemote(name,readOnly))throw new AutomationException("AccessDenied","External tools are blocked by the host policy.");
        if(Permissions.ControlOperations&&!await Permissions.ConfirmAsync(name,arguments,token).ConfigureAwait(false))throw new AutomationException("ConfirmationRequired","External tool invocation requires explicit host approval.");
        if(!CanCallRemote(name,readOnly))throw new AutomationException("AccessDenied","External tool permissions changed before execution.");
    }
    public async Task<object> ExecuteAsync(string name,JsonElement arguments,CancellationToken token)
    {
        if(!IsAllowed(name))throw new AutomationException("AccessDenied","The operation is not allowed for this project/session.");
        var operation=_operations.First(value=>value.Name==name);
        bool serialized=!operation.ReadOnly||operation.ConfirmationRequired;
        if(serialized){
            if(Permissions.ControlOperations&&!await Permissions.ConfirmAsync(name,arguments,token).ConfigureAwait(false))throw new AutomationException("ConfirmationRequired","Confirm this exact operation in the host, or use --confirm for a direct CLI invocation.");
            if(!IsAllowed(name))throw new AutomationException("AccessDenied","The host permissions changed before execution.");
            if(!await _writes.WaitAsync(0,token).ConfigureAwait(false))throw new AutomationException("Busy","Another automation write is active for this project.");
        }
        try{
            token.ThrowIfCancellationRequested();object value=operation.Handler is not null?await operation.Handler(arguments,token).ConfigureAwait(false):await ExecuteBusinessAsync(name,arguments,token).ConfigureAwait(false);
            if(!operation.ReadOnly)Changed?.Invoke(name);
            return new{value};
        }finally{if(serialized)_writes.Release();}
    }
    private static string Text(JsonElement args,string name,string fallback="")=>args.TryGetProperty(name,out var value)?value.GetString()??fallback:fallback;
    private static int Number(JsonElement args,string name,int fallback)=>args.TryGetProperty(name,out var value)?value.GetInt32():fallback;
    private static bool Flag(JsonElement args,string name)=>args.TryGetProperty(name,out var value)&&value.GetBoolean();
    private static object Paginate<T>(IReadOnlyList<T> items,JsonElement args){int offset=Number(args,"offset",0),limit=Number(args,"limit",100);return new{items=items.Skip(offset).Take(limit).ToArray(),total=items.Count,nextOffset=offset+limit<items.Count?(int?)(offset+limit):null};}
    private string[] SelectedPaths(JsonElement args)=>args.GetProperty("paths").EnumerateArray().Select(value=>Relative(TextValue(value))).ToArray();
    private static string TextValue(JsonElement value)=>value.GetString()??throw new ArgumentException("Expected a path.");
    private string Relative(string path){Resolve(path);return path.Replace('\\','/');}
    public string Resolve(string relative,bool allowRoot=false)
    {
        if(!allowRoot&&string.IsNullOrWhiteSpace(relative))throw new ArgumentException("A project-relative path is required.");
        if(Path.IsPathRooted(relative)||relative.Contains(':')||relative.Contains('\0'))throw new UnauthorizedAccessException("Only relative project paths are accepted.");
        string normalized=relative.Replace('\\','/');
        foreach(string part in normalized.Split('/',StringSplitOptions.RemoveEmptyEntries)){
            if(new[]{".git",".cyrevision",".codex","secrets","..","id_rsa","id_ed25519"}.Contains(part,StringComparer.OrdinalIgnoreCase)||part.StartsWith(".env",StringComparison.OrdinalIgnoreCase)||new[]{".pfx",".p12",".pem",".key"}.Any(suffix=>part.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)))throw new UnauthorizedAccessException("This private or escaping path is not exposed.");
        }
        string full=Path.GetFullPath(Path.Combine(Root,normalized));
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        if(!full.StartsWith(Root+Path.DirectorySeparatorChar,comparison)&&!(allowRoot&&full.Equals(Root,comparison)))throw new UnauthorizedAccessException("The path escapes the selected project.");
        for(string? current=full;current is not null&&current.Length>=Root.Length;current=Path.GetDirectoryName(current))if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Symbolic links and reparse points are not exposed.");
        return full;
    }
    private async Task<object> ExecuteBusinessAsync(string name,JsonElement args,CancellationToken token)
    {
        int offset=Number(args,"offset",0),limit=Number(args,"limit",100);
        switch(name){
            case "project.info":return new{root=Root,name=Path.GetFileName(Root),sdkVersion="0.7.1",sdkRevision="34d4bcae126bc83d1a2925b5047dac36e3d09d92",accessProfile=Permissions.AccessProfile.Id(),operations=_operations.Count};
            case "project.refresh":return new{refreshed=true};
            case "git.tools":return await _git.GetToolAvailabilityAsync(token);
            case "git.status":{var status=await _git.GetDetailedStatusAsync(Root,token);return new{status.CurrentBranch,status.AheadBy,status.BehindBy,changes=Paginate(status.Changes,args)};}
            case "git.history":return Paginate(Flag(args,"allReferences")?await _git.GetHistoryAcrossRefsAsync(Root,offset+limit,token):await _git.GetHistoryAsync(Root,offset+limit,token),args);
            case "git.fileHistory":return Paginate(await _git.GetFileHistoryAsync(Root,Relative(Text(args,"path")),offset+limit,token),args);
            case "git.branches":return Paginate(await _git.GetBranchesAsync(Root,token),args);
            case "git.remotes":return new{items=(await _git.GetRemotesAsync(Root,token)).Select(remote=>new{remote.Name,url=SanitizeUrl(remote.FetchUrl),pushUrl=SanitizeUrl(remote.PushUrl)})};
            case "git.diff":return new{text=await _git.GetDiffAsync(Root,Relative(Text(args,"path")),cancellationToken:token)};
            case "git.commitDetails":return await _git.GetCommitDetailsAsync(Root,Text(args,"revision"),token);
            case "git.compare":return await _git.CompareCommitsAsync(Root,Text(args,"from"),Text(args,"to"),token);
            case "git.conflicts":return await _git.GetConflictStateAsync(Root,token);
            case "git.mergeAnalysis":return await _git.AnalyzeMergeConflictsAsync(Root,Text(args,"base"),Text(args,"head"),token);
            case "git.graph":return new{items=await _git.GetCommitGraphAsync(Root,limit,Flag(args,"allReferences"),token)};
            case "git.worktrees":return new{items=await _git.GetHistoricalWorktreesAsync(Root,token)};
            case "git.lfs.patterns":return new{items=await _git.GetLfsPatternsAsync(Root,token)};
            case "git.lfs.files":return Paginate(await _git.GetLfsTrackedFilesAsync(Root,token),args);
            case "git.lfs.history":return Paginate(await _git.GetLfsFileVersionsAsync(Root,Relative(Text(args,"path")),offset+limit,token),args);
            case "git.stage":await _git.StageAsync(Root,SelectedPaths(args),token);return new{staged=true};
            case "git.unstage":await _git.UnstageAsync(Root,SelectedPaths(args),token);return new{unstaged=true};
            case "git.commit":await _git.CreateRevisionAsync(Root,Text(args,"message"),SelectedPaths(args),token);return new{committed=true};
            case "git.branch.create":await _git.CreateBranchFromAsync(Root,Text(args,"name"),Text(args,"start"),token);return new{created=true};
            case "git.remote.set":{string url=Text(args,"url");if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&(!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)))throw new ArgumentException("Use credential-free remote URLs and Git Credential Manager.");await _git.AddOrUpdateRemoteAsync(Root,Text(args,"name"),url,token);return new{updated=true};}
            case "git.fetch":await _git.FetchAsync(Root,token);return new{fetched=true};
            case "git.pull":await _git.PullAsync(Root,token);return new{pulled=true};
            case "git.push":await _git.PushAsync(Root,token);return new{pushed=true};
            case "git.checkout":await _git.CheckoutBranchAsync(Root,Text(args,"name"),token);return new{checkedOut=true};
            case "git.merge":await _git.MergeBranchAsync(Root,Text(args,"name"),token);return new{merged=true};
            case "git.branch.analyzeRemoval":return await _git.AnalyzeLocalBranchRemovalAsync(Root,Text(args,"name"),token);
            case "git.branch.remove":await _git.RemoveLocalBranchAsync(Root,Text(args,"name"),Flag(args,"forceUnretained"),token);return new{removed=true};
            case "git.remote.remove":await _git.RemoveRemoteAsync(Root,Text(args,"name"),token);return new{removed=true};
            case "git.conflict.resolve":await _git.ResolveConflictAsync(Root,Relative(Text(args,"path")),Enum.Parse<GitConflictResolutionChoice>(Text(args,"choice")),args.TryGetProperty("text",out var manual)?manual.GetString():null,token);return new{resolved=true};
            case "git.conflict.continue":await _git.ContinueConflictOperationAsync(Root,token);return new{continued=true};
            case "git.conflict.abort":await _git.AbortConflictOperationAsync(Root,token);return new{aborted=true};
            case "git.lfs.track":await _git.TrackLfsPatternAsync(Root,Text(args,"pattern"),token);return new{updated=true};
            case "git.lfs.untrack":await _git.UntrackLfsPatternAsync(Root,Text(args,"pattern"),token);return new{updated=true};
            case "files.list":{string folder=Resolve(Text(args,"path"),true);var entries=new List<object>();foreach(string file in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.OrdinalIgnoreCase)){token.ThrowIfCancellationRequested();try{string relative=Path.GetRelativePath(Root,file);Resolve(relative);entries.Add(new{path=relative.Replace('\\','/'),directory=Directory.Exists(file)});}catch(UnauthorizedAccessException){}}return Paginate(entries,args);}
            case "file.read":return await ReadAsync(Resolve(Text(args,"path")),offset,Number(args,"length",32768),token);
            case "file.preview":return await _code.ReadPreviewAsync(Root,Relative(Text(args,"path")),token);
            case "file.write":return await WriteAsync(args,token);
            case "code.search":{var result=await _code.SearchAsync(Root,Text(args,"text"),new CodeSearchOptions(MatchCase:Flag(args,"caseSensitive"),MaximumResults:Math.Min(limit,500)),token);return new{items=result.Results.Where(item=>IsExposedPath(item.RelativePath)).ToArray()};}
            case "backup.list":return Paginate(await _backup.GetSnapshotsAsync(_projectId,token),args);
            case "backup.create":return await _backup.CreateSnapshotAsync(_projectId,Root,RetentionPolicy.KeepForever,token);
            case "backup.manifest":return await OwnedManifestAsync(Guid.Parse(Text(args,"snapshotId")),token);
            case "backup.restoreFile":{string destination=Resolve(Text(args,"destination"));if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("Restore destinations must be absent.");Relative(Text(args,"path"));Guid snapshot=Guid.Parse(Text(args,"snapshotId"));await OwnedManifestAsync(snapshot,token);await _backup.RestoreFileAsync(snapshot,Text(args,"path"),destination,token);return new{restored=true};}
            default:throw new AutomationException("UnknownOperation","The operation is not exposed by this host.");
        }
    }
    private async Task<BackupManifest> OwnedManifestAsync(Guid id,CancellationToken token)
    {
        var manifest=await _backup.GetManifestAsync(id,token);
        if(manifest.Snapshot.ProjectId!=_projectId)throw new AutomationException("AccessDenied","The snapshot does not belong to the selected project.");
        return manifest;
    }
    private static string SanitizeUrl(string url){if(!Uri.TryCreate(url,UriKind.Absolute,out var uri))return url;var builder=new UriBuilder(uri){UserName="",Password="",Query=""};return builder.Uri.ToString();}
    private bool IsExposedPath(string path){try{Resolve(path);return true;}catch(UnauthorizedAccessException){return false;}}
    private static async Task<object> ReadAsync(string file,int offset,int length,CancellationToken token)
    {
        await using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,8192,true);stream.Position=Math.Min(offset,stream.Length);byte[] buffer=new byte[length];int count=await stream.ReadAsync(buffer,token);
        long? next=stream.Position<stream.Length?stream.Position:null;stream.Position=0;string sha256=Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).ToLowerInvariant();
        return new{offset,length=count,totalBytes=stream.Length,nextOffset=next,sha256,text=Encoding.UTF8.GetString(buffer,0,count),bytesBase64=Convert.ToBase64String(buffer,0,count)};
    }
    private async Task<object> WriteAsync(JsonElement args,CancellationToken token)
    {
        string file=Resolve(Text(args,"path")),expected=Text(args,"expectedSha256");
        string current=File.Exists(file)?Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file,token))).ToLowerInvariant():"missing";
        if(!string.Equals(current,expected,StringComparison.OrdinalIgnoreCase))throw new AutomationException("StaleState","The file changed; read it again before explicitly confirming a new write.");
        string? backup=null;
        if(current!="missing"){string directory=Path.Combine(_privateData,"recovery");Directory.CreateDirectory(directory);backup=Path.Combine(directory,Guid.NewGuid().ToString("N")+".backup");File.Copy(file,backup,false);}
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temporary=file+".cyrevision-"+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllTextAsync(temporary,Text(args,"text"),new UTF8Encoding(false),token);Resolve(Text(args,"path"));token.ThrowIfCancellationRequested();string latest=File.Exists(file)?Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file,token))).ToLowerInvariant():"missing";if(latest!=current)throw new AutomationException("StaleState","The file changed during preparation; the write was not applied.");File.Move(temporary,file,current!="missing");}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        return new{written=true,sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(args,"text")))) .ToLowerInvariant(),recoveryCopy=backup};
    }
}
