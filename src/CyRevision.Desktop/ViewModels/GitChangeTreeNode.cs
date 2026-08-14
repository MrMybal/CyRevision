using System.Collections.ObjectModel;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitChangeTreeNode
{
    private const int FilePageSize = 500;
    private IReadOnlyList<GitChangeViewModel>? _lazyChanges;
    private readonly bool _isFilePage;

    public GitChangeTreeNode(
        string name,
        string relativePath,
        bool isDirectory,
        GitChangeViewModel? change = null,
        IEnumerable<GitChangeTreeNode>? children = null,
        string? groupKind = null,
        IReadOnlyList<GitChangeViewModel>? lazyChanges = null,
        bool isFilePage = false,
        bool isPlaceholder = false)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        Change = change;
        GroupKind = groupKind;
        _lazyChanges = lazyChanges;
        _isFilePage = isFilePage;
        IsPlaceholder = isPlaceholder;
        GitChangeTreeNode[] initialChildren = children?.ToArray() ?? [];
        if (initialChildren.Length == 0 && lazyChanges is { Count: > 0 })
        {
            initialChildren = [CreatePlaceholder()];
        }
        Children = new ObservableCollection<GitChangeTreeNode>(initialChildren);
        LeafCount = lazyChanges?.Count ?? (change is null ? initialChildren.Sum(child => child.LeafCount) : 1);
    }

    public string Name { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public GitChangeViewModel? Change { get; }

    public bool HasChange => Change is not null;

    public bool IsPlaceholder { get; }

    public bool HasUnloadedChildren => _lazyChanges is { Count: > 0 };

    public bool CanInclude => Change is { IsLocalOnly: false };

    public string? GroupKind { get; }

    public ObservableCollection<GitChangeTreeNode> Children { get; }

    public int LeafCount { get; private set; }

    public string Icon => IsPlaceholder ? "…" : IsDirectory ? "▸" : Change?.IsUntracked == true ? "+" : "•";

    public string Detail => IsDirectory
        ? $"{LeafCount} file(s)"
        : Change is null
            ? string.Empty
            : $"{Change.State} · {Change.PreparationState}" +
              (Change.HasLock ? $" · {Change.LockOwner}" : string.Empty);

    public string AccentColor => Change?.StatusColor ?? GroupKind switch
    {
        "tracked" => "#61AFEF",
        "untracked" => "#78D7B7",
        "local" => "#7E8799",
        _ => "#D7BA7D"
    };

    internal void IncrementLeafCount() => LeafCount++;

    public void EnsureChildrenLoaded()
    {
        if (_lazyChanges is not { Count: > 0 } changes) return;
        _lazyChanges = null;
        Children.Clear();
        if (_isFilePage)
        {
            foreach (GitChangeViewModel change in changes)
            {
                Children.Add(CreateFile(change));
            }
            return;
        }

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
        IReadOnlyList<GitChangeViewModel> changes) => new(
        name,
        string.Empty,
        true,
        groupKind: groupKind,
        lazyChanges: changes);

    private GitChangeTreeNode CreateFile(GitChangeViewModel change) => new(
        change.FileName,
        change.Path,
        false,
        change,
        groupKind: GroupKind);

    private static GitChangeTreeNode CreatePlaceholder() => new(
        "Open to load…",
        string.Empty,
        false,
        isPlaceholder: true);
}
