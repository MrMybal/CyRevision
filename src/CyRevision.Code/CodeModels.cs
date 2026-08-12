using System.Collections.ObjectModel;

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
        string language = "")
    {
        Name = name;
        RelativePath = relativePath;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Size = size;
        Language = language;
        Children = new ObservableCollection<CodeTreeNode>(children ?? []);
    }

    public string Name { get; }
    public string RelativePath { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long Size { get; }
    public string Language { get; }
    public ObservableCollection<CodeTreeNode> Children { get; }
    public string Icon => IsDirectory ? "▸" : LanguageIcon(Language);
    public string Detail => IsDirectory ? $"{Children.Count} item(s)" : $"{FormatSize(Size)} · {Language}";

    private static string LanguageIcon(string language) => language switch
    {
        "C#" => "C#",
        "C/C++" => "C+",
        "Python" => "Py",
        "JavaScript" or "TypeScript" => "JS",
        "JSON" => "{}",
        "Markdown" => "M↓",
        "Unreal" => "UE",
        _ => "·"
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

public sealed record CodeWorkspaceSnapshot(
    IReadOnlyList<CodeTreeNode> Roots,
    int DirectoryCount,
    int FileCount,
    long TotalBytes,
    bool WasTruncated,
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
