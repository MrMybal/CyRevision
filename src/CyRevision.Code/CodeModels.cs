using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CyRevision.Code;

public sealed class CodeTreeNode
{
    public CodeTreeNode(
        string name,
        string relativePath,
        string fullPath,
        bool isDirectory,
        IEnumerable<CodeTreeNode>? children = null,
        long size = 0,
        string language = "",
        bool hasUnloadedChildren = false,
        bool isPlaceholder = false)
    {
        Name = name;
        RelativePath = relativePath;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Size = size;
        Language = language;
        HasUnloadedChildren = hasUnloadedChildren;
        IsPlaceholder = isPlaceholder;
        Children = new CodeTreeNodeCollection(children ??
            (hasUnloadedChildren ? [CreatePlaceholder(fullPath)] : []));
    }

    public string Name { get; }
    public string RelativePath { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long Size { get; }
    public string Language { get; }
    public ObservableCollection<CodeTreeNode> Children { get; }
    public bool HasUnloadedChildren { get; private set; }
    public bool IsPlaceholder { get; }
    public string Icon => IsDirectory ? "▸" : LanguageIcon(Language);
    public string Detail => IsPlaceholder
        ? string.Empty
        : IsDirectory
            ? HasUnloadedChildren ? "Open to load" : $"{Children.Count} item(s)"
            : $"{FormatSize(Size)} · {Language}";
    public string AccentColor => IsDirectory ? "#D7BA7D" : LanguageColor(Language);

    public void ReplaceChildren(IEnumerable<CodeTreeNode> children)
    {
        if (Children is CodeTreeNodeCollection batch)
        {
            batch.ReplaceAll(children);
        }
        else
        {
            Children.Clear();
            foreach (CodeTreeNode child in children) Children.Add(child);
        }
        HasUnloadedChildren = false;
    }

    private static CodeTreeNode CreatePlaceholder(string parentPath) => new(
        "Loading…",
        string.Empty,
        parentPath,
        false,
        language: "Text",
        isPlaceholder: true);

    private static string LanguageIcon(string language) => language switch
    {
        "C#" => "C#",
        "C/C++" => "C+",
        "Python" => "Py",
        "JavaScript" or "TypeScript" => "JS",
        "JSON" => "{}",
        "Markdown" => "M↓",
        "Unreal" => "UE",
        "Image" => "IMG",
        "Unreal package" => "UE",
        _ => "·"
    };

    private static string LanguageColor(string language) => language switch
    {
        "C#" => "#B77FDB",
        "C/C++" => "#5DADE2",
        "Python" => "#E5C07B",
        "JavaScript" or "TypeScript" => "#E8D44D",
        "JSON" => "#9CDC8C",
        "Markdown" => "#61AFEF",
        "Unreal" => "#4EC9B0",
        "Image" => "#E06C75",
        "Unreal package" => "#C678DD",
        "XML" => "#CE9178",
        _ => "#A9B7C6"
    };

    private static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = size;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}

internal sealed class CodeTreeNodeCollection : ObservableCollection<CodeTreeNode>
{
    public CodeTreeNodeCollection(IEnumerable<CodeTreeNode> nodes)
    {
        foreach (CodeTreeNode node in nodes) Items.Add(node);
    }

    public void ReplaceAll(IEnumerable<CodeTreeNode> nodes)
    {
        CodeTreeNode[] snapshot = nodes as CodeTreeNode[] ?? nodes.ToArray();
        Items.Clear();
        foreach (CodeTreeNode node in snapshot) Items.Add(node);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed record CodeWorkspaceSnapshot(
    IReadOnlyList<CodeTreeNode> Roots,
    int DirectoryCount,
    int FileCount,
    long TotalBytes,
    bool WasTruncated,
    TimeSpan Elapsed);

public sealed record CodeFileEntry(
    string RelativePath,
    string FullPath,
    long Size,
    string Language)
{
    public string Name => Path.GetFileName(RelativePath);

    public string DirectoryPath => Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? "/";

    public string SizeText
    {
        get
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double value = Size;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.#} {units[unit]}";
        }
    }

    public CodeTreeNode ToTreeNode() => new(
        Name,
        RelativePath,
        FullPath,
        false,
        size: Size,
        language: Language);
}

public sealed record CodeFileIndex(
    IReadOnlyList<CodeFileEntry> Files,
    TimeSpan Elapsed);

public sealed record CodeSearchOptions(
    bool MatchCase = false,
    bool WholeWord = false,
    bool UseRegex = false,
    bool IncludeHidden = false,
    string FilePatterns = "",
    int MaximumResults = 2_000);

public sealed record CodeSearchResult(
    string RelativePath,
    string FullPath,
    int LineNumber,
    int ColumnNumber,
    string Preview,
    string MatchText)
{
    public string Location => $"{RelativePath}:{LineNumber}:{ColumnNumber}";
    public string FileName => Path.GetFileName(RelativePath);
    public string DirectoryPath => Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? "/";
    public string Position => $"{LineNumber}:{ColumnNumber}";

    private int MatchIndex => string.IsNullOrEmpty(MatchText)
        ? -1
        : Preview.IndexOf(MatchText, StringComparison.OrdinalIgnoreCase);

    public string PreviewBeforeMatch => MatchIndex < 0 ? Preview : Preview[..MatchIndex];
    public string PreviewMatch => MatchIndex < 0 ? string.Empty : Preview.Substring(MatchIndex, MatchText.Length);
    public string PreviewAfterMatch => MatchIndex < 0 ? string.Empty : Preview[(MatchIndex + MatchText.Length)..];
}

public sealed record CodeSearchReport(
    IReadOnlyList<CodeSearchResult> Results,
    int FilesScanned,
    bool UsedRipgrep,
    bool WasTruncated,
    TimeSpan Elapsed);

public sealed record CodeSymbol(string Kind, string Name, int LineNumber)
{
    public string Display => $"{Kind}  {Name}";
}

public sealed record CodeFilePreview(
    string RelativePath,
    string Language,
    string Text,
    int LineCount,
    long Size,
    bool IsBinary,
    bool WasTruncated,
    IReadOnlyList<CodeSymbol> Symbols)
{
    public string Summary => IsBinary
        ? $"Binary file · {Size:N0} bytes"
        : $"{Language} · {LineCount:N0} lines · {Size:N0} bytes" + (WasTruncated ? " · preview truncated" : "");
}

public sealed record CodeHistoryEntry(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset Date,
    string Subject)
{
    public string DateText => Date.ToLocalTime().ToString("g");
}

public sealed record CodeSelection(int StartLine, int EndLine)
{
    public static CodeSelection Normalize(int first, int second) =>
        new(Math.Max(1, Math.Min(first, second)), Math.Max(1, Math.Max(first, second)));
}
