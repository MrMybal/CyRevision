using System.Collections.ObjectModel;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitChangeTreeNode : ObservableObject
{
    private const int FilePageSize = 500;
    private IReadOnlyList<GitChangeViewModel>? _lazyChanges;
    private readonly IReadOnlyList<GitChangeViewModel> _containedChanges;
    private readonly bool _isFilePage;
    private int _loadedFileCount;
    private bool _isExpanded;

    public GitChangeTreeNode(
        string name,
        string relativePath,
        bool isDirectory,
        GitChangeViewModel? change = null,
        IEnumerable<GitChangeTreeNode>? children = null,
        string? groupKind = null,
        IReadOnlyList<GitChangeViewModel>? lazyChanges = null,
        bool isFilePage = false,
        bool isPlaceholder = false,
        bool isExpanded = false)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        Change = change;
        GroupKind = groupKind;
        _lazyChanges = lazyChanges;
        _isFilePage = isFilePage;
        _isExpanded = isExpanded;
        IsPlaceholder = isPlaceholder;
        GitChangeTreeNode[] initialChildren = children?.ToArray() ?? [];
        if (initialChildren.Length == 0 && lazyChanges is { Count: > 0 })
        {
            initialChildren = [CreatePlaceholder()];
        }
        Children = new ObservableCollection<GitChangeTreeNode>(initialChildren);
        _containedChanges = lazyChanges
                            ?? (change is not null
                                ? [change]
                                : initialChildren.SelectMany(child => child.ContainedChanges).ToArray());
        LeafCount = _containedChanges.Count;
    }

    public string Name { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public GitChangeViewModel? Change { get; }

    public bool HasChange => Change is not null;

    public bool IsPlaceholder { get; }

    public bool HasUnloadedChildren => _lazyChanges is { Count: > 0 };

    public bool CanInclude => Change is { IsLocalOnly: false };

    public bool CanIncludeRecursively => _containedChanges.Any(change => !change.IsLocalOnly);

    public bool? IsIncluded
    {
        get
        {
            bool anyIncluded = false;
            bool anyKept = false;
            foreach (GitChangeViewModel change in _containedChanges)
            {
                if (change.IsLocalOnly) continue;
                if (change.IsIncluded) anyIncluded = true;
                else anyKept = true;
                if (anyIncluded && anyKept) return null;
            }

            return anyIncluded;
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string? GroupKind { get; }

    public ObservableCollection<GitChangeTreeNode> Children { get; }

    public int LeafCount { get; private set; }

    public IReadOnlyList<GitChangeViewModel> ContainedChanges => _containedChanges;

    public string Icon => IsPlaceholder ? "…" : IsDirectory ? "▸" : Change?.IsUntracked == true ? "+" : "•";

    public string Detail => IsDirectory
        ? $"{LeafCount} file(s)"
        : Change is null
            ? string.Empty
            : $"{Change.State} · {Change.Area}";

    public string AccentColor => Change?.StatusColor ?? GroupKind switch
    {
        "tracked" => "#61AFEF",
        "untracked" => "#78D7B7",
        "local" => "#7E8799",
        _ => "#D7BA7D"
    };

    internal void IncrementLeafCount() => LeafCount++;

    public void RefreshIncludedState()
    {
        OnPropertyChanged(nameof(IsIncluded));
        foreach (GitChangeTreeNode child in Children)
        {
            if (!child.IsPlaceholder) child.RefreshIncludedState();
        }
    }

    public void EnsureChildrenLoaded()
    {
        if (_lazyChanges is not { Count: > 0 } changes) return;
        if (_isFilePage)
        {
            if (_loadedFileCount == 0) Children.Clear();
            else if (Children.LastOrDefault()?.IsPlaceholder == true) Children.RemoveAt(Children.Count - 1);
            int end = Math.Min(_loadedFileCount + FilePageSize, changes.Count);
            for (int index = _loadedFileCount; index < end; index++)
            {
                Children.Add(CreateFile(changes[index]));
            }
            _loadedFileCount = end;
            if (_loadedFileCount < changes.Count) Children.Add(CreatePlaceholder("Load more files…"));
            else _lazyChanges = null;
            return;
        }

        _lazyChanges = null;
        Children.Clear();
        string prefix = string.IsNullOrWhiteSpace(RelativePath)
            ? string.Empty
            : RelativePath.Trim('/');
        Dictionary<string, List<GitChangeViewModel>> directories = new(StringComparer.OrdinalIgnoreCase);
        List<GitChangeViewModel> directFiles = [];
        foreach (GitChangeViewModel change in changes)
        {
            string path = change.Path.Replace('\\', '/').TrimStart('/');
            string remainder = prefix.Length > 0 && path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                ? path[(prefix.Length + 1)..]
                : path;
            int separator = remainder.IndexOf('/');
            if (separator < 0)
            {
                directFiles.Add(change);
                continue;
            }

            string directoryName = remainder[..separator];
            if (!directories.TryGetValue(directoryName, out List<GitChangeViewModel>? directoryChanges))
            {
                directoryChanges = [];
                directories[directoryName] = directoryChanges;
            }
            directoryChanges.Add(change);
        }

        foreach ((string directoryName, List<GitChangeViewModel> directoryChanges) in directories
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string directoryPath = prefix.Length == 0 ? directoryName : prefix + "/" + directoryName;
            Children.Add(new GitChangeTreeNode(
                directoryName,
                directoryPath,
                true,
                groupKind: GroupKind,
                lazyChanges: directoryChanges));
        }

        directFiles.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.FileName, right.FileName));
        if (directFiles.Count <= FilePageSize)
        {
            foreach (GitChangeViewModel change in directFiles) Children.Add(CreateFile(change));
            return;
        }

        for (int start = 0; start < directFiles.Count; start += FilePageSize)
        {
            int count = Math.Min(FilePageSize, directFiles.Count - start);
            int end = start + count;
            Children.Add(new GitChangeTreeNode(
                $"Files {start + 1:N0}–{end:N0}",
                prefix,
                true,
                groupKind: GroupKind,
                lazyChanges: directFiles.GetRange(start, count),
                isFilePage: true));
        }
    }

    public static GitChangeTreeNode CreateLazyGroup(
        string name,
        string groupKind,
        IReadOnlyList<GitChangeViewModel> changes,
        bool isExpanded = true) => new(
        name,
        string.Empty,
        true,
        groupKind: groupKind,
        lazyChanges: changes,
        isExpanded: isExpanded);

    public static GitChangeTreeNode CreateFlatGroup(
        string name,
        string groupKind,
        IReadOnlyList<GitChangeViewModel> changes,
        bool isExpanded = true) => new(
        name,
        string.Empty,
        true,
        groupKind: groupKind,
        lazyChanges: changes,
        isFilePage: true,
        isExpanded: isExpanded);

    private GitChangeTreeNode CreateFile(GitChangeViewModel change) => new(
        change.FileName,
        change.Path,
        false,
        change,
        groupKind: GroupKind);

    private static GitChangeTreeNode CreatePlaceholder(string name = "Open to load…") => new(
        name,
        string.Empty,
        false,
        isPlaceholder: true);
}
