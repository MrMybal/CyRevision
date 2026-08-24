using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitRevisionTreeNode
{
    private readonly List<GitRevisionTreeNode> _mutableChildren = [];
    private int _fileCount;

    private GitRevisionTreeNode(string name, string path, bool isDirectory, GitRevisionFile? file)
    {
        Name = name;
        Path = path;
        IsDirectory = isDirectory;
        File = file;
        _fileCount = isDirectory ? 0 : 1;
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public GitRevisionFile? File { get; }
    public IReadOnlyList<GitRevisionTreeNode> Children => _mutableChildren;
    public string Icon => IsDirectory ? "▸" : File?.IsSubmodule == true ? "SUB" : FileIcon(Name);
    public string AccentColor => IsDirectory ? "#D7BA7D" : FileColor(Name);
    public string Detail => IsDirectory ? $"{_fileCount:N0} file(s)" : File?.SizeText ?? string.Empty;

    public static IReadOnlyList<GitRevisionTreeNode> Build(IReadOnlyList<GitRevisionFile> files)
    {
        GitRevisionTreeNode root = new(string.Empty, string.Empty, true, null);
        Dictionary<string, GitRevisionTreeNode> directories = new(StringComparer.Ordinal)
        {
            [string.Empty] = root
        };

        foreach (GitRevisionFile file in files)
        {
            string[] segments = file.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            string parentPath = string.Empty;
            GitRevisionTreeNode parent = root;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                string path = parentPath.Length == 0 ? segments[index] : parentPath + "/" + segments[index];
                if (!directories.TryGetValue(path, out GitRevisionTreeNode? directory))
                {
                    directory = new GitRevisionTreeNode(segments[index], path, true, null);
                    directories[path] = directory;
                    parent._mutableChildren.Add(directory);
                }

                parent = directory;
                parentPath = path;
            }

            parent._mutableChildren.Add(new GitRevisionTreeNode(segments[^1], file.Path, false, file));
        }

        SortRecursively(root);
        ComputeFileCounts(root);
        return root._mutableChildren;
    }

    private static int ComputeFileCounts(GitRevisionTreeNode node)
    {
        if (!node.IsDirectory) return node._fileCount;
        node._fileCount = node._mutableChildren.Sum(ComputeFileCounts);
        return node._fileCount;
    }

    private static void SortRecursively(GitRevisionTreeNode node)
    {
        node._mutableChildren.Sort((left, right) =>
        {
            int directoriesFirst = right.IsDirectory.CompareTo(left.IsDirectory);
            return directoriesFirst != 0
                ? directoriesFirst
                : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });
        foreach (GitRevisionTreeNode child in node._mutableChildren.Where(child => child.IsDirectory))
            SortRecursively(child);
    }

    private static string FileIcon(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "C#",
        ".cpp" or ".h" or ".hpp" => "C+",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => "IMG",
        ".uasset" or ".umap" => "UE",
        ".json" => "{}",
        ".md" => "M↓",
        _ => "·"
    };

    private static string FileColor(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "#B77FDB",
        ".cpp" or ".h" or ".hpp" => "#5DADE2",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => "#E06C75",
        ".uasset" or ".umap" => "#C678DD",
        ".json" => "#9CDC8C",
        ".md" => "#61AFEF",
        _ => "#A9B7C6"
    };
}

public sealed record BranchFileOperationHistoryItem(
    DateTimeOffset Timestamp,
    string Operation,
    string Branch,
    IReadOnlyList<string> Paths,
    string Result,
    bool Succeeded)
{
    public string TimestampText => Timestamp.ToLocalTime().ToString("g");
    public string PathSummary => Paths.Count switch
    {
        0 => "—",
        1 => Paths[0],
        _ => $"{Paths[0]} + {Paths.Count - 1:N0} more"
    };
    public string State => Succeeded ? "Completed" : "Failed";
}

public sealed record BranchFileOperationProgress(
    string Stage,
    string Detail,
    int Completed,
    int Total)
{
    public bool IsIndeterminate => Total <= 0;
    public double Percent => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total * 100, 0, 100);
    public string Summary => Total <= 0 ? Stage : $"{Stage} · {Completed:N0}/{Total:N0}";
}
