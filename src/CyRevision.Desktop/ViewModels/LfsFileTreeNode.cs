using System.Collections.ObjectModel;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class LfsFileTreeNode
{
    public LfsFileTreeNode(string name, string relativePath, bool isFolder, LfsTrackedFile? file = null)
    {
        Name = name;
        RelativePath = relativePath;
        IsFolder = isFolder;
        File = file;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public bool IsFolder { get; }
    public LfsTrackedFile? File { get; }
    public ObservableCollection<LfsFileTreeNode> Children { get; } = [];
    public string Icon => IsFolder ? "▸" : "•";
    public string Detail => IsFolder ? $"{CountFiles():N0} file(s)" : $"{File!.Kind} · {File.SizeText} · {File.Availability}";

    private int CountFiles() => IsFolder
        ? Children.Sum(child => child.IsFolder ? child.CountFiles() : 1)
        : 1;
}
