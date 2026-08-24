using System.Text;

namespace CyRevision.Git;

public sealed record GitAttributesMergeResult(
    string FilePath,
    IReadOnlyList<string> AddedPatterns,
    IReadOnlyList<string> ExistingPatterns)
{
    public bool Changed => AddedPatterns.Count > 0;
}

public sealed class GitAttributesService
{
    public async Task<IReadOnlyList<string>> GetLfsPatternsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(repositoryRoot);
        if (!File.Exists(path)) return [];
        string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return ParseLfsPatterns(content);
    }

    public async Task<GitAttributesMergeResult> MergeLfsPatternsAsync(
        string repositoryRoot,
        IEnumerable<string> patterns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        string root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        string path = ResolvePath(root);
        string existingContent = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        IReadOnlyList<string> existingPatterns = ParseLfsPatterns(existingContent);
        HashSet<string> known = new(existingPatterns, StringComparer.OrdinalIgnoreCase);
        string[] additions = patterns
            .Select(NormalizePattern)
            .Where(known.Add)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (additions.Length == 0)
            return new GitAttributesMergeResult(path, [], existingPatterns);

        StringBuilder merged = new(NormalizeLineEndings(existingContent).TrimEnd('\n'));
        if (merged.Length > 0) merged.AppendLine().AppendLine();
        merged.AppendLine("# Git LFS rules managed by CyRevision");
        foreach (string pattern in additions)
            merged.Append(pattern).AppendLine(" filter=lfs diff=lfs merge=lfs -text");

        await WriteAtomicallyAsync(path, merged.ToString(), cancellationToken).ConfigureAwait(false);
        return new GitAttributesMergeResult(path, additions, existingPatterns);
    }

    public static IReadOnlyList<string> ParseLfsPatterns(string content)
    {
        if (string.IsNullOrEmpty(content)) return [];
        List<string> patterns = [];
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in NormalizeLineEndings(content).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                !line.Contains("filter=lfs", StringComparison.OrdinalIgnoreCase))
                continue;
            string pattern = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (pattern.Length > 0 && emitted.Add(pattern)) patterns.Add(pattern);
        }
        return patterns;
    }

    public static string NormalizePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        string normalized = pattern.Trim().Replace('\\', '/');
        if (normalized.Contains('\n') || normalized.Contains('\r') || normalized.StartsWith('#'))
            throw new ArgumentException("The Git LFS pattern is invalid.", nameof(pattern));
        if (normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("Whitespace in Git LFS patterns is not supported by this assistant.", nameof(pattern));
        return normalized;
    }

    private static string ResolvePath(string repositoryRoot) =>
        Path.Combine(Path.GetFullPath(repositoryRoot), ".gitattributes");

    private static string NormalizeLineEndings(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = $"{path}.cyrevision.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    NormalizeLineEndings(content),
                    new UTF8Encoding(false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
