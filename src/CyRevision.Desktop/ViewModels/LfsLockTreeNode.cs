using System.Collections.ObjectModel;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class LfsLockTreeNode
{
    public LfsLockTreeNode(
        string name,
        string relativePath,
        bool isDirectory,
        LfsFileLock? fileLock = null)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        FileLock = fileLock;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public bool IsDirectory { get; }
    public LfsFileLock? FileLock { get; }
    public ObservableCollection<LfsLockTreeNode> Children { get; } = [];
    public string Icon => IsDirectory ? "▸" : "◆";
    public string AccentColor => FileLock?.FileColor ?? "#D7BA7D";
    public string Detail => IsDirectory
        ? $"{CountLeaves(this):N0} file(s)"
        : $"{FileLock?.OwnerName} · {FileLock?.LockedAtText} · {FileLock?.Source}";

    private static int CountLeaves(LfsLockTreeNode node) =>
        node.FileLock is not null ? 1 : node.Children.Sum(CountLeaves);
}
