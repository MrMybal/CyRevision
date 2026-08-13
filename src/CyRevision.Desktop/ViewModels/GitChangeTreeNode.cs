using System.Collections.ObjectModel;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitChangeTreeNode
{
    public GitChangeTreeNode(
        string name,
        string relativePath,
        bool isDirectory,
        GitChangeViewModel? change = null,
        IEnumerable<GitChangeTreeNode>? children = null,
        string? groupKind = null)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        Change = change;
        GroupKind = groupKind;
        Children = new ObservableCollection<GitChangeTreeNode>(children ?? []);
    }

    public string Name { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public GitChangeViewModel? Change { get; }

    public bool HasChange => Change is not null;

    public bool CanInclude => Change is { IsLocalOnly: false };

    public string? GroupKind { get; }

    public ObservableCollection<GitChangeTreeNode> Children { get; }

    public string Icon => IsDirectory ? "▸" : Change?.IsUntracked == true ? "+" : "•";

    public string Detail => IsDirectory
        ? $"{CountLeaves(this)} file(s)"
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

    private static int CountLeaves(GitChangeTreeNode node) =>
        node.Change is not null ? 1 : node.Children.Sum(CountLeaves);
}
