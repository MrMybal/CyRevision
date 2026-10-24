using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Desktop.Native;

/// <summary>Native interaction adapters. All repository mutations use the existing, tested workflows.</summary>
public static class NativeGitActions
{
    private sealed class Session
    {
        public Guid? ProjectId;
        public BranchFileExplorerViewModel? Branch;
        public readonly ConditionalWeakTable<GitChangeTreeNode, ChangeRow> Changes = new();
        public readonly ConditionalWeakTable<GitRevisionTreeNode, BranchRow> BranchRows = new();
        public readonly ConditionalWeakTable<GitRevisionFile, BranchRow> BranchFileRows = new();
        public readonly ConditionalWeakTable<LfsFileTreeNode, LfsFolderRow> LfsRows = new();
        public readonly HashSet<string> ExpandedLfsDirectories = new(StringComparer.Ordinal);
        public readonly HashSet<string> ExpandedBranchDirectories = new(StringComparer.Ordinal);
        public IReadOnlyList<GitLocalBranchRemovalAnalysis> Removals = [];
    }

    public sealed class ChangeRow(GitChangeTreeNode node)
    {
        internal GitChangeTreeNode Node { get; } = node;
        internal GitChangeTreeNode? ParentNode { get; set; }
        public string Name => Node.GroupKind switch
        {
            "tracked" when Depth == 0 => "Versioned files",
            "untracked" when Depth == 0 => "Unversioned files",
            "local" when Depth == 0 => "Local files",
            _ => Node.Name
        };
        public string Path => Node.RelativePath;
        public int Depth { get; internal set; }
        public bool IsDirectory => Node.IsDirectory;
        public bool IsPlaceholder => Node.IsPlaceholder;
        public bool IsExpanded => Node.IsExpanded;
        public bool CanInclude => Node.CanIncludeRecursively;
        public bool? IsIncluded => Node.IsIncluded;
        public bool IsLocalOnly => Node.GroupKind == "local" || Node.Change?.IsLocalOnly == true;
        public int Count => Node.LeafCount;
        public string State => Node.Change?.State ?? Node.Detail;
        public string Area => Node.Change?.Area ?? "";
        public string LockOwner => Node.Change?.LockOwner ?? "";
        public string Lfs => Node.Change?.Lfs ?? "";
        public string Preparation => Node.Change?.PreparationState ?? "";
    }

    public sealed class BranchRow
    {
        public BranchRow(GitRevisionTreeNode node) { Node = node; }
        public BranchRow(GitRevisionFile file) { File = file; }
        internal GitRevisionTreeNode? Node { get; }
        private GitRevisionFile? File { get; }
        public string Name => Node?.Name ?? File?.Name ?? "";
        public string Path => Node?.Path ?? File?.Path ?? "";
        public int Depth { get; internal set; }
        public bool IsDirectory => Node?.IsDirectory == true;
        public bool IsExpanded { get; internal set; }
        public string Size => Node?.Detail ?? File?.SizeText ?? "";
        public string ObjectId => (Node?.File ?? File)?.ObjectId ?? "";
        public string Kind => (Node?.File ?? File)?.ObjectType ?? "Directory";
    }

    public sealed record GraphRow(string Hash, string ShortHash, string Parents, string AuthorName,
        DateTimeOffset AuthoredAt, string Subject, string Decorations);
    public sealed record DailyRow(string Day, int CommitCount, int FilesTouched, long AddedLines, long DeletedLines);

    public sealed class LfsFolderRow(LfsFileTreeNode node)
    {
        internal LfsFileTreeNode Node { get; } = node;
        public string Name => Node.Name;
        public string Path => Node.RelativePath;
        public bool IsDirectory => Node.IsFolder;
        public int Depth { get; internal set; }
        public bool IsExpanded { get; internal set; }
        public string Kind => Node.File?.Kind.ToString() ?? "Directory";
        public string Size => Node.File?.SizeText ?? "";
        public string Availability => Node.File?.Availability.ToString() ?? "";
    }

    private static readonly ConditionalWeakTable<MainWindowViewModel, Session> Sessions = new();
    private static Session For(MainWindowViewModel model)
    {
        Session session = Sessions.GetOrCreateValue(model);
        if (session.ProjectId != model.SelectedProject?.Id)
        {
            session.Branch?.CancelOperation();
            session.ProjectId = model.SelectedProject?.Id;
            session.Removals = [];
            session.ExpandedLfsDirectories.Clear();
        }
        return session;
    }

    public static bool Handles(string command) => command is "gitChange" or "gitRows" or "gitBranchInspector" or "gitBranches" or "gitComposition" or "gitLfsTree" or "gitLfsPrune" or "gitConflict";

    public static IList? GetList(MainWindowViewModel model, string name)
    {
        Session session = For(model);
        if (NativeConflictActions.GetList(model, name) is IList conflicts) return conflicts;
        if (name is "NativeGitChanges" or "NativeGitFolders")
        {
            List<ChangeRow> result = [];
            foreach (GitChangeTreeNode node in name == "NativeGitFolders" ? model.ChangeTree : model.FlatChangeTree)
                AppendChanges(node, 0, session, result);
            return result;
        }
        if (name == "NativeBranchFiles")
        {
            List<BranchRow> result = [];
            if (session.Branch is { } branch && session.ProjectId == model.SelectedProject?.Id)
            {
                if (!string.IsNullOrWhiteSpace(branch.Search))
                    foreach (GitRevisionFile file in branch.FilteredFiles) result.Add(session.BranchFileRows.GetValue(file, item => new BranchRow(item)));
                else foreach (GitRevisionTreeNode node in branch.TreeRoots) AppendBranch(node, 0, session, result);
            }
            return result;
        }
        if (name == "NativeBranchHistory") return (IList?)session.Branch?.OperationHistory ?? Array.Empty<object>();
        if (name == "NativeLfsFolders")
        {
            List<LfsFolderRow> result = [];
            void Append(LfsFileTreeNode node, int depth)
            {
                var row = session.LfsRows.GetValue(node, item => new LfsFolderRow(item));
                row.Depth = depth; row.IsExpanded = session.ExpandedLfsDirectories.Contains(node.RelativePath);
                result.Add(row);
                if (row.IsExpanded) foreach (var child in node.Children) Append(child, depth + 1);
            }
            foreach (var node in model.LfsFileTree) Append(node, 0);
            return result;
        }
        if (name == "NativeBranchRemovals") return session.Removals.ToArray();
        if (name == "NativeGitGraph") return model.GitGraphCommits.Select(commit => new GraphRow(commit.Hash,
            commit.ShortHash, string.Join(' ', commit.ParentHashes), commit.AuthorName, commit.AuthoredAt,
            commit.Subject, commit.Decorations)).ToArray();
        if (name == "NativeGitDailyActivity") return model.GitDailyActivity.Select(day => new DailyRow(
            day.Day.ToString("yyyy-MM-dd"), day.CommitCount, day.FilesTouched, day.AddedLines, day.DeletedLines)).ToArray();
        return null;
    }

    private static void AppendChanges(GitChangeTreeNode node, int depth, Session session, List<ChangeRow> result, GitChangeTreeNode? parent = null)
    {
        ChangeRow row = session.Changes.GetValue(node, item => new ChangeRow(item));
        row.Depth = depth;
        row.ParentNode = parent;
        result.Add(row);
        if (!node.IsExpanded) return;
        // State polling must not silently append another 500-file page on every refresh.
        // Only an unopened placeholder is initialized here; subsequent pages require a user action.
        if (NeedsInitialLoad(node)) node.EnsureChildrenLoaded();
        foreach (GitChangeTreeNode child in node.Children) AppendChanges(child, depth + 1, session, result, node);
    }

    internal static bool NeedsInitialLoad(GitChangeTreeNode node) =>
        node.HasUnloadedChildren && node.Children.Count == 1 && node.Children[0].IsPlaceholder;

    private static void AppendBranch(GitRevisionTreeNode node, int depth, Session session, List<BranchRow> result)
    {
        BranchRow row = session.BranchRows.GetValue(node, item => new BranchRow(item));
        row.Depth = depth;
        row.IsExpanded = session.ExpandedBranchDirectories.Contains(node.Path);
        result.Add(row);
        if (row.IsExpanded) foreach (GitRevisionTreeNode child in node.Children) AppendBranch(child, depth + 1, session, result);
    }

    public static Dictionary<string, object?> GetFields(MainWindowViewModel model, string? workspace = null)
    {
        Session session = For(model);
        BranchFileExplorerViewModel? branch = session.Branch;
        var fields = new Dictionary<string, object?>
        {
            ["BranchInspectorOpen"] = branch is not null,
            ["BranchInspectorTitle"] = branch?.WindowTitle ?? "Branch files",
            ["BranchInspectorSummary"] = branch?.Summary ?? "Choose a branch and browse its files without switching the working branch.",
            ["BranchInspectorStatus"] = branch?.Status ?? "",
            ["BranchInspectorProgress"] = branch?.OperationPercent ?? 0,
            ["BranchInspectorIndeterminate"] = branch?.OperationIsIndeterminate ?? false,
            ["BranchInspectorBusy"] = branch?.HasCancelableOperation ?? false,
            ["BranchInspectorCanRefreshRemote"] = branch?.CanRefreshRemote ?? false,
            ["BranchInspectorStage"] = branch?.OperationSummary ?? "",
            ["BranchInspectorDetail"] = branch?.OperationDetail ?? "",
            ["BranchInspectorPreview"] = branch?.PreviewText ?? "",
            ["BranchInspectorPreviewSummary"] = branch?.PreviewSummary ?? "",
            ["BranchInspectorDiff"] = branch?.DiffText ?? "",
            ["BranchInspectorDiffSummary"] = branch?.DiffSummary ?? "",
            ["BranchInspectorSelected"] = branch?.SelectedFileSummary ?? "",
            ["BranchInspectorSearch"] = branch?.Search ?? "",
            ["BranchRemovalSummary"] = string.Join("\n", session.Removals.Select(item => $"{item.BranchName}: {item.SafetyMessage}"))
        };
        if (model.SelectedBranchDetails is { } details)
        {
            fields["BranchDetailsBase"] = details.ComparisonBaseText;
            fields["BranchDetailsAhead"] = details.UniqueCommitText;
            fields["BranchDetailsCreator"] = details.CreatorText;
            fields["BranchDetailsCreated"] = details.CreatedText;
            fields["BranchDetailsUpdated"] = details.UpdatedText;
            fields["BranchDetailsCommit"] = details.LastSubject;
            fields["BranchDetailsAuthor"] = details.LastAuthorName;
        }
        if (workspace == "graphs")
        {
            // The graph is already limited by the analysis workflow (at most 1,000 commits).
            // Transport all its edges together: paging the nodes would sever parent connections.
            fields["NativeGraphNodes"] = model.GitGraphCommits.Select(commit => new GraphRow(commit.Hash,
                commit.ShortHash, string.Join(' ', commit.ParentHashes), commit.AuthorName, commit.AuthoredAt,
                commit.Subject, commit.Decorations)).ToArray();
            fields["NativeGraphDays"] = model.GitDailyActivity.Select(day => new DailyRow(day.Day.ToString("yyyy-MM-dd"),
                day.CommitCount, day.FilesTouched, day.AddedLines, day.DeletedLines)).ToArray();
            fields["NativeGraphRelations"] = model.GitFileRelations.Take(2000).ToArray();
            fields["NativeUnrealRelations"] = model.UnrealDependencyRelations.Take(2000).ToArray();
        }
        foreach (var field in NativeConflictActions.Fields(model)) fields[field.Key] = field.Value;
        return fields;
    }

    public static async ValueTask DisposeAsync(MainWindowViewModel model)
    {
        NativeConflictActions.Dispose(model);
        if (!Sessions.TryGetValue(model, out Session? session)) return;
        BranchFileExplorerViewModel? branch = session.Branch;
        session.Branch = null;
        session.ExpandedBranchDirectories.Clear();
        session.Removals = [];
        if (branch is not null) await branch.DisposeAsync();
    }

    public static Bitmap? GetImage(MainWindowViewModel model, string name)
    {
        BranchFileExplorerViewModel? branch = For(model).Branch;
        return name switch
        {
            "BranchInspectorPreviewImage" => branch?.PreviewImage,
            "BranchInspectorDiffBaselineImage" => branch?.DiffBaselineImage,
            "BranchInspectorDiffCandidateImage" => branch?.DiffCandidateImage,
            "BranchInspectorDiffHeatmapImage" or "BranchInspectorDiffPreviewImage" => branch?.DiffHeatmapImage,
            "DiffPreviewImage" => model.DiffPreviewImage,
            "ExplorerDiffPreviewImage" => model.ExplorerDiffPreviewImage,
            "MultiRestoreDiffPreviewImage" => model.MultiRestoreDiffPreviewImage,
            "LfsPreview" => model.LfsPreview,
            _ => null
        };
    }

    public static async Task<object?> ExecuteAsync(MainWindowViewModel model, JsonElement request, Func<string, IList?> listResolver)
    {
        if (model.SelectedProject?.Definition.Features.GitEnabled != true)
            throw new InvalidOperationException("Git is not enabled for the selected project.");
        return Text(request, "command") switch
        {
            "gitChange" => await ChangeAsync(model, request),
            "gitRows" => await RowsAsync(model, request, listResolver),
            "gitBranchInspector" => await InspectAsync(model, request),
            "gitBranches" => await BranchesAsync(model, request),
            "gitComposition" => await ComposeAsync(model, request),
            "gitLfsTree" => LfsTree(model, request),
            "gitConflict" => await NativeConflictActions.ExecuteAsync(model, request),
            "gitLfsPrune" => await PruneAsync(model, request),
            _ => throw new ArgumentException("Unknown Git interaction.")
        };
    }

    private static async Task<object?> PruneAsync(MainWindowViewModel model, JsonElement request)
    {
        bool dryRun = Bool(request, "dryRun");
        if (!dryRun) RequireConfirmation(request);
        await model.RunNativeLfsPruneAsync(dryRun);
        return new { message = model.LfsStorageSummary };
    }

    private static object LfsTree(MainWindowViewModel model, JsonElement request)
    {
        Session session = For(model);
        LfsFolderRow[] rows = ResolveKeys<LfsFolderRow>(GetList(model, "NativeLfsFolders")!, Strings(request, "keys"));
        if (Text(request, "operation") == "expand")
        {
            foreach (var row in rows.Where(row => row.IsDirectory))
                if (Bool(request, "expanded")) session.ExpandedLfsDirectories.Add(row.Path);
                else session.ExpandedLfsDirectories.Remove(row.Path);
        }
        else if (Text(request, "operation") == "select")
        {
            if (rows.Length != 1 || rows[0].IsDirectory) throw new InvalidOperationException("Choose a versioned LFS file.");
            model.SelectedLfsFileTreeNode = rows[0].Node;
        }
        else throw new ArgumentException("Unknown LFS folder operation.");
        return new { message = model.LfsTimelineSummary };
    }

    private static async Task<object?> ComposeAsync(MainWindowViewModel model, JsonElement request)
    {
        if (Text(request, "operation") != "loadRestore") throw new ArgumentException("Unknown composition operation.");
        await model.LoadMultiRestoreCommitAsync();
        return new { message = model.StatusMessage };
    }

    private static async Task<object?> ChangeAsync(MainWindowViewModel model, JsonElement request)
    {
        string source = Text(request, "source", "NativeGitChanges");
        if (source is not ("NativeGitChanges" or "NativeGitFolders")) throw new ArgumentException("Unknown change view.");
        string operation = Text(request, "operation");
        ChangeRow[] rows = ResolveKeys<ChangeRow>(GetList(model, source)!, Strings(request, "keys"));
        if (rows.Length == 0) throw new InvalidOperationException("Select one or more files or folders first.");
        if (operation == "loadMore")
        {
            foreach (ChangeRow row in rows)
                if (row.Node.IsPlaceholder && row.ParentNode is { } parent) parent.EnsureChildrenLoaded();
                else throw new InvalidOperationException("This row does not contain another file page.");
            return new { message = "Additional files loaded" };
        }
        if (operation == "expand")
        {
            foreach (ChangeRow row in rows)
            {
                if (row.Node.IsPlaceholder) throw new InvalidOperationException("Open the containing file page to load more files.");
                row.Node.IsExpanded = Bool(request, "expanded");
                if (row.Node.IsExpanded) row.Node.EnsureChildrenLoaded();
            }
            return new { message = "Folder visibility updated" };
        }
        if (operation == "select")
        {
            model.SelectedChangeTreeNode = rows[0].Node;
            return new { message = "Selection updated" };
        }
        if (operation == "include")
        {
            foreach (ChangeRow row in rows) model.SetChangeTreeNodeIncluded(row.Node, Bool(request, "included"));
            return new { message = "Commit inclusion updated for the selected files and all folder descendants" };
        }
        GitChangeViewModel[] changes = rows.SelectMany(row => row.Node.ContainedChanges)
            .DistinctBy(change => change.Path, StringComparer.Ordinal).ToArray();
        if (changes.Length == 0) throw new InvalidOperationException("There are no eligible files in the selection.");
        switch (operation)
        {
            case "local": await model.SetChangesLocalOnlyAsync(changes, true); break;
            case "restore": await model.SetChangesLocalOnlyAsync(changes, false); break;
            case "rollback": RequireConfirmation(request); await model.RollbackChangesAsync(changes); break;
            case "delete": RequireConfirmation(request); await model.DeleteChangesAsync(changes); break;
            default: throw new ArgumentException("Unknown change operation.");
        }
        return new { message = model.StatusMessage };
    }

    private static async Task<object?> RowsAsync(MainWindowViewModel model, JsonElement request, Func<string, IList?> resolver)
    {
        string source = Text(request, "source");
        IList list = resolver(source) ?? throw new ArgumentException("Unknown list.");
        object[] rows = ResolveKeys<object>(list, Strings(request, "keys"));
        if (rows.Length == 0) throw new InvalidOperationException("Select at least one row.");
        bool selected = Bool(request, "selected");
        switch (source)
        {
            case "MultiRestoreFiles":
                GitRestorePoint? point = request.TryGetProperty("restorePoint", out var supplied)
                    ? Enum.TryParse(supplied.GetString(), out GitRestorePoint parsed) && Enum.IsDefined(parsed)
                        ? parsed : throw new ArgumentException("Unknown restore point.") : null;
                foreach (MultiRestoreFileViewModel row in rows.Cast<MultiRestoreFileViewModel>())
                { if (point is not null) row.RestorePoint = point.Value; else row.IsSelected = selected; }
                break;
            case "BranchComparisonCommits":
                foreach (CherryPickCommitViewModel row in rows.Cast<CherryPickCommitViewModel>()) row.IsSelected = selected;
                break;
            case "IgnoreFolderSuggestions":
            case "IgnoreFileTypeSuggestions":
                foreach (IgnoreSuggestionViewModel row in rows.Cast<IgnoreSuggestionViewModel>()) row.IsSelected = selected;
                break;
            case "FilteredLfsLocks":
            case "FilteredMyLfsLocks":
                RequireConfirmation(request);
                foreach (LfsFileLock row in rows.Cast<LfsFileLock>()) await model.UnlockLfsLockAsync(row, Bool(request, "force"));
                break;
            default: throw new ArgumentException("This list does not support editable selection.");
        }
        return new { message = model.StatusMessage };
    }

    private static async Task<object?> BranchesAsync(MainWindowViewModel model, JsonElement request)
    {
        GitBranch[] branches = ResolveKeys<GitBranch>(model.FilteredBranches, Strings(request, "keys"));
        if (branches.Length == 0 || branches.Any(branch => branch.IsCurrent || branch.IsRemote))
            throw new InvalidOperationException("Select non-current local branches only. Remote branches are never removed.");
        model.SetSelectedBranches(branches);
        Session session = For(model);
        session.Removals = await model.AnalyzeSelectedLocalBranchRemovalsAsync();
        if (Text(request, "operation") == "remove")
        {
            RequireConfirmation(request);
            if (session.Removals.Count != branches.Length) throw new InvalidOperationException("The branch safety analysis did not complete.");
            bool force = Bool(request, "force");
            if (session.Removals.Any(item => !item.CanForceRemove))
                throw new InvalidOperationException("A selected branch is current or checked out in a worktree. No branch was removed.");
            if (!force && session.Removals.Any(item => !item.CanRemoveSafely))
                throw new InvalidOperationException("Some selected branches have no verified retained copy. Review the safety report or explicitly choose forced local removal. No branch was removed.");
            await model.RemoveLocalBranchesAsync(session.Removals, force);
        }
        else if (Text(request, "operation") != "analyze") throw new ArgumentException("Unknown branch operation.");
        return new { message = model.StatusMessage };
    }

    private static async Task<object?> InspectAsync(MainWindowViewModel model, JsonElement request)
    {
        Session session = For(model);
        string operation = Text(request, "operation");
        if (operation == "cancel") { session.Branch?.CancelOperation(); return new { message = "Cancellation requested" }; }
        if (operation == "close") { await DisposeAsync(model); return new { message = "Branch inspection closed" }; }
        if (operation == "open")
        {
            await DisposeAsync(model);
            session.Branch = model.CreateBranchFileExplorer() ?? throw new InvalidOperationException("Select a branch first.");
            await session.Branch.LoadAsync();
            return new { message = session.Branch.Status };
        }
        BranchFileExplorerViewModel branch = session.Branch ?? throw new InvalidOperationException("Open a branch inspection first.");
        if (branch.IsOperationActive || branch.IsDiffLoading || operation == "preview" && branch.IsPreviewLoading)
            throw new InvalidOperationException("A branch operation is already running. Cancel it or wait for completion.");
        if (operation == "refresh") { await branch.RefreshRemoteAsync(); return new { message = branch.Status }; }
        if (operation == "search") { branch.Search = Text(request, "search"); return new { message = branch.Summary }; }
        if (operation == "expand")
        {
            BranchRow[] rows = ResolveKeys<BranchRow>(GetList(model, "NativeBranchFiles")!, Strings(request, "keys"));
            foreach (BranchRow row in rows.Where(row => row.IsDirectory))
                if (Bool(request, "expanded")) session.ExpandedBranchDirectories.Add(row.Path);
                else session.ExpandedBranchDirectories.Remove(row.Path);
            return new { message = "Folder visibility updated" };
        }
        string[] paths = Strings(request, "paths");
        if (paths.Length > 0)
        {
            var all = branch.FilteredFiles.ToDictionary(file => file.Path, StringComparer.Ordinal);
            var directories = new HashSet<string>(StringComparer.Ordinal);
            void CollectDirectories(GitRevisionTreeNode node)
            { if (node.IsDirectory) { directories.Add(node.Path); foreach (var child in node.Children) CollectDirectories(child); } }
            foreach (var root in branch.TreeRoots) CollectDirectories(root);
            if (paths.Any(path => !all.ContainsKey(path) && !directories.Contains(path)))
                throw new InvalidOperationException("The branch selection changed. Select the files again.");
            var files = paths.SelectMany(path => all.TryGetValue(path, out var file) ? [file] :
                all.Values.Where(candidate => candidate.Path.StartsWith(path + "/", StringComparison.Ordinal)));
            branch.SetSelectedFiles(files);
        }
        switch (operation)
        {
            case "select": break;
            case "preview": await branch.RetrieveSelectedForPreviewAsync(); break;
            case "diff": await branch.LoadDiffAsync(); break;
            case "retrieve": RequireConfirmation(request); await branch.RetrieveSelectedFilesAsync(RequiredDestination(request)); break;
            case "export": RequireConfirmation(request); await branch.ExportSelectedAsync(RequiredDestination(request)); break;
            case "restore": RequireConfirmation(request); await branch.RestoreSelectedToWorkingTreeAsync(); break;
            default: throw new ArgumentException("Unknown branch inspection operation.");
        }
        return new { message = branch.Status };
    }

    public static T[] ResolveKeys<T>(IList list, IReadOnlyList<string> keys)
    {
        if (keys.Count > 100_000) throw new ArgumentException("The selection is too large.");
        var requested = keys.ToHashSet(StringComparer.Ordinal);
        if (requested.Count != keys.Count) throw new ArgumentException("Duplicate selection keys.");
        Dictionary<string, object> rows = list.Cast<object>().ToDictionary(NativeTable.Key, StringComparer.Ordinal);
        if (requested.Any(key => !rows.TryGetValue(key, out object? row) || row is not T))
            throw new InvalidOperationException("The list changed. Select the items again.");
        return keys.Select(key => (T)rows[key]).ToArray();
    }

    private static string RequiredDestination(JsonElement request) => string.IsNullOrWhiteSpace(Text(request, "destination"))
        ? throw new ArgumentException("Choose a destination first.") : Path.GetFullPath(Text(request, "destination"));
    private static void RequireConfirmation(JsonElement request)
    { if (!Bool(request, "confirmed")) throw new InvalidOperationException("Confirm this operation first."); }
    private static bool TryValue(JsonElement request, string name, out JsonElement value) => request.TryGetProperty(name, out value) ||
        request.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out value);
    private static string Text(JsonElement request, string name, string fallback = "") => TryValue(request, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static bool Bool(JsonElement request, string name) => TryValue(request, name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string[] Strings(JsonElement request, string name) => request.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(item => item.GetString() ?? throw new ArgumentException("Invalid selection key.")).Take(100_001).ToArray() : [];
}
