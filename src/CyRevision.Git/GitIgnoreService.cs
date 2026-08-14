using System.Text;

namespace CyRevision.Git;

public enum GitIgnoreSource
{
    Repository,
    LocalExclude
}

public sealed record GitIgnoreRule(
    int LineNumber,
    string Pattern,
    string Kind,
    string Warning);

public sealed record GitIgnoreDocument(
    GitIgnoreSource Source,
    string FilePath,
    string Content,
    bool Exists,
    IReadOnlyList<GitIgnoreRule> Rules);

public sealed record GitIgnoreMatch(
    bool IsIgnored,
    string Path,
    string Source,
    int? LineNumber,
    string Pattern,
    string Summary);

public sealed class GitIgnoreService
{
    private readonly string _gitExecutable;
    private readonly ProcessRunner _runner = new();

    public GitIgnoreService(string gitExecutable = "git")
    {
        _gitExecutable = gitExecutable;
    }

    public async Task<GitIgnoreDocument> LoadAsync(
        string repositoryPath,
        GitIgnoreSource source,
        CancellationToken cancellationToken = default)
    {
        string root = await ResolveRepositoryRootAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        string path = ResolvePath(root, source);
        bool exists = File.Exists(path);
        string content = exists
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        return new GitIgnoreDocument(source, path, content, exists, ParseRules(content));
    }

    public async Task SaveAsync(
        string repositoryPath,
        GitIgnoreSource source,
        string content,
        CancellationToken cancellationToken = default)
    {
        string root = await ResolveRepositoryRootAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        string path = ResolvePath(root, source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string normalized = NormalizeLineEndings(content);
        string temporaryPath = path + ".cyrevision.tmp";
        await File.WriteAllTextAsync(temporaryPath, normalized, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<GitIgnoreMatch> TestPathAsync(
        string repositoryPath,
        string path,
        CancellationToken cancellationToken = default)
    {
        string root = await ResolveRepositoryRootAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        string relativePath = NormalizeRelativePath(root, path);
        ProcessResult result = await _runner.RunAsync(
                _gitExecutable,
                ["check-ignore", "-z", "-v", "--no-index", "--stdin"],
                root,
                relativePath + "\0",
                cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode == 1)
        {
            return new GitIgnoreMatch(false, relativePath, string.Empty, null, string.Empty,
                "Not ignored by the active Git rules.");
        }

        if (!result.Succeeded)
        {
            throw new GitOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "Unable to test the ignore rule."
                : result.StandardError.Trim());
        }

        string[] fields = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        string source = fields.ElementAtOrDefault(0) ?? string.Empty;
        int? line = int.TryParse(fields.ElementAtOrDefault(1), out int parsedLine) ? parsedLine : null;
        string pattern = fields.ElementAtOrDefault(2) ?? string.Empty;
        string matchedPath = fields.ElementAtOrDefault(3) ?? relativePath;
        bool ignored = !pattern.StartsWith('!');
        return new GitIgnoreMatch(
            ignored,
            matchedPath,
            source,
            line,
            pattern,
            ignored
                ? $"Ignored by {source}:{line?.ToString() ?? "?"} ({pattern})"
                : $"Included again by {source}:{line?.ToString() ?? "?"} ({pattern})");
    }

    public async Task<IReadOnlyList<string>> ListIgnoredFilesAsync(
        string repositoryPath,
        int maximumCount = 2500,
        CancellationToken cancellationToken = default)
    {
        string root = await ResolveRepositoryRootAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        ProcessResult result = await _runner.RunAsync(
                _gitExecutable,
                ["--no-optional-locks", "status", "--ignored", "--porcelain=v1", "-z", "--untracked-files=all"],
                root,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new GitOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "Unable to list ignored files."
                : result.StandardError.Trim());
        }

        List<string> ignored = new(Math.Min(maximumCount, 256));
        foreach (string entry in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!entry.StartsWith("!! ", StringComparison.Ordinal)) continue;
            ignored.Add(entry[3..].Replace('\\', '/'));
            if (ignored.Count >= maximumCount) break;
        }

        return ignored;
    }

    public static IReadOnlyList<GitIgnoreRule> ParseRules(string content)
    {
        string[] lines = NormalizeLineEndings(content).Split('\n');
        List<GitIgnoreRule> rules = new(lines.Length);
        for (int index = 0; index < lines.Length; index++)
        {
            string raw = lines[index];
            string trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                rules.Add(new GitIgnoreRule(index + 1, string.Empty, "Blank", string.Empty));
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                rules.Add(new GitIgnoreRule(index + 1, trimmed, "Comment", string.Empty));
                continue;
            }

            bool negated = trimmed.StartsWith('!') && !trimmed.StartsWith("\\!", StringComparison.Ordinal);
            string effective = negated ? trimmed[1..] : trimmed;
            string kind = negated ? "Include" : effective.EndsWith('/') ? "Directory" : "Ignore";
            string warning = ValidatePattern(effective, negated);
            rules.Add(new GitIgnoreRule(index + 1, trimmed, kind, warning));
        }

        return rules;
    }

    private async Task<string> ResolveRepositoryRootAsync(string path, CancellationToken cancellationToken)
    {
        ProcessResult result = await _runner.RunAsync(
                _gitExecutable,
                ["rev-parse", "--show-toplevel"],
                path,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new GitOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "The selected folder is not a Git repository."
                : result.StandardError.Trim());
        }

        return Path.GetFullPath(result.StandardOutput.Trim());
    }

    private static string ResolvePath(string root, GitIgnoreSource source) => source switch
    {
        GitIgnoreSource.Repository => Path.Combine(root, ".gitignore"),
        GitIgnoreSource.LocalExclude => Path.Combine(root, ".git", "info", "exclude"),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    private static string NormalizeRelativePath(string root, string path)
    {
        string trimmed = path.Trim().Trim('"');
        string relative = Path.IsPathRooted(trimmed) ? Path.GetRelativePath(root, trimmed) : trimmed;
        return relative.Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeLineEndings(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string ValidatePattern(string pattern, bool negated)
    {
        if (pattern.Length == 0) return negated ? "A negation requires a pattern." : "Empty pattern.";
        if (pattern.EndsWith('\\') && !pattern.EndsWith("\\\\", StringComparison.Ordinal))
            return "Trailing escape character.";
        int open = pattern.Count(character => character == '[');
        int close = pattern.Count(character => character == ']');
        if (open != close) return "Unbalanced character class brackets.";
        return string.Empty;
    }
}
