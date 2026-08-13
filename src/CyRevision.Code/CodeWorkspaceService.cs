using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyRevision.Code;

public sealed class CodeWorkspaceService
{
    private const int MaximumIndexedFiles = 150_000;
    private const int MaximumPreviewBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".idea", ".vs", ".cache", "bin", "obj", "Binaries",
        "Intermediate", "Saved", "DerivedDataCache", "node_modules", "packages", "Library", "Temp",
        "BuildToolsOutput"
    };

    public Task<CodeWorkspaceSnapshot> BuildTreeAsync(
        string rootPath,
        string filter = "",
        bool includeHidden = false,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildTree(rootPath, filter, includeHidden, cancellationToken), cancellationToken);

    public async Task<CodeFilePreview> ReadPreviewAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath = ResolveInsideRoot(rootPath, relativePath);
        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The selected file no longer exists.", fullPath);
        }

        await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int length = (int)Math.Min(stream.Length, MaximumPreviewBytes);
        byte[] bytes = new byte[length];
        int read = await stream.ReadAsync(bytes, cancellationToken);
        bool binary = IsBinary(bytes.AsSpan(0, Math.Min(read, 8_192)));
        string language = DetectLanguage(fullPath);
        if (binary)
        {
            return new CodeFilePreview(relativePath, language, string.Empty, 0, info.Length, true, false, []);
        }

        string text = Encoding.UTF8.GetString(bytes, 0, read);
        int lineCount = text.Length == 0 ? 0 : text.Count(character => character == '\n') + 1;
        return new CodeFilePreview(
            relativePath,
            language,
            text,
            lineCount,
            info.Length,
            false,
            stream.Length > MaximumPreviewBytes,
            ExtractSymbols(text, language));
    }

    public async Task<CodeSearchReport> SearchAsync(
        string rootPath,
        string query,
        CodeSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string root = NormalizeRoot(rootPath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            IReadOnlyList<CodeSearchResult> fastResults = await SearchWithRipgrepAsync(
                root, query, options, cancellationToken);
            stopwatch.Stop();
            return new CodeSearchReport(
                fastResults,
                fastResults.Select(result => result.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                true,
                fastResults.Count >= options.MaximumResults,
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IReadOnlyList<CodeSearchResult> fallback = await Task.Run(
                () => SearchManaged(root, query, options, cancellationToken), cancellationToken);
            stopwatch.Stop();
            return new CodeSearchReport(
                fallback,
                fallback.Select(result => result.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                false,
                fallback.Count >= options.MaximumResults,
                stopwatch.Elapsed);
        }
    }

    public async Task<IReadOnlyList<CodeHistoryEntry>> GetHistoryAsync(
        string rootPath,
        string relativePath,
        CodeSelection? selection = null,
        int maximumCount = 200,
        CancellationToken cancellationToken = default)
    {
        string root = NormalizeRoot(rootPath);
        string normalizedRelative = Path.GetRelativePath(root, ResolveInsideRoot(root, relativePath)).Replace('\\', '/');
        List<string> arguments = ["-c", $"safe.directory={root.Replace('\\', '/')}", "log"];
        if (selection is null)
        {
            arguments.Add($"--max-count={Math.Clamp(maximumCount, 1, 2_000)}");
            arguments.Add("--format=CYREV%x1f%H%x1f%h%x1f%an%x1f%aI%x1f%s");
            arguments.Add("--");
            arguments.Add(normalizedRelative);
        }
        else
        {
            arguments.Add("--format=CYREV%x1f%H%x1f%h%x1f%an%x1f%aI%x1f%s");
            arguments.Add($"-L{selection.StartLine},{selection.EndLine}:{normalizedRelative}");
        }

        ProcessOutput output = await RunProcessAsync("git", arguments, root, cancellationToken);
        if (output.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(output.Error)
                ? "Git could not load the selected history."
                : output.Error.Trim());
        }

        return output.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("CYREV\u001f", StringComparison.Ordinal))
            .Select(ParseHistoryLine)
            .Where(entry => entry is not null)
            .Cast<CodeHistoryEntry>()
            .DistinctBy(entry => entry.Hash)
            .Take(maximumCount)
            .ToArray();
    }

    public static CodeSelection SelectionFromOffsets(string text, int selectionStart, int selectionEnd)
    {
        int safeStart = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        int safeEnd = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        int startLine = 1 + CountCharacter(text.AsSpan(0, safeStart), '\n');
        int endLine = 1 + CountCharacter(text.AsSpan(0, safeEnd), '\n');
        return CodeSelection.Normalize(startLine, endLine);
    }

    private static CodeWorkspaceSnapshot BuildTree(
        string rootPath,
        string filter,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        string root = NormalizeRoot(rootPath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        BuildCounters counters = new();
        IReadOnlyList<CodeTreeNode> nodes = BuildDirectoryChildren(
            root, root, filter.Trim(), includeHidden, counters, cancellationToken);
        stopwatch.Stop();
        return new CodeWorkspaceSnapshot(
            nodes,
            counters.Directories,
            counters.Files,
            counters.Bytes,
            counters.Truncated,
            stopwatch.Elapsed);
    }

    private static IReadOnlyList<CodeTreeNode> BuildDirectoryChildren(
        string root,
        string directory,
        string filter,
        bool includeHidden,
        BuildCounters counters,
        CancellationToken cancellationToken)
    {
        List<CodeTreeNode> nodes = [];
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory)
                .OrderBy(path => !Directory.Exists(path))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return nodes;
        }
        catch (IOException)
        {
            return nodes;
        }

        foreach (string path in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            string name = Path.GetFileName(path);
            bool directoryEntry = attributes.HasFlag(FileAttributes.Directory);
            if ((!includeHidden && attributes.HasFlag(FileAttributes.Hidden)) ||
                (directoryEntry && (ExcludedDirectories.Contains(name) || attributes.HasFlag(FileAttributes.ReparsePoint))))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (directoryEntry)
            {
                IReadOnlyList<CodeTreeNode> children = BuildDirectoryChildren(
                    root, path, filter, includeHidden, counters, cancellationToken);
                if (filter.Length > 0 && children.Count == 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                counters.Directories++;
                nodes.Add(new CodeTreeNode(name, relative, path, true, children));
                continue;
            }

            if (counters.Files >= MaximumIndexedFiles)
            {
                counters.Truncated = true;
                break;
            }
            if (filter.Length > 0 && !relative.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileInfo info = new(path);
            counters.Files++;
            counters.Bytes += info.Length;
            nodes.Add(new CodeTreeNode(name, relative, path, false, size: info.Length, language: DetectLanguage(path)));
        }
        return nodes;
    }

    private static async Task<IReadOnlyList<CodeSearchResult>> SearchWithRipgrepAsync(
        string root,
        string query,
        CodeSearchOptions options,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "rg",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in BuildRipgrepArguments(query, options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("ripgrep could not start.");
        }

        List<CodeSearchResult> results = [];
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (TryParseRipgrepMatch(root, line, out CodeSearchResult? result))
            {
                results.Add(result!);
                if (results.Count >= options.MaximumResults)
                {
                    if (!process.HasExited) process.Kill(true);
                    break;
                }
            }
        }
        if (!process.HasExited)
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        if (process.ExitCode > 1)
        {
            throw new InvalidOperationException((await process.StandardError.ReadToEndAsync(cancellationToken)).Trim());
        }
        return results;
    }

    private static IEnumerable<string> BuildRipgrepArguments(string query, CodeSearchOptions options)
    {
        yield return "--json";
        yield return "--line-number";
        yield return "--column";
        yield return "--color=never";
        if (!options.UseRegex) yield return "--fixed-strings";
        if (!options.MatchCase) yield return "--ignore-case";
        if (options.WholeWord) yield return "--word-regexp";
        if (options.IncludeHidden) yield return "--hidden";
        foreach (string excluded in ExcludedDirectories)
        {
            yield return "-g";
            yield return $"!**/{excluded}/**";
        }
        foreach (string pattern in ParsePatterns(options.FilePatterns))
        {
            yield return "-g";
            yield return pattern;
        }
        yield return "--";
        yield return query;
        yield return ".";
    }

    private static bool TryParseRipgrepMatch(string root, string json, out CodeSearchResult? result)
    {
        result = null;
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("type").GetString() != "match") return false;
        JsonElement data = document.RootElement.GetProperty("data");
        string relative = data.GetProperty("path").GetProperty("text").GetString()!.Replace('\\', '/');
        if (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
        int line = data.GetProperty("line_number").GetInt32();
        string preview = data.GetProperty("lines").GetProperty("text").GetString()!.TrimEnd('\r', '\n', ' ');
        JsonElement submatch = data.GetProperty("submatches")[0];
        int column = submatch.GetProperty("start").GetInt32() + 1;
        string match = submatch.GetProperty("match").GetProperty("text").GetString() ?? string.Empty;
        result = new CodeSearchResult(relative, Path.Combine(root, relative), line, column, preview.TrimStart(), match);
        return true;
    }

    private static IReadOnlyList<CodeSearchResult> SearchManaged(
        string root,
        string query,
        CodeSearchOptions options,
        CancellationToken cancellationToken)
    {
        List<CodeSearchResult> results = [];
        Regex? regex = options.UseRegex
            ? new Regex(query, options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2))
            : null;
        StringComparison comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (string path in EnumerateSearchableFiles(root, options.IncludeHidden, options.FilePatterns))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using StreamReader reader = new(path, detectEncodingFromByteOrderMarks: true);
                int lineNumber = 0;
                while (reader.ReadLine() is { } line)
                {
                    lineNumber++;
                    Match? regexMatch = regex?.Match(line);
                    int index = regexMatch?.Success == true ? regexMatch.Index : line.IndexOf(query, comparison);
                    if (index < 0 || (options.WholeWord && !IsWholeWord(line, index, regexMatch?.Length ?? query.Length))) continue;
                    string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    results.Add(new CodeSearchResult(
                        relative, path, lineNumber, index + 1, line.Trim(), regexMatch?.Value ?? query));
                    if (results.Count >= options.MaximumResults) return results;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                // A file can change while the workspace is being searched. Skip it and continue.
            }
        }
        return results;
    }

    private static IEnumerable<string> EnumerateSearchableFiles(string root, bool includeHidden, string patterns)
    {
        Stack<string> pending = new();
        pending.Push(root);
        string[] parsedPatterns = ParsePatterns(patterns).ToArray();
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (UnauthorizedAccessException) { continue; }
            foreach (string path in entries)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch { continue; }
                if (!includeHidden && attributes.HasFlag(FileAttributes.Hidden)) continue;
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!attributes.HasFlag(FileAttributes.ReparsePoint) && !ExcludedDirectories.Contains(Path.GetFileName(path)))
                        pending.Push(path);
                    continue;
                }
                if (new FileInfo(path).Length > MaximumPreviewBytes) continue;
                if (parsedPatterns.Length > 0 && !parsedPatterns.Any(pattern =>
                        FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true))) continue;
                yield return path;
            }
        }
    }

    private static IReadOnlyList<CodeSymbol> ExtractSymbols(string text, string language)
    {
        if (text.Length > 500_000) return [];
        Regex regex = language switch
        {
            "C#" => new Regex(@"^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|async|readonly)\s+)*(class|record|struct|interface|enum|namespace)\s+([A-Za-z_][\w.]*)|^\s*(?:(?:public|private|protected|internal|static|virtual|override|async|sealed|abstract|partial|extern)\s+)+(?:[\w<>,?\[\].]+\s+)+([A-Za-z_]\w*)\s*\(", RegexOptions.Multiline),
            "C/C++" => new Regex(@"^\s*(class|struct|enum|namespace)\s+([A-Za-z_]\w*)|^\s*[\w:<>,~*&\s]+\s+([A-Za-z_]\w*)\s*\([^;]*\)\s*(?:const\s*)?\{", RegexOptions.Multiline),
            "Python" => new Regex(@"^\s*(class|def)\s+([A-Za-z_]\w*)", RegexOptions.Multiline),
            "JavaScript" or "TypeScript" => new Regex(@"^\s*(class|interface|type|enum|function)\s+([A-Za-z_$][\w$]*)|^\s*(?:export\s+)?(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=", RegexOptions.Multiline),
            _ => new Regex("(?!)")
        };
        List<CodeSymbol> symbols = [];
        foreach (Match match in regex.Matches(text).Cast<Match>().Take(500))
        {
            string kind = match.Groups[1].Success ? match.Groups[1].Value : "member";
            string name = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            if (name.Length == 0) continue;
            int line = 1 + CountCharacter(text.AsSpan(0, match.Index), '\n');
            symbols.Add(new CodeSymbol(kind, name, line));
        }
        return symbols;
    }

    private static string DetectLanguage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "C#", ".cpp" or ".h" or ".hpp" or ".c" or ".cc" => "C/C++",
        ".py" => "Python", ".js" or ".jsx" => "JavaScript", ".ts" or ".tsx" => "TypeScript",
        ".json" or ".jsonc" => "JSON", ".xml" or ".axaml" or ".xaml" => "XML/XAML",
        ".md" => "Markdown", ".uplugin" or ".uproject" or ".ini" => "Unreal",
        ".sh" or ".ps1" or ".cmd" or ".bat" => "Script", ".yml" or ".yaml" => "YAML",
        ".html" or ".css" or ".scss" => "Web", ".java" or ".kt" => "JVM",
        ".rs" => "Rust", ".go" => "Go", ".sql" => "SQL", _ => "Text"
    };

    private static bool IsBinary(ReadOnlySpan<byte> bytes) => bytes.IndexOf((byte)0) >= 0;

    private static string NormalizeRoot(string rootPath)
    {
        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return Path.TrimEndingDirectorySeparator(root);
    }

    private static string ResolveInsideRoot(string rootPath, string relativePath)
    {
        string root = NormalizeRoot(rootPath);
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        string prefix = root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison) && !string.Equals(full, root, comparison))
            throw new InvalidOperationException("The selected path is outside the project workspace.");
        return full;
    }

    private static IEnumerable<string> ParsePatterns(string value) => value
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(pattern => pattern.Replace('\\', '/'));

    private static bool IsWholeWord(string line, int index, int length)
    {
        bool left = index == 0 || !IsWord(line[index - 1]);
        int after = index + length;
        bool right = after >= line.Length || !IsWord(line[after]);
        return left && right;
    }

    private static bool IsWord(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static int CountCharacter(ReadOnlySpan<char> value, char character)
    {
        int count = 0;
        foreach (char current in value)
        {
            if (current == character) count++;
        }
        return count;
    }

    private static CodeHistoryEntry? ParseHistoryLine(string line)
    {
        string[] parts = line.Split('\u001f');
        if (parts.Length < 6 || !DateTimeOffset.TryParse(parts[4], out DateTimeOffset date)) return null;
        return new CodeHistoryEntry(parts[1], parts[2], parts[3], date, parts[5]);
    }

    private static async Task<ProcessOutput> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Unable to start {executable}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        return new ProcessOutput(process.ExitCode, await output, await error);
    }

    private sealed class BuildCounters
    {
        public int Directories { get; set; }
        public int Files { get; set; }
        public long Bytes { get; set; }
        public bool Truncated { get; set; }
    }

    private sealed record ProcessOutput(int ExitCode, string Output, string Error);
}
