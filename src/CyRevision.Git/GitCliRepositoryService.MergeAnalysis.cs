using System.Text.RegularExpressions;

namespace CyRevision.Git;

public sealed partial class GitCliRepositoryService
{
    public async Task<GitMergeConflictAnalysis> AnalyzeMergeConflictsAsync(
        string repositoryPath,
        string baseReference,
        string headReference,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(baseReference);
        ValidateReferenceName(headReference);
        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                ["merge-tree", "--write-tree", "--name-only", "--messages", baseReference, headReference],
                cancellationToken)
            .ConfigureAwait(false);
        string detail = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput.Trim(), result.StandardError.Trim() }
                .Where(value => value.Length > 0));
        if (result.Succeeded)
            return new GitMergeConflictAnalysis(true, [], detail);

        if (result.ExitCode != 1)
        {
            EnsureSuccess(result, "Unable to analyze the pull-request merge.");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string[] lines = result.StandardOutput.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool passedTreeObject = false;
        foreach (string line in lines)
        {
            if (!passedTreeObject && Regex.IsMatch(line, "^[0-9a-f]{40,64}$", RegexOptions.IgnoreCase))
            {
                passedTreeObject = true;
                continue;
            }

            Match conflictIn = Regex.Match(
                line,
                @"CONFLICT\s+\([^)]+\):.*?\bin\s+(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (conflictIn.Success)
            {
                paths.Add(UnquotePath(conflictIn.Groups[1].Value));
                continue;
            }

            Match modifyDelete = Regex.Match(
                line,
                @"CONFLICT\s+\(modify/delete\):\s+(.+?)\s+deleted\s+in\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (modifyDelete.Success)
            {
                paths.Add(UnquotePath(modifyDelete.Groups[1].Value));
                continue;
            }

            if (passedTreeObject &&
                !line.StartsWith("Auto-merging ", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("CONFLICT ", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains(':') &&
                line.IndexOfAny(['/', '\\', '.']) >= 0)
            {
                paths.Add(UnquotePath(line));
            }
        }

        return new GitMergeConflictAnalysis(
            false,
            paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            detail);
    }

    private static string UnquotePath(string path)
    {
        string normalized = path.Trim();
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
            normalized = normalized[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        return normalized.Replace('\\', '/');
    }
}