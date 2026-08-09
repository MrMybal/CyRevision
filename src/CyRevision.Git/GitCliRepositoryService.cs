using System.Globalization;
using System.Text.RegularExpressions;

namespace CyRevision.Git;

public sealed partial class GitCliRepositoryService : IGitRepositoryService
{
    private readonly string _gitExecutable;
    private readonly ProcessRunner _processRunner;

    public GitCliRepositoryService(string gitExecutable = "git")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        _gitExecutable = gitExecutable;
        _processRunner = new ProcessRunner();
    }

    public async Task<GitToolAvailability> GetToolAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult gitResult;
        try
        {
            gitResult = await _processRunner.RunAsync(
                    _gitExecutable,
                    ["--version"],
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return new GitToolAvailability(false, null, false, null);
        }

        if (!gitResult.Succeeded)
        {
            return new GitToolAvailability(false, null, false, null);
        }

        ProcessResult lfsResult = await _processRunner.RunAsync(
                _gitExecutable,
                ["lfs", "version"],
                null,
                cancellationToken)
            .ConfigureAwait(false);

        return new GitToolAvailability(
            true,
            gitResult.StandardOutput.Trim(),
            lfsResult.Succeeded,
            lfsResult.Succeeded ? lfsResult.StandardOutput.Trim() : null);
    }

    public async Task InitializeAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(repositoryPath);
        Directory.CreateDirectory(fullPath);
        await RunGitAsync(fullPath, ["init", "-b", "main"], cancellationToken).ConfigureAwait(false);
        await RunGitAsync(fullPath, ["lfs", "install", "--local"], cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitRepositoryStatus> GetStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string root = (await RunGitAsync(
                repositoryPath,
                ["rev-parse", "--show-toplevel"],
                cancellationToken)
            .ConfigureAwait(false)).Trim();

        string branch = (await RunGitAsync(
                root,
                ["branch", "--show-current"],
                cancellationToken)
            .ConfigureAwait(false)).Trim();

        bool detached = string.IsNullOrWhiteSpace(branch);
        if (detached)
        {
            branch = (await RunGitAsync(root, ["rev-parse", "--short", "HEAD"], cancellationToken)
                    .ConfigureAwait(false))
                .Trim();
        }

        string remotes = await RunGitAsync(root, ["remote"], cancellationToken).ConfigureAwait(false);
        string porcelain = await RunGitAsync(
                root,
                ["status", "--porcelain=v1", "-z", "--branch"],
                cancellationToken)
            .ConfigureAwait(false);

        HashSet<string> lfsFiles = await GetLfsFileNamesAsync(root, cancellationToken).ConfigureAwait(false);
        (int ahead, int behind) = ParseAheadBehind(porcelain);
        IReadOnlyList<GitChange> changes = ParseStatus(porcelain, lfsFiles);

        return new GitRepositoryStatus(
            root,
            branch,
            detached,
            !string.IsNullOrWhiteSpace(remotes),
            ahead,
            behind,
            changes);
    }

    public async Task ConfigureIdentityAsync(
        string repositoryPath,
        string userName,
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);
        await RunGitAsync(repositoryPath, ["config", "user.name", userName], cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repositoryPath, ["config", "user.email", userEmail], cancellationToken).ConfigureAwait(false);
    }

    public Task StageAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        EnsurePaths(paths);
        return RunGitWithoutOutputAsync(repositoryPath, ["add", "--", .. paths], cancellationToken);
    }

    public async Task UnstageAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        EnsurePaths(paths);
        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                ["restore", "--staged", "--", .. paths],
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            await RunGitAsync(repositoryPath, ["rm", "--cached", "--", .. paths], cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task CreateRevisionAsync(
        string repositoryPath,
        string message,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (paths.Count > 0)
        {
            await StageAsync(repositoryPath, paths, cancellationToken).ConfigureAwait(false);
        }

        await RunGitAsync(repositoryPath, ["commit", "-m", message.Trim()], cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GitRevision>> GetHistoryAsync(
        string repositoryPath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                [
                    "log",
                    $"--max-count={maximumCount}",
                    "--date=iso-strict",
                    "--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e"
                ],
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded && result.StandardError.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        EnsureSuccess(result, "Unable to read Git history.");
        List<GitRevision> revisions = [];
        foreach (string entry in result.StandardOutput.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = entry.Trim().Split('\x1f');
            if (fields.Length != 6 ||
                !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt))
            {
                continue;
            }

            revisions.Add(new GitRevision(fields[0], fields[1], fields[2], fields[3], authoredAt, fields[5]));
        }

        return revisions;
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string output = await RunGitAsync(
                repositoryPath,
            ["for-each-ref", "--format=%(refname:short)%00%(objectname:short)%00%(HEAD)%00%(refname)", "refs/heads", "refs/remotes/cyrevision"],
                cancellationToken)
            .ConfigureAwait(false);

        List<GitBranch> branches = [];
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\0');
            if (fields.Length == 4)
            {
                branches.Add(new GitBranch(
                    fields[0],
                    fields[1],
                    fields[2] == "*",
                    fields[3].StartsWith("refs/remotes/", StringComparison.Ordinal)));
            }
        }

        return branches;
    }

    public Task CreateBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(branchName);
        return RunGitWithoutOutputAsync(repositoryPath, ["switch", "-c", branchName], cancellationToken);
    }

    public Task CreateBranchFromAsync(
        string repositoryPath,
        string branchName,
        string startPoint,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(branchName);
        ValidateReferenceName(startPoint);
        return RunGitWithoutOutputAsync(repositoryPath, ["switch", "-c", branchName, startPoint], cancellationToken);
    }

    public Task CheckoutBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(branchName);
        return RunGitWithoutOutputAsync(repositoryPath, ["switch", branchName], cancellationToken);
    }

    public Task MergeBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(branchName);
        return RunGitWithoutOutputAsync(repositoryPath, ["merge", "--no-edit", branchName], cancellationToken);
    }

    public Task<string> GetDiffAsync(
        string repositoryPath,
        string? relativePath = null,
        bool staged = false,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["diff", "--no-ext-diff", "--no-color"];
        if (staged)
        {
            arguments.Add("--staged");
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            arguments.Add("--");
            arguments.Add(relativePath);
        }

        return RunGitAsync(repositoryPath, arguments, cancellationToken);
    }

    public Task RestoreFileFromRevisionAsync(
        string repositoryPath,
        string relativePath,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ValidateReferenceName(revision);
        return RunGitWithoutOutputAsync(
            repositoryPath,
            ["restore", $"--source={revision}", "--worktree", "--", relativePath],
            cancellationToken);
    }

    public async Task ExportFileFromRevisionAsync(
        string repositoryPath,
        string relativePath,
        string revision,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ValidateReferenceName(revision);
        string gitPath = relativePath.Replace('\\', '/').TrimStart('/');
        ProcessResult result = await _processRunner.RunToFileAsync(
            "git",
            ["show", $"{revision}:{gitPath}"],
            repositoryPath,
            destinationPath,
            cancellationToken);
        if (!result.Succeeded)
        {
            File.Delete(destinationPath);
            throw new GitOperationException(result.StandardError.Trim());
        }
    }

    public async Task AddOrUpdateRemoteAsync(
        string repositoryPath,
        string remoteName,
        string remoteUrl,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(remoteName);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        ProcessResult getUrl = await RunGitResultAsync(
                repositoryPath,
                ["remote", "get-url", remoteName],
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<string> arguments = getUrl.Succeeded
            ? ["remote", "set-url", remoteName, remoteUrl]
            : ["remote", "add", remoteName, remoteUrl];
        await RunGitAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task FetchAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        RunGitWithoutOutputAsync(repositoryPath, ["fetch", "--all", "--prune"], cancellationToken);

    public Task PullAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        RunGitWithoutOutputAsync(repositoryPath, ["pull", "--ff-only"], cancellationToken);

    public async Task PushAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunGitResultAsync(repositoryPath, ["push"], cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return;
        }

        string branch = (await RunGitAsync(repositoryPath, ["branch", "--show-current"], cancellationToken)
                .ConfigureAwait(false))
            .Trim();
        if (string.IsNullOrWhiteSpace(branch))
        {
            EnsureSuccess(result, "Unable to push a detached HEAD.");
        }

        await RunGitAsync(repositoryPath, ["push", "--set-upstream", "origin", branch], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task TrackLfsPatternAsync(
        string repositoryPath,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        await RunGitAsync(repositoryPath, ["lfs", "track", pattern], cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repositoryPath, ["add", ".gitattributes"], cancellationToken).ConfigureAwait(false);
    }

    public async Task UntrackLfsPatternAsync(
        string repositoryPath,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        await RunGitAsync(repositoryPath, ["lfs", "untrack", pattern], cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repositoryPath, ["add", ".gitattributes"], cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LfsTrackedPattern>> GetLfsPatternsAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string attributesPath = Path.Combine(Path.GetFullPath(repositoryPath), ".gitattributes");
        if (!File.Exists(attributesPath))
        {
            return [];
        }

        string[] lines = await File.ReadAllLinesAsync(attributesPath, cancellationToken).ConfigureAwait(false);
        return lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains("filter=lfs", StringComparison.Ordinal))
            .Select(line => new LfsTrackedPattern(line.Split((char[]?)null, 2)[0], ".gitattributes"))
            .ToArray();
    }

    private async Task<HashSet<string>> GetLfsFileNamesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                ["lfs", "ls-files", "--name-only"],
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> RunGitAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "Git operation failed.");
        return result.StandardOutput;
    }

    private async Task RunGitWithoutOutputAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        await RunGitAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    private Task<ProcessResult> RunGitResultAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken) =>
        _processRunner.RunAsync(_gitExecutable, arguments, Path.GetFullPath(workingDirectory), cancellationToken);

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Exit code {result.ExitCode}."
            : result.StandardError.Trim();
        throw new GitOperationException($"{message} {detail}");
    }

    private static IReadOnlyList<GitChange> ParseStatus(string porcelain, IReadOnlySet<string> lfsFiles)
    {
        string[] records = porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        List<GitChange> changes = [];
        for (int index = 0; index < records.Length; index++)
        {
            string record = records[index];
            if (record.StartsWith("## ", StringComparison.Ordinal) || record.Length < 4)
            {
                continue;
            }

            char indexStatus = record[0];
            char workTreeStatus = record[1];
            string path = record[3..];
            string? originalPath = null;
            if ((indexStatus is 'R' or 'C' || workTreeStatus is 'R' or 'C') && index + 1 < records.Length)
            {
                originalPath = records[++index];
            }

            bool staged = indexStatus is not (' ' or '?');
            GitChangeKind kind = MapChangeKind(indexStatus, workTreeStatus);
            changes.Add(new GitChange(path, kind, staged, lfsFiles.Contains(path), originalPath));
        }

        return changes;
    }

    private static GitChangeKind MapChangeKind(char indexStatus, char workTreeStatus)
    {
        if (indexStatus == '?' && workTreeStatus == '?')
        {
            return GitChangeKind.Untracked;
        }

        if (indexStatus == 'U' || workTreeStatus == 'U' || (indexStatus == 'A' && workTreeStatus == 'A'))
        {
            return GitChangeKind.Conflicted;
        }

        if (indexStatus == 'R' || workTreeStatus == 'R')
        {
            return GitChangeKind.Renamed;
        }

        if (indexStatus == 'A' || workTreeStatus == 'A')
        {
            return GitChangeKind.Added;
        }

        if (indexStatus == 'D' || workTreeStatus == 'D')
        {
            return GitChangeKind.Deleted;
        }

        return GitChangeKind.Modified;
    }

    private static (int Ahead, int Behind) ParseAheadBehind(string porcelain)
    {
        string? branchLine = porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(record => record.StartsWith("## ", StringComparison.Ordinal));
        if (branchLine is null)
        {
            return (0, 0);
        }

        Match match = AheadBehindRegex().Match(branchLine);
        int ahead = match.Groups["ahead"].Success ? int.Parse(match.Groups["ahead"].Value, CultureInfo.InvariantCulture) : 0;
        int behind = match.Groups["behind"].Success ? int.Parse(match.Groups["behind"].Value, CultureInfo.InvariantCulture) : 0;
        return (ahead, behind);
    }

    private static void EnsurePaths(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0 || paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one valid path is required.", nameof(paths));
        }
    }

    private static void ValidateReferenceName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.StartsWith('-') || value.Contains('\0') || value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The Git reference is invalid.", nameof(value));
        }
    }

    [GeneratedRegex(@"(?:ahead (?<ahead>\d+))|(?:behind (?<behind>\d+))", RegexOptions.CultureInvariant)]
    private static partial Regex AheadBehindRegex();
}
