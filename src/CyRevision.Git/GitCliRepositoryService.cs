using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public async Task CloneAsync(
        string remoteUrl,
        string destinationPath,
        bool recurseSubmodules = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string fullDestination = Path.GetFullPath(destinationPath);
        string? parentPath = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            throw new ArgumentException("The clone destination must have a parent folder.", nameof(destinationPath));
        }

        Directory.CreateDirectory(parentPath);
        if (Directory.Exists(fullDestination) && Directory.EnumerateFileSystemEntries(fullDestination).Any())
        {
            throw new IOException("The clone destination already exists and is not empty.");
        }

        List<string> arguments = ["clone", "--progress"];
        if (recurseSubmodules)
        {
            arguments.Add("--recurse-submodules");
        }
        arguments.Add(remoteUrl.Trim());
        arguments.Add(fullDestination);

        await RunGitAsync(parentPath, arguments, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(fullDestination, ["lfs", "install", "--local"], cancellationToken).ConfigureAwait(false);
    }

    public Task<GitRepositoryStatus> GetStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default) =>
        GetStatusCoreAsync(repositoryPath, untrackedFilesMode: "all", cancellationToken);

    public Task<GitRepositoryStatus> GetQuickStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default) =>
        GetStatusCoreAsync(repositoryPath, untrackedFilesMode: "no", cancellationToken);

    public Task<GitRepositoryStatus> GetDetailedStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default) =>
        GetStatusCoreAsync(repositoryPath, untrackedFilesMode: "all", cancellationToken);

    private async Task<GitRepositoryStatus> GetStatusCoreAsync(
        string repositoryPath,
        string untrackedFilesMode,
        CancellationToken cancellationToken)
    {
        string root = (await RunGitAsync(
                repositoryPath,
                ["rev-parse", "--show-toplevel"],
                cancellationToken)
            .ConfigureAwait(false)).Trim();

        Task<string> branchTask = RunGitAsync(root, ["branch", "--show-current"], cancellationToken);
        Task<string> remotesTask = RunGitAsync(root, ["remote"], cancellationToken);
        Task<string> porcelainTask = RunGitAsync(
            root,
            ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--branch", $"--untracked-files={untrackedFilesMode}"],
            cancellationToken);
        await Task.WhenAll(branchTask, remotesTask, porcelainTask).ConfigureAwait(false);

        string branch = (await branchTask.ConfigureAwait(false)).Trim();

        bool detached = string.IsNullOrWhiteSpace(branch);
        if (detached)
        {
            branch = (await RunGitAsync(root, ["rev-parse", "--short", "HEAD"], cancellationToken)
                    .ConfigureAwait(false))
                .Trim();
        }

        string remotes = await remotesTask.ConfigureAwait(false);
        string porcelain = await porcelainTask.ConfigureAwait(false);

        IReadOnlyList<GitChange> preliminaryChanges = ParseStatus(
            porcelain,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        HashSet<string> lfsFiles = await GetLfsFileNamesForPathsAsync(
                root,
                preliminaryChanges.Select(change => change.Path),
                cancellationToken)
            .ConfigureAwait(false);
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

    public async Task StageAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        EnsurePaths(paths);
        await RunGitPathspecCommandAsync(
                repositoryPath,
                ["add"],
                paths,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UnstageAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        EnsurePaths(paths);
        ProcessResult result = await RunGitPathspecResultAsync(
                repositoryPath,
                ["restore", "--staged"],
                paths,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            await RunGitPathspecCommandAsync(
                    repositoryPath,
                    ["rm", "--cached"],
                    paths,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task DiscardChangesAsync(
        string repositoryPath,
        IReadOnlyCollection<GitChange> changes,
        CancellationToken cancellationToken = default)
    {
        if (changes.Count == 0)
        {
            throw new ArgumentException("At least one change is required.", nameof(changes));
        }

        string root = Path.GetFullPath(repositoryPath);
        foreach (GitChange change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] paths = string.IsNullOrWhiteSpace(change.OriginalPath)
                ? [change.Path]
                : [change.Path, change.OriginalPath];
            EnsurePaths(paths);

            if (change.Kind is GitChangeKind.Untracked or GitChangeKind.Added)
            {
                if (change.Kind == GitChangeKind.Added && change.IsStaged)
                {
                    await UnstageAsync(root, paths, cancellationToken).ConfigureAwait(false);
                }

                foreach (string path in paths)
                {
                    DeleteUntrackedPath(root, path);
                }
                continue;
            }

            await RunGitWithoutOutputAsync(
                    root,
                    ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. paths],
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task DeleteWorkingTreePathsAsync(
        string repositoryPath,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        EnsurePaths(paths);
        string root = Path.GetFullPath(repositoryPath);
        string[] uniquePaths = paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.Run(() =>
        {
            foreach (string path in uniquePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteWorkingTreeFile(root, path);
            }
        }, cancellationToken);
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

    public Task<IReadOnlyList<GitRevision>> GetHistoryAsync(
        string repositoryPath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default) =>
        GetHistoryCoreAsync(repositoryPath, maximumCount, includeAllRefs: false, null, cancellationToken);

    public Task<IReadOnlyList<GitRevision>> GetHistoryAcrossRefsAsync(
        string repositoryPath,
        int maximumCount = 500,
        CancellationToken cancellationToken = default) =>
        GetHistoryCoreAsync(repositoryPath, maximumCount, includeAllRefs: true, null, cancellationToken);

    public Task<IReadOnlyList<GitRevision>> GetHistoryForReferenceAsync(
        string repositoryPath,
        string reference,
        int maximumCount = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(reference);
        return GetHistoryCoreAsync(repositoryPath, maximumCount, includeAllRefs: false, reference, cancellationToken);
    }

    private async Task<IReadOnlyList<GitRevision>> GetHistoryCoreAsync(
        string repositoryPath,
        int maximumCount,
        bool includeAllRefs,
        string? reference,
        CancellationToken cancellationToken)
    {
        if (maximumCount is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        List<string> arguments = ["log"];
        if (includeAllRefs)
        {
            arguments.Add("--all");
        }
        else if (!string.IsNullOrWhiteSpace(reference))
        {
            arguments.Add(reference);
        }
        arguments.Add($"--max-count={maximumCount}");
        arguments.Add("--date=iso-strict");
        arguments.Add("--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e");
        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                arguments,
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

    public async Task<IReadOnlyList<GitGraphCommit>> GetCommitGraphAsync(
        string repositoryPath,
        int maximumCount = 250,
        bool includeAllBranches = true,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        List<string> arguments = ["log"];
        if (includeAllBranches)
        {
            arguments.Add("--all");
        }

        arguments.AddRange([
            "--topo-order",
            $"--max-count={maximumCount}",
            "--date=iso-strict",
            "--decorate=short",
            "--pretty=format:%H%x1f%h%x1f%P%x1f%an%x1f%aI%x1f%s%x1f%D%x1e"
        ]);
        ProcessResult result = await RunGitResultAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && result.StandardError.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        EnsureSuccess(result, "Unable to read the Git commit graph.");
        List<GitGraphCommit> commits = [];
        foreach (string entry in result.StandardOutput.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = entry.Trim().Split('\x1f');
            if (fields.Length != 7 ||
                !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt))
            {
                continue;
            }

            commits.Add(new GitGraphCommit(
                fields[0],
                fields[1],
                fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                fields[3],
                authoredAt,
                fields[5],
                fields[6]));
        }

        return commits;
    }

    public async Task<GitFileActivityGraph> GetFileActivityGraphAsync(
        string repositoryPath,
        int maximumCommitCount = 250,
        int maximumFileCount = 80,
        bool includeAllBranches = true,
        CancellationToken cancellationToken = default)
    {
        if (maximumCommitCount is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCommitCount));
        }

        if (maximumFileCount is < 5 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileCount));
        }

        List<string> arguments = ["-c", "core.quotepath=false", "log"];
        if (includeAllBranches)
        {
            arguments.Add("--all");
        }

        arguments.AddRange([
            "--date-order",
            $"--max-count={maximumCommitCount}",
            "--date=iso-strict",
            "--pretty=format:%x1e%H%x1f%aI",
            "--numstat"
        ]);
        ProcessResult result = await RunGitResultAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && result.StandardError.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase))
        {
            return new GitFileActivityGraph([], [], 0, 0);
        }

        EnsureSuccess(result, "Unable to analyze Git file activity.");
        Dictionary<string, MutableFileActivity> activities = new(StringComparer.Ordinal);
        List<HashSet<string>> commitFiles = [];
        int commitCount = 0;
        foreach (string rawEntry in result.StandardOutput.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            string entry = rawEntry.TrimStart('\r', '\n');
            int lineBreak = entry.IndexOfAny(['\r', '\n']);
            string header = lineBreak < 0 ? entry : entry[..lineBreak];
            string[] headerFields = header.Split('\x1f');
            if (headerFields.Length != 2 ||
                !DateTimeOffset.TryParse(headerFields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset changedAt))
            {
                continue;
            }

            commitCount++;
            HashSet<string> pathsInCommit = new(StringComparer.Ordinal);
            if (lineBreak >= 0)
            {
                foreach (string line in entry[(lineBreak + 1)..].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = line.Split('\t', 3);
                    if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[2]))
                    {
                        continue;
                    }

                    string path = NormalizeHistoryPath(fields[2]);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    pathsInCommit.Add(path);
                    if (!activities.TryGetValue(path, out MutableFileActivity? activity))
                    {
                        activity = new MutableFileActivity(path, ClassifyFile(path));
                        activities.Add(path, activity);
                    }

                    activity.ChangeCount++;
                    activity.LastChangedAt = activity.LastChangedAt > changedAt ? activity.LastChangedAt : changedAt;
                    if (fields[0] == "-" || fields[1] == "-")
                    {
                        activity.BinaryChangeCount++;
                    }
                    else
                    {
                        if (long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long additions))
                        {
                            activity.AddedLines += additions;
                        }

                        if (long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long deletions))
                        {
                            activity.DeletedLines += deletions;
                        }
                    }
                }
            }

            commitFiles.Add(pathsInCommit);
        }

        GitFileActivity[] files = activities.Values
            .OrderByDescending(activity => activity.ChangeCount)
            .ThenByDescending(activity => activity.AddedLines + activity.DeletedLines)
            .ThenBy(activity => activity.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maximumFileCount)
            .Select(activity => activity.ToRecord())
            .ToArray();
        HashSet<string> selectedPaths = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        Dictionary<(string Source, string Target), int> relations = new();
        foreach (HashSet<string> changedPaths in commitFiles)
        {
            string[] selected = changedPaths.Where(selectedPaths.Contains)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(40)
                .ToArray();
            for (int left = 0; left < selected.Length; left++)
            {
                for (int right = left + 1; right < selected.Length; right++)
                {
                    (string, string) key = (selected[left], selected[right]);
                    relations[key] = relations.GetValueOrDefault(key) + 1;
                }
            }
        }

        GitFileRelation[] graphRelations = relations
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Source, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Target, StringComparer.Ordinal)
            .Take(300)
            .Select(pair => new GitFileRelation(pair.Key.Source, pair.Key.Target, pair.Value))
            .ToArray();
        return new GitFileActivityGraph(files, graphRelations, commitCount, activities.Count);
    }

    public async Task<GitRepositoryInsights> GetRepositoryInsightsAsync(
        string repositoryPath,
        int maximumCommitCount = 500,
        bool includeAllBranches = true,
        CancellationToken cancellationToken = default)
    {
        if (maximumCommitCount is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCommitCount));
        }

        List<string> arguments = ["-c", "core.quotepath=false", "log"];
        if (includeAllBranches)
        {
            arguments.Add("--all");
        }

        arguments.AddRange([
            "--date-order",
            $"--max-count={maximumCommitCount}",
            "--date=iso-strict",
            "--pretty=format:%x1e%H%x1f%P%x1f%an%x1f%ae%x1f%aI",
            "--numstat"
        ]);
        ProcessResult result = await RunGitResultAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && result.StandardError.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase))
        {
            return new GitRepositoryInsights(0, 0, 0, 0, 0, 0, 0, [], [], []);
        }

        EnsureSuccess(result, "Unable to analyze repository activity.");
        Dictionary<(string Name, string Email), MutableContributorActivity> contributors = new();
        Dictionary<DateOnly, MutableDailyActivity> days = new();
        HashSet<string> allFiles = new(StringComparer.Ordinal);
        int commitCount = 0;
        int mergeCount = 0;
        long totalAdded = 0;
        long totalDeleted = 0;
        int binaryChanges = 0;
        foreach (string rawEntry in result.StandardOutput.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            string entry = rawEntry.TrimStart('\r', '\n');
            int lineBreak = entry.IndexOfAny(['\r', '\n']);
            string header = lineBreak < 0 ? entry : entry[..lineBreak];
            string[] fields = header.Split('\x1f');
            if (fields.Length != 5 ||
                !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt))
            {
                continue;
            }

            commitCount++;
            if (fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1)
            {
                mergeCount++;
            }

            (string Name, string Email) contributorKey = (fields[2], fields[3]);
            if (!contributors.TryGetValue(contributorKey, out MutableContributorActivity? contributor))
            {
                contributor = new MutableContributorActivity(fields[2], fields[3]);
                contributors[contributorKey] = contributor;
            }

            contributor.CommitCount++;
            contributor.LastActiveAt = contributor.LastActiveAt > authoredAt ? contributor.LastActiveAt : authoredAt;
            DateOnly dayKey = DateOnly.FromDateTime(authoredAt.LocalDateTime);
            if (!days.TryGetValue(dayKey, out MutableDailyActivity? day))
            {
                day = new MutableDailyActivity(dayKey);
                days[dayKey] = day;
            }

            day.CommitCount++;
            if (lineBreak < 0)
            {
                continue;
            }

            foreach (string line in entry[(lineBreak + 1)..].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] stat = line.Split('\t', 3);
                if (stat.Length != 3)
                {
                    continue;
                }

                string path = NormalizeHistoryPath(stat[2]);
                contributor.Files.Add(path);
                day.Files.Add(path);
                allFiles.Add(path);
                if (stat[0] == "-" || stat[1] == "-")
                {
                    contributor.BinaryChanges++;
                    binaryChanges++;
                    continue;
                }

                long added = long.TryParse(stat[0], NumberStyles.None, CultureInfo.InvariantCulture, out long parsedAdded)
                    ? parsedAdded
                    : 0;
                long deleted = long.TryParse(stat[1], NumberStyles.None, CultureInfo.InvariantCulture, out long parsedDeleted)
                    ? parsedDeleted
                    : 0;
                contributor.AddedLines += added;
                contributor.DeletedLines += deleted;
                day.AddedLines += added;
                day.DeletedLines += deleted;
                totalAdded += added;
                totalDeleted += deleted;
            }
        }

        GitFileActivityGraph fileActivity = await GetFileActivityGraphAsync(
                repositoryPath,
                maximumCommitCount,
                30,
                includeAllBranches,
                cancellationToken)
            .ConfigureAwait(false);
        return new GitRepositoryInsights(
            commitCount,
            mergeCount,
            contributors.Count,
            allFiles.Count,
            totalAdded,
            totalDeleted,
            binaryChanges,
            contributors.Values
                .OrderByDescending(value => value.CommitCount)
                .ThenBy(value => value.AuthorName, StringComparer.OrdinalIgnoreCase)
                .Select(value => value.ToRecord())
                .ToArray(),
            days.Values.OrderBy(value => value.Day).Select(value => value.ToRecord()).ToArray(),
            fileActivity.Files.Take(20).ToArray());
    }

    public async Task<GitCommitDetails> GetCommitDetailsAsync(
        string repositoryPath,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(revision);
        ProcessResult headerResult = await RunGitResultAsync(
                repositoryPath,
                [
                    "show", "-s", "--date=iso-strict",
                    "--format=%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%P%x1f%B",
                    revision
                ],
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(headerResult, "Unable to inspect the Git revision.");

        string[] fields = headerResult.StandardOutput.TrimEnd().Split('\x1f', 8);
        if (fields.Length != 8 ||
            !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt))
        {
            throw new GitOperationException("Git returned an invalid revision description.");
        }

        GitRevision parsedRevision = new(fields[0], fields[1], fields[2], fields[3], authoredAt, fields[5]);
        string[] parentHashes = fields[6].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<GitCommitFileChange> files = await GetRevisionChangesAsync(
                repositoryPath,
                parentHashes.Length > 1 ? parentHashes[0] : revision,
                parentHashes.Length > 1 ? revision : null,
                cancellationToken)
            .ConfigureAwait(false);
        return new GitCommitDetails(
            parsedRevision,
            parentHashes,
            fields[7].Trim(),
            files,
            files.Where(file => !file.IsBinary).Sum(file => file.AddedLines ?? 0),
            files.Where(file => !file.IsBinary).Sum(file => file.DeletedLines ?? 0),
            files.Count(file => file.IsBinary));
    }

    public async Task<GitCommitComparison> CompareCommitsAsync(
        string repositoryPath,
        string fromRevision,
        string toRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(fromRevision);
        ValidateReferenceName(toRevision);
        IReadOnlyList<GitCommitFileChange> files = await GetRevisionChangesAsync(
                repositoryPath,
                fromRevision,
                toRevision,
                cancellationToken)
            .ConfigureAwait(false);
        return new GitCommitComparison(
            fromRevision,
            toRevision,
            files,
            files.Where(file => !file.IsBinary).Sum(file => file.AddedLines ?? 0),
            files.Where(file => !file.IsBinary).Sum(file => file.DeletedLines ?? 0),
            files.Count(file => file.IsBinary));
    }

    public async Task<string> GetCommitDiffAsync(
        string repositoryPath,
        string revision,
        string? relativePath = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(revision);
        List<string> arguments = ["diff", "--no-ext-diff", "--no-color", "--no-renames", $"{revision}^1", revision];
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            arguments.Add("--");
            arguments.Add(relativePath);
        }

        ProcessResult result = await RunGitResultAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) return result.StandardOutput;

        List<string> rootArguments = ["show", "--format=", "--no-ext-diff", "--no-color", "--no-renames", revision];
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            rootArguments.Add("--");
            rootArguments.Add(relativePath);
        }
        return await RunGitAsync(repositoryPath, rootArguments, cancellationToken).ConfigureAwait(false);
    }

    public Task<string> GetComparisonDiffAsync(
        string repositoryPath,
        string fromRevision,
        string toRevision,
        string? relativePath = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(fromRevision);
        ValidateReferenceName(toRevision);
        List<string> arguments = ["diff", "--no-ext-diff", "--no-color", "--no-renames", fromRevision, toRevision];
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            arguments.Add("--");
            arguments.Add(relativePath);
        }

        return RunGitAsync(repositoryPath, arguments, cancellationToken);
    }

    public async Task<IReadOnlyList<GitFileRevision>> GetFileHistoryAsync(
        string repositoryPath,
        string relativePath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (maximumCount is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                [
                    "-c", "core.quotepath=false", "log", "--follow", $"--max-count={maximumCount}",
                    "--date=iso-strict", "--format=CYREVHIST%x1f%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s",
                    "--raw", "--numstat",
                    "--", relativePath
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded && result.StandardError.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        EnsureSuccess(result, "Unable to read the file history.");
        List<GitFileRevision> history = [];
        GitRevision? revision = null;
        string path = relativePath.Replace('\\', '/');
        GitChangeKind kind = GitChangeKind.Modified;
        long? addedLines = null;
        long? deletedLines = null;
        bool isBinary = false;

        void CompleteRevision()
        {
            if (revision is null) return;
            history.Add(new GitFileRevision(
                revision,
                path,
                kind,
                addedLines,
                deletedLines,
                isBinary));
        }

        foreach (string rawLine in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("CYREVHIST\x1f", StringComparison.Ordinal))
            {
                CompleteRevision();
                string[] fields = line.Split('\x1f');
                if (fields.Length != 7 ||
                    !DateTimeOffset.TryParse(fields[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt))
                {
                    revision = null;
                    continue;
                }
                revision = new GitRevision(fields[1], fields[2], fields[3], fields[4], authoredAt, fields[6]);
                path = relativePath.Replace('\\', '/');
                kind = GitChangeKind.Modified;
                addedLines = null;
                deletedLines = null;
                isBinary = false;
                continue;
            }

            if (revision is null) continue;
            if (line[0] == ':' && line.IndexOf('\t') is int rawTab && rawTab > 0)
            {
                string[] metadata = line[..rawTab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string[] paths = line[(rawTab + 1)..].Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (metadata.Length > 0)
                {
                    kind = metadata[^1][0] switch
                    {
                        'A' => GitChangeKind.Added,
                        'D' => GitChangeKind.Deleted,
                        'R' => GitChangeKind.Renamed,
                        'U' => GitChangeKind.Conflicted,
                        _ => GitChangeKind.Modified
                    };
                }
                if (paths.Length > 0) path = NormalizeHistoryPath(paths[^1]);
                continue;
            }

            string[] stat = line.Split('\t', 3);
            if (stat.Length != 3 || (stat[0] != "-" && !long.TryParse(stat[0], out _))) continue;
            path = NormalizeHistoryPath(stat[2]);
            if (stat[0] == "-" || stat[1] == "-")
            {
                isBinary = true;
            }
            else
            {
                addedLines = long.TryParse(stat[0], NumberStyles.None, CultureInfo.InvariantCulture, out long added) ? added : 0;
                deletedLines = long.TryParse(stat[1], NumberStyles.None, CultureInfo.InvariantCulture, out long deleted) ? deleted : 0;
            }
        }
        CompleteRevision();

        HashSet<string> lfsPaths = await GetLfsFileNamesForPathsAsync(
            repositoryPath,
            [relativePath],
            cancellationToken).ConfigureAwait(false);
        if (lfsPaths.Count > 0)
        {
            for (int start = 0; start < history.Count; start += 8)
            {
                int count = Math.Min(8, history.Count - start);
                Task<LfsPointerInfo?>[] pointerTasks = new Task<LfsPointerInfo?>[count];
                for (int offset = 0; offset < count; offset++)
                {
                    GitFileRevision item = history[start + offset];
                    pointerTasks[offset] = item.Kind == GitChangeKind.Deleted
                        ? Task.FromResult<LfsPointerInfo?>(null)
                        : TryReadLfsPointerAtRevisionAsync(
                            repositoryPath,
                            item.Revision.Hash,
                            item.Path,
                            cancellationToken);
                }
                LfsPointerInfo?[] pointers = await Task.WhenAll(pointerTasks).ConfigureAwait(false);
                for (int offset = 0; offset < count; offset++)
                    history[start + offset] = history[start + offset] with { LfsPointer = pointers[offset] };
            }
        }

        return history;
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string output = await RunGitAsync(
                repositoryPath,
            ["for-each-ref", "--format=%(refname:short)%00%(objectname:short)%00%(HEAD)%00%(refname)%00%(upstream:short)%00%(upstream:track,nobracket)%00%(authorname)%00%(authordate:iso-strict)%00%(subject)", "refs/heads", "refs/remotes"],
                cancellationToken)
            .ConfigureAwait(false);

        List<GitBranch> branches = [];
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\0');
            if (fields.Length >= 9 && !fields[0].EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
            {
                (int ahead, int behind) = ParseTrackingCounts(fields[5]);
                DateTimeOffset? tipAuthoredAt = DateTimeOffset.TryParse(
                    fields[7],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsedTipAuthoredAt)
                    ? parsedTipAuthoredAt
                    : null;
                branches.Add(new GitBranch(
                    fields[0],
                    fields[1],
                    fields[2] == "*",
                    fields[3].StartsWith("refs/remotes/", StringComparison.Ordinal),
                    string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4],
                    ahead,
                    behind,
                    !string.IsNullOrWhiteSpace(fields[4]),
                    string.IsNullOrWhiteSpace(fields[6]) ? "Unknown" : fields[6],
                    tipAuthoredAt,
                    fields[8]));
            }
        }

        Dictionary<string, GitBranch> exactRemoteBranches = branches
            .Where(branch => branch.IsRemote)
            .GroupBy(branch => branch.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, GitBranch> remoteBranchesByLocalName = branches
            .Where(branch => branch.IsRemote)
            .Select(branch => new
            {
                Branch = branch,
                LocalName = branch.Name.IndexOf('/') is int separator && separator >= 0
                    ? branch.Name[(separator + 1)..]
                    : branch.Name
            })
            .GroupBy(item => item.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Branch, StringComparer.Ordinal);
        return branches.Select(branch =>
        {
            if (branch.IsRemote || !string.IsNullOrWhiteSpace(branch.RemoteName))
            {
                return branch;
            }

            exactRemoteBranches.TryGetValue($"origin/{branch.Name}", out GitBranch? counterpart);
            counterpart ??= remoteBranchesByLocalName.GetValueOrDefault(branch.Name);
            return counterpart is null ? branch : branch with { RemoteName = counterpart.Name };
        }).ToArray();
    }

    public async Task<GitBranchDetails> GetBranchDetailsAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(branchName);

        IReadOnlyList<GitBranch> branches = await GetBranchesAsync(repositoryPath, cancellationToken)
            .ConfigureAwait(false);
        GitBranch? selected = branches.FirstOrDefault(branch =>
            branch.Name.Equals(branchName, StringComparison.Ordinal));

        string lastAuthor = selected?.TipAuthorName ?? "Unknown";
        DateTimeOffset? lastUpdatedAt = selected?.TipAuthoredAt;
        string lastSubject = selected?.TipSubject ?? string.Empty;
        string? comparisonBase = SelectBranchComparisonBase(branchName, selected, branches);
        if (comparisonBase is null)
        {
            return new GitBranchDetails(
                branchName,
                null,
                0,
                null,
                null,
                lastAuthor,
                lastUpdatedAt,
                lastSubject);
        }

        string countOutput = await RunGitAsync(
                repositoryPath,
                ["rev-list", "--count", $"{comparisonBase}..{branchName}"],
                cancellationToken)
            .ConfigureAwait(false);
        int uniqueCommitCount = int.TryParse(
            countOutput.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsedCount)
            ? parsedCount
            : 0;

        string? creatorName = null;
        DateTimeOffset? createdAt = null;
        if (uniqueCommitCount > 0)
        {
            string oldestUniqueHash = (await RunGitAsync(
                    repositoryPath,
                    ["rev-list", $"--skip={uniqueCommitCount - 1}", "--max-count=1", $"{comparisonBase}..{branchName}"],
                    cancellationToken)
                .ConfigureAwait(false)).Trim();
            if (!string.IsNullOrWhiteSpace(oldestUniqueHash))
            {
                string creatorOutput = await RunGitAsync(
                        repositoryPath,
                        ["show", "-s", "--date=iso-strict", "--format=%an%x00%aI", oldestUniqueHash],
                        cancellationToken)
                    .ConfigureAwait(false);
                string[] creatorFields = creatorOutput.Trim().Split('\0');
                if (creatorFields.Length >= 2)
                {
                    creatorName = string.IsNullOrWhiteSpace(creatorFields[0]) ? null : creatorFields[0];
                    createdAt = DateTimeOffset.TryParse(
                        creatorFields[1],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset parsedCreatedAt)
                        ? parsedCreatedAt
                        : null;
                }
            }
        }

        return new GitBranchDetails(
            branchName,
            comparisonBase,
            uniqueCommitCount,
            creatorName,
            createdAt,
            lastAuthor,
            lastUpdatedAt,
            lastSubject);
    }

    public async Task<IReadOnlyList<GitRevisionFile>> GetRevisionFilesAsync(
        string repositoryPath,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(revision);
        string output = await RunGitAsync(
                repositoryPath,
                ["ls-tree", "-r", "-z", "-l", "--full-tree", revision],
                cancellationToken)
            .ConfigureAwait(false);

        List<GitRevisionFile> files = [];
        foreach (string item in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = item.IndexOf('\t');
            if (tab <= 0 || tab >= item.Length - 1) continue;
            string metadata = item[..tab];
            string path = item[(tab + 1)..];
            string[] fields = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4) continue;
            long? size = long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedSize)
                ? parsedSize
                : null;
            files.Add(new GitRevisionFile(path, fields[2], fields[1], size, fields[0]));
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<string> FetchRemoteBranchForInspectionAsync(
        string repositoryPath,
        string remoteName,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(remoteName);
        ValidateReferenceName(branchName);
        string normalizedBranch = branchName.StartsWith(remoteName + "/", StringComparison.Ordinal)
            ? branchName[(remoteName.Length + 1)..]
            : branchName;
        ValidateReferenceName(normalizedBranch);
        string inspectionReference = $"refs/cyrevision/inspect/{remoteName}/{normalizedBranch}";
        await RunGitWithoutOutputAsync(
                repositoryPath,
                ["fetch", "--no-tags", remoteName,
                    $"+refs/heads/{normalizedBranch}:{inspectionReference}"],
                cancellationToken)
            .ConfigureAwait(false);
        return inspectionReference;
    }

    public Task DeleteInspectionReferenceAsync(
        string repositoryPath,
        string inspectionReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inspectionReference);
        const string prefix = "refs/cyrevision/inspect/";
        if (!inspectionReference.StartsWith(prefix, StringComparison.Ordinal) ||
            inspectionReference.Length <= prefix.Length)
        {
            throw new ArgumentException(
                "Only private CyRevision inspection references can be removed.",
                nameof(inspectionReference));
        }

        ValidateReferenceName(inspectionReference);
        return RunGitWithoutOutputAsync(
            repositoryPath,
            ["update-ref", "-d", inspectionReference],
            cancellationToken);
    }

    private static string? SelectBranchComparisonBase(
        string branchName,
        GitBranch? selected,
        IReadOnlyList<GitBranch> branches)
    {
        HashSet<string> available = branches
            .Select(branch => branch.Name)
            .ToHashSet(StringComparer.Ordinal);
        string localName = selected?.IsRemote == true && branchName.Contains('/')
            ? branchName[(branchName.IndexOf('/') + 1)..]
            : branchName;
        string? remotePrefix = selected?.IsRemote == true && branchName.Contains('/')
            ? branchName[..branchName.IndexOf('/')]
            : selected?.RemoteName?.Split('/', 2)[0];

        string? prefixParent = branches
            .Where(candidate => !candidate.Name.Equals(branchName, StringComparison.Ordinal))
            .Where(candidate =>
            {
                string candidateLocalName = candidate.IsRemote && candidate.Name.Contains('/')
                    ? candidate.Name[(candidate.Name.IndexOf('/') + 1)..]
                    : candidate.Name;
                return localName.StartsWith(candidateLocalName + "-", StringComparison.OrdinalIgnoreCase) ||
                       localName.StartsWith(candidateLocalName + "/", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(candidate => candidate.Name.Length)
            .ThenBy(candidate => candidate.IsRemote)
            .Select(candidate => candidate.Name)
            .FirstOrDefault();
        if (prefixParent is not null) return prefixParent;

        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(remotePrefix))
        {
            candidates.AddRange([$"{remotePrefix}/main", $"{remotePrefix}/master", $"{remotePrefix}/dev"]);
        }
        candidates.AddRange(["origin/main", "main", "origin/master", "master", "origin/dev", "dev"]);

        if (selected is { IsRemote: false } && !string.IsNullOrWhiteSpace(selected.RemoteName) &&
            (localName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
             localName.Equals("master", StringComparison.OrdinalIgnoreCase) ||
             localName.Equals("dev", StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Insert(0, selected.RemoteName);
        }

        GitBranch? current = branches.FirstOrDefault(branch => branch.IsCurrent);
        if (current is not null) candidates.Add(current.Name);

        return candidates.FirstOrDefault(candidate =>
            !candidate.Equals(branchName, StringComparison.Ordinal) && available.Contains(candidate));
    }

    private static (int Ahead, int Behind) ParseTrackingCounts(string tracking)
    {
        int ahead = 0;
        int behind = 0;
        foreach (string part in tracking.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                continue;
            }

            if (fields[0].Equals("ahead", StringComparison.OrdinalIgnoreCase)) ahead = count;
            if (fields[0].Equals("behind", StringComparison.OrdinalIgnoreCase)) behind = count;
        }
        return (ahead, behind);
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

    public async Task<GitHistoricalWorktreeResult> CreateHistoricalWorktreeAsync(
        string repositoryPath,
        string commitHash,
        string? branchName = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(commitHash);
        if (!string.IsNullOrWhiteSpace(branchName)) ValidateReferenceName(branchName);
        string repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        string root = GetManagedWorktreeRoot(repository);
        Directory.CreateDirectory(root);
        string label = string.IsNullOrWhiteSpace(branchName) ? "detached" : SanitizeWorktreeName(branchName);
        string shortHash = commitHash.Length > 9 ? commitHash[..9] : commitHash;
        string worktree = Path.Combine(root, $"{label}-{shortHash}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        List<string> arguments = ["worktree", "add", "--quiet"];
        if (string.IsNullOrWhiteSpace(branchName)) arguments.Add("--detach");
        else { arguments.Add("-b"); arguments.Add(branchName.Trim()); }
        arguments.Add(worktree);
        arguments.Add(commitHash);
        ProcessResult result = await RunGitResultAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "Unable to create the historical worktree.");
        string commonGitDirectory = (await RunGitAsync(
            repository,
            ["rev-parse", "--absolute-git-dir"],
            cancellationToken).ConfigureAwait(false)).Trim();
        await File.WriteAllTextAsync(
            GetManagedWorktreeMarkerPath(worktree),
            JsonSerializer.Serialize(new ManagedWorktreeMarker(commonGitDirectory, DateTimeOffset.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        return new GitHistoricalWorktreeResult(
            true,
            repository,
            worktree,
            commitHash,
            branchName?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(branchName)
                ? $"Detached historical worktree created at {worktree}."
                : $"Branch {branchName.Trim()} created in isolated worktree {worktree}.");
    }

    public async Task<IReadOnlyList<GitHistoricalWorktree>> GetHistoricalWorktreesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        ProcessResult result = await RunGitResultAsync(repository, ["worktree", "list", "--porcelain"], cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "Unable to list Git worktrees.");
        string managedRoot = GetManagedWorktreeRoot(repository);
        string commonGitDirectory = (await RunGitAsync(
            repository,
            ["rev-parse", "--absolute-git-dir"],
            cancellationToken).ConfigureAwait(false)).Trim();
        List<GitHistoricalWorktree> worktrees = [];
        foreach (string block in result.StandardOutput.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string path = string.Empty;
            string head = string.Empty;
            string branch = string.Empty;
            bool detached = false;
            bool locked = false;
            bool prunable = false;
            foreach (string line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("worktree ", StringComparison.Ordinal)) path = line[9..].Trim();
                else if (line.StartsWith("HEAD ", StringComparison.Ordinal)) head = line[5..].Trim();
                else if (line.StartsWith("branch ", StringComparison.Ordinal)) branch = line[7..].Trim().Replace("refs/heads/", string.Empty, StringComparison.Ordinal);
                else if (line.Equals("detached", StringComparison.Ordinal)) detached = true;
                else if (line.StartsWith("locked", StringComparison.Ordinal)) locked = true;
                else if (line.StartsWith("prunable", StringComparison.Ordinal)) prunable = true;
            }
            if (string.IsNullOrWhiteSpace(path)) continue;
            string full = Path.GetFullPath(path);
            bool managed = IsWithinDirectory(full, managedRoot) ||
                           IsManagedWorktreeMarkerValid(full, commonGitDirectory);
            DateTimeOffset? created = Directory.Exists(full) ? new DateTimeOffset(Directory.GetCreationTimeUtc(full), TimeSpan.Zero) : null;
            worktrees.Add(new GitHistoricalWorktree(full, head, branch, detached, locked, prunable, managed, created));
        }
        return worktrees.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task RemoveHistoricalWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        string repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        string worktree = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreePath));
        string managedRoot = GetManagedWorktreeRoot(repository);
        string commonGitDirectory = (await RunGitAsync(
            repository,
            ["rev-parse", "--absolute-git-dir"],
            cancellationToken).ConfigureAwait(false)).Trim();
        bool markedAsManaged = IsManagedWorktreeMarkerValid(worktree, commonGitDirectory);
        if ((!IsWithinDirectory(worktree, managedRoot) && !markedAsManaged) ||
            string.Equals(worktree, managedRoot, PathComparison))
            throw new GitOperationException("CyRevision only removes historical worktrees created inside its managed worktree directory.");
        List<string> arguments = ["worktree", "remove"];
        if (force) arguments.Add("--force");
        arguments.Add(worktree);
        ProcessResult result = await RunGitResultAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "Unable to remove the historical worktree.");
        string markerPath = GetManagedWorktreeMarkerPath(worktree);
        if (File.Exists(markerPath)) File.Delete(markerPath);
        await RunGitWithoutOutputAsync(repository, ["worktree", "prune"], cancellationToken).ConfigureAwait(false);
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
        List<string> arguments = ["diff", "--no-ext-diff", "--no-color", "--no-renames"];
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

    public async Task<GitRevisionFileExportResult> MaterializeFileFromRevisionAsync(
        string repositoryPath,
        string relativePath,
        string revision,
        string destinationPath,
        string? lfsRemoteName = null,
        bool fetchMissingLfsObject = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateReferenceName(revision);
        string fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);

        string pointerPath = fullDestination + ".cyrevision-pointer-" + Guid.NewGuid().ToString("N");
        try
        {
            await ExportFileFromRevisionAsync(
                    repositoryPath,
                    relativePath,
                    revision,
                    pointerPath,
                    cancellationToken)
                .ConfigureAwait(false);

            LfsPointerInfo? pointer = await TryReadLfsPointerAtRevisionAsync(
                    repositoryPath,
                    revision,
                    relativePath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (pointer is null)
            {
                File.Move(pointerPath, fullDestination, overwrite: true);
                return new GitRevisionFileExportResult(
                    fullDestination,
                    false,
                    false,
                    new FileInfo(fullDestination).Length);
            }

            LfsStoragePaths storage = await LfsStoragePathResolver.ResolveAsync(
                    _processRunner,
                    _gitExecutable,
                    repositoryPath,
                    cancellationToken)
                .ConfigureAwait(false);
            string objectPath = storage.GetObjectPath(pointer.OidSha256);
            bool downloaded = false;
            if (!File.Exists(objectPath) && fetchMissingLfsObject)
            {
                string remote = string.IsNullOrWhiteSpace(lfsRemoteName) ? "origin" : lfsRemoteName.Trim();
                ValidateReferenceName(remote);
                ProcessResult fetch = await RunGitResultAsync(
                        repositoryPath,
                        ["lfs", "fetch", $"--include={relativePath.Replace('\\', '/')}", "--exclude=", remote, revision],
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureSuccess(fetch, $"Unable to retrieve the selected LFS object from {remote}.");
                downloaded = true;
            }

            if (!File.Exists(objectPath))
            {
                throw new GitOperationException(
                    "The selected revision contains a Git LFS pointer, but its object is not stored locally. " +
                    "Enable targeted remote retrieval or obtain the object from an authorized peer/archive.");
            }

            await using FileStream source = new(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using FileStream destination = new(fullDestination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return new GitRevisionFileExportResult(fullDestination, true, downloaded, pointer.Size);
        }
        finally
        {
            if (File.Exists(pointerPath)) File.Delete(pointerPath);
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

    public async Task<string?> GetRemoteUrlAsync(
        string repositoryPath,
        string remoteName = "origin",
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(remoteName);
        ProcessResult result = await RunGitResultAsync(
            repositoryPath,
            ["remote", "get-url", remoteName],
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.Trim()
            : null;
    }

    public Task FetchAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        RunGitWithoutOutputAsync(repositoryPath, ["fetch", "--all", "--prune"], cancellationToken);

    public Task FetchReferenceAsync(
        string repositoryPath,
        string remoteName,
        string referenceSpec,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(remoteName);
        ValidateReferenceName(referenceSpec);
        return RunGitWithoutOutputAsync(
            repositoryPath,
            ["fetch", "--no-tags", remoteName, referenceSpec],
            cancellationToken);
    }

    public Task FastForwardAsync(
        string repositoryPath,
        string reference,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(reference);
        return RunGitWithoutOutputAsync(repositoryPath, ["merge", "--ff-only", reference], cancellationToken);
    }

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

    public async Task<IReadOnlyList<LfsFileLock>> GetLfsLocksAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ProcessResult verified = await RunGitResultAsync(
                repositoryPath,
                ["lfs", "locks", "--verify", "--json", "--limit=1000"],
                cancellationToken)
            .ConfigureAwait(false);
        if (verified.Succeeded)
        {
            return ParseLfsLocksJson(verified.StandardOutput);
        }

        ProcessResult cached = await RunGitResultAsync(
                repositoryPath,
                ["lfs", "locks", "--cached", "--json", "--limit=1000"],
                cancellationToken)
            .ConfigureAwait(false);
        ProcessResult local = await RunGitResultAsync(
                repositoryPath,
                ["lfs", "locks", "--local", "--json", "--limit=1000"],
                cancellationToken)
            .ConfigureAwait(false);
        if (!cached.Succeeded && !local.Succeeded)
        {
            EnsureSuccess(verified, "Unable to read Git LFS locks from the remote.");
        }

        Dictionary<string, LfsFileLock> locks = new(StringComparer.Ordinal);
        if (cached.Succeeded)
        {
            foreach (LfsFileLock item in ParseLfsLocksJson(cached.StandardOutput, defaultOurs: false, isCached: true))
            {
                locks[item.Id] = item;
            }
        }
        if (local.Succeeded)
        {
            foreach (LfsFileLock item in ParseLfsLocksJson(local.StandardOutput, defaultOurs: true, isCached: true))
            {
                locks[item.Id] = item;
            }
        }

        return locks.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task UnlockLfsFileAsync(
        string repositoryPath,
        string lockId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);
        List<string> arguments = ["lfs", "unlock", "--json"];
        if (force)
        {
            arguments.Add("--force");
        }
        arguments.Add("--id=" + lockId.Trim());
        ProcessResult result = await RunGitResultAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, force
            ? "Unable to force-unlock the Git LFS file."
            : "Unable to unlock the Git LFS file.");
    }

    public static IReadOnlyList<LfsFileLock> ParseLfsLocksJson(
        string json,
        bool defaultOurs = false,
        bool isCached = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonObject root = JsonNode.Parse(json) as JsonObject
                          ?? throw new JsonException("Git LFS returned an invalid lock response.");
        Dictionary<string, LfsFileLock> result = new(StringComparer.Ordinal);
        AddLocks(root["ours"] as JsonArray, isOurs: true);
        AddLocks(root["theirs"] as JsonArray, isOurs: false);
        AddLocks(root["locks"] as JsonArray, defaultOurs);
        return result.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();

        void AddLocks(JsonArray? array, bool isOurs)
        {
            if (array is null)
            {
                return;
            }

            foreach (JsonNode? node in array)
            {
                JsonObject? item = node as JsonObject;
                string? id = item?["id"]?.GetValue<string>();
                string? path = item?["path"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string owner = item?["owner"]?["name"]?.GetValue<string>() ?? "Unknown user";
                DateTimeOffset? lockedAt = DateTimeOffset.TryParse(
                    item?["locked_at"]?.GetValue<string>(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed)
                    ? parsed
                    : null;
                result[id] = new LfsFileLock(id, path, owner, lockedAt, isOurs, isCached);
            }
        }
    }

    public async Task<IReadOnlyList<LfsTrackedFile>> GetLfsTrackedFilesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                ["lfs", "ls-files", "--long", "--size"],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return [];
        }

        LfsStoragePaths lfsStorage = await LfsStoragePathResolver.ResolveAsync(
            _processRunner, _gitExecutable, repositoryPath, cancellationToken).ConfigureAwait(false);
        List<LfsTrackedFile> files = [];
        foreach (string line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = LfsListLineRegex().Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            string path = match.Groups["path"].Value.Trim();
            string oid = match.Groups["oid"].Value.ToLowerInvariant();
            string objectPath = lfsStorage.GetObjectPath(oid);
            long size = File.Exists(objectPath)
                ? new FileInfo(objectPath).Length
                : ParseHumanSize(match.Groups["size"].Value, match.Groups["unit"].Value);
            LfsPointerInfo pointer = new(oid, size);
            files.Add(new LfsTrackedFile(
                path,
                ClassifyFile(path),
                pointer,
                File.Exists(objectPath),
                File.Exists(objectPath) ? objectPath : null));
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static long ParseHumanSize(string value, string unit)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return 0;
        }

        double multiplier = unit.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024d,
            "GB" => 1024d * 1024d * 1024d,
            "TB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };
        return checked((long)Math.Round(parsed * multiplier));
    }

    public async Task<IReadOnlyList<LfsFileVersion>> GetLfsFileVersionsAsync(
        string repositoryPath,
        string relativePath,
        int maximumCount = 200,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitFileRevision> history = await GetFileHistoryAsync(
                repositoryPath,
                relativePath,
                maximumCount,
                cancellationToken)
            .ConfigureAwait(false);
        LfsStoragePaths lfsStorage = await LfsStoragePathResolver.ResolveAsync(
            _processRunner, _gitExecutable, repositoryPath, cancellationToken).ConfigureAwait(false);
        List<LfsFileVersion> versions = [];
        HashSet<string> seenObjects = new(StringComparer.OrdinalIgnoreCase);
        foreach (GitFileRevision entry in history)
        {
            LfsPointerInfo? pointer = entry.LfsPointer ?? await TryReadLfsPointerAtRevisionAsync(
                    repositoryPath,
                    entry.Revision.Hash,
                    relativePath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (pointer is null || !seenObjects.Add(pointer.OidSha256))
            {
                continue;
            }

            string objectPath = lfsStorage.GetObjectPath(pointer.OidSha256);
            bool isAvailable = File.Exists(objectPath);
            versions.Add(new LfsFileVersion(
                relativePath,
                entry.Revision,
                pointer,
                isAvailable,
                isAvailable ? objectPath : null,
                versions.Count == 0));
        }

        return versions;
    }

    public async Task ExportLfsFileVersionAsync(
        string repositoryPath,
        LfsFileVersion version,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string? sourcePath = version.LocalObjectPath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            LfsStoragePaths lfsStorage = await LfsStoragePathResolver.ResolveAsync(
                _processRunner, _gitExecutable, repositoryPath, cancellationToken).ConfigureAwait(false);
            sourcePath = lfsStorage.GetObjectPath(version.Pointer.OidSha256);
        }

        if (!File.Exists(sourcePath))
        {
            throw new GitOperationException(
                "This LFS object is not stored locally. Retrieve it from an authorized peer or archive before exporting it.");
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using FileStream destination = new(fullDestination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreLfsFileVersionAsync(
        string repositoryPath,
        LfsFileVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        string root = Path.GetFullPath(repositoryPath);
        string destination = Path.GetFullPath(Path.Combine(root, version.Path));
        string relative = Path.GetRelativePath(root, destination);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new GitOperationException("The LFS path is outside the repository.");
        }

        await ExportLfsFileVersionAsync(repositoryPath, version, destination, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<string>> GetLfsFileNamesForPathsAsync(
        string repositoryPath,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        HashSet<string> lfsFiles = new(StringComparer.OrdinalIgnoreCase);
        string[] candidates = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            return lfsFiles;
        }

        string standardInput = string.Join('\0', candidates) + '\0';
        ProcessResult result = await RunGitResultWithInputAsync(
                repositoryPath,
                ["check-attr", "-z", "--stdin", "filter"],
                standardInput,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return lfsFiles;
        }

        string[] fields = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 2 < fields.Length; index += 3)
        {
            if (string.Equals(fields[index + 1], "filter", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fields[index + 2], "lfs", StringComparison.OrdinalIgnoreCase))
            {
                lfsFiles.Add(fields[index]);
            }
        }
        return lfsFiles;
    }

    private async Task<IReadOnlyList<GitCommitFileChange>> GetRevisionChangesAsync(
        string repositoryPath,
        string firstRevision,
        string? secondRevision,
        CancellationToken cancellationToken,
        string? relativePath = null)
    {
        List<string> statusArguments = ["-c", "core.quotepath=false"];
        List<string> statArguments = ["-c", "core.quotepath=false"];
        if (secondRevision is null)
        {
            statusArguments.AddRange(["diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-M", firstRevision]);
            statArguments.AddRange(["diff-tree", "--root", "--no-commit-id", "--numstat", "-r", "-M", firstRevision]);
        }
        else
        {
            statusArguments.AddRange(["diff", "--name-status", "-M", firstRevision, secondRevision]);
            statArguments.AddRange(["diff", "--numstat", "-M", firstRevision, secondRevision]);
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            statusArguments.AddRange(["--", relativePath]);
            statArguments.AddRange(["--", relativePath]);
        }

        Task<ProcessResult> statusTask = RunGitResultAsync(repositoryPath, statusArguments, cancellationToken);
        Task<ProcessResult> statTask = RunGitResultAsync(repositoryPath, statArguments, cancellationToken);
        await Task.WhenAll(statusTask, statTask).ConfigureAwait(false);
        ProcessResult statusResult = await statusTask.ConfigureAwait(false);
        ProcessResult statResult = await statTask.ConfigureAwait(false);
        EnsureSuccess(statusResult, "Unable to read changed file states.");
        EnsureSuccess(statResult, "Unable to read changed file statistics.");

        Dictionary<string, MutableRevisionChange> changes = new(StringComparer.Ordinal);
        foreach (string line in statusResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 2)
            {
                continue;
            }

            string code = fields[0];
            string path = NormalizeHistoryPath(fields[^1]);
            string? originalPath = code.StartsWith('R') && fields.Length >= 3
                ? NormalizeHistoryPath(fields[1])
                : null;
            changes[path] = new MutableRevisionChange(
                path,
                code[0] switch
                {
                    'A' => GitChangeKind.Added,
                    'D' => GitChangeKind.Deleted,
                    'R' => GitChangeKind.Renamed,
                    'U' => GitChangeKind.Conflicted,
                    _ => GitChangeKind.Modified
                },
                originalPath);
        }

        foreach (string line in statResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t', 3);
            if (fields.Length != 3)
            {
                continue;
            }

            string path = NormalizeHistoryPath(fields[2]);
            if (!changes.TryGetValue(path, out MutableRevisionChange? change))
            {
                change = new MutableRevisionChange(path, GitChangeKind.Modified, null);
                changes[path] = change;
            }

            if (fields[0] == "-" || fields[1] == "-")
            {
                change.IsBinary = true;
            }
            else
            {
                change.AddedLines = long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long added)
                    ? added
                    : 0;
                change.DeletedLines = long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long deleted)
                    ? deleted
                    : 0;
            }
        }

        string contentRevision = secondRevision ?? firstRevision;
        HashSet<string> lfsPaths = await GetLfsFileNamesForPathsAsync(
                repositoryPath,
                changes.Values
                    .Where(change => change.Kind != GitChangeKind.Deleted)
                    .Select(change => change.Path),
                cancellationToken)
            .ConfigureAwait(false);
        List<GitCommitFileChange> result = [];
        foreach (MutableRevisionChange change in changes.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            LfsPointerInfo? pointer = change.Kind == GitChangeKind.Deleted || !lfsPaths.Contains(change.Path)
                ? null
                : await TryReadLfsPointerAtRevisionAsync(
                        repositoryPath,
                        contentRevision,
                        change.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
            result.Add(new GitCommitFileChange(
                change.Path,
                change.Kind,
                change.AddedLines,
                change.DeletedLines,
                change.IsBinary,
                change.OriginalPath,
                pointer));
        }

        return result;
    }

    private async Task<LfsPointerInfo?> TryReadLfsPointerAtRevisionAsync(
        string repositoryPath,
        string revision,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string gitPath = relativePath.Replace('\\', '/').TrimStart('/');
        ProcessResult result = await RunGitResultAsync(
                repositoryPath,
                ["show", $"{revision}:{gitPath}"],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded || result.StandardOutput.Length > 1024)
        {
            return null;
        }

        Match match = LfsPointerRegex().Match(result.StandardOutput.Replace("\r", string.Empty, StringComparison.Ordinal));
        if (!match.Success ||
            !long.TryParse(match.Groups["size"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long size))
        {
            return null;
        }

        return new LfsPointerInfo(match.Groups["oid"].Value.ToLowerInvariant(), size);
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

    private Task<ProcessResult> RunGitResultWithInputAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        string standardInput,
        CancellationToken cancellationToken) =>
        _processRunner.RunAsync(
            _gitExecutable,
            arguments,
            Path.GetFullPath(workingDirectory),
            standardInput,
            cancellationToken);

    private async Task RunGitPathspecCommandAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitPathspecResultAsync(
                workingDirectory,
                arguments,
                paths,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "Git operation failed.");
    }

    private Task<ProcessResult> RunGitPathspecResultAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        EnsurePaths(paths);
        List<string> pathspecArguments = [.. arguments, "--pathspec-from-file=-", "--pathspec-file-nul"];
        string standardInput = string.Join('\0', paths) + '\0';
        return RunGitResultWithInputAsync(
            workingDirectory,
            pathspecArguments,
            standardInput,
            cancellationToken);
    }

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

    private static string NormalizeHistoryPath(string value)
    {
        string path = value.Trim().Trim('"').Replace('\\', '/');
        int arrow = path.IndexOf(" => ", StringComparison.Ordinal);
        if (arrow < 0)
        {
            return path;
        }

        int openBrace = path.LastIndexOf('{', arrow);
        int closeBrace = path.IndexOf('}', arrow);
        if (openBrace >= 0 && closeBrace > arrow)
        {
            string replacement = path[(arrow + 4)..closeBrace];
            return path[..openBrace] + replacement + path[(closeBrace + 1)..];
        }

        return path[(arrow + 4)..];
    }

    private static GitFileKind ClassifyFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".cs" or ".cpp" or ".c" or ".h" or ".hpp" or ".inl" or ".py" or ".js" or ".ts" or
                ".tsx" or ".jsx" or ".java" or ".kt" or ".rs" or ".go" or ".shader" or ".usf" or ".ush" or
                ".axaml" or ".html" or ".css" or ".scss" or ".ps1" or ".sh" => GitFileKind.Code,
            ".uasset" or ".umap" => GitFileKind.UnrealAsset,
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".exr" or ".hdr" or ".dds" or ".tif" or ".tiff" => GitFileKind.Texture,
            ".fbx" or ".obj" or ".gltf" or ".glb" or ".usd" or ".usda" or ".usdc" or ".blend" => GitFileKind.Model,
            ".wav" or ".mp3" or ".ogg" or ".flac" or ".aiff" => GitFileKind.Audio,
            ".md" or ".txt" or ".pdf" or ".doc" or ".docx" or ".rtf" => GitFileKind.Document,
            ".json" or ".xml" or ".yaml" or ".yml" or ".ini" or ".toml" or ".config" or ".csproj" or ".sln" => GitFileKind.Configuration,
            _ => GitFileKind.Other
        };
    }

    private sealed class MutableFileActivity(string path, GitFileKind kind)
    {
        public string Path { get; } = path;
        public GitFileKind Kind { get; } = kind;
        public int ChangeCount { get; set; }
        public long AddedLines { get; set; }
        public long DeletedLines { get; set; }
        public int BinaryChangeCount { get; set; }
        public DateTimeOffset LastChangedAt { get; set; }

        public GitFileActivity ToRecord() => new(
            Path,
            Kind,
            ChangeCount,
            AddedLines,
            DeletedLines,
            BinaryChangeCount,
            LastChangedAt);
    }

    private sealed class MutableRevisionChange(string path, GitChangeKind kind, string? originalPath)
    {
        public string Path { get; } = path;
        public GitChangeKind Kind { get; } = kind;
        public string? OriginalPath { get; } = originalPath;
        public long? AddedLines { get; set; }
        public long? DeletedLines { get; set; }
        public bool IsBinary { get; set; }
    }

    private sealed class MutableContributorActivity(string authorName, string authorEmail)
    {
        public string AuthorName { get; } = authorName;
        public string AuthorEmail { get; } = authorEmail;
        public int CommitCount { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        public long AddedLines { get; set; }
        public long DeletedLines { get; set; }
        public int BinaryChanges { get; set; }
        public DateTimeOffset LastActiveAt { get; set; }

        public GitContributorActivity ToRecord() => new(
            AuthorName,
            AuthorEmail,
            CommitCount,
            Files.Count,
            AddedLines,
            DeletedLines,
            BinaryChanges,
            LastActiveAt);
    }

    private sealed class MutableDailyActivity(DateOnly day)
    {
        public DateOnly Day { get; } = day;
        public int CommitCount { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        public long AddedLines { get; set; }
        public long DeletedLines { get; set; }

        public GitDailyActivity ToRecord() => new(Day, CommitCount, Files.Count, AddedLines, DeletedLines);
    }

    private static void EnsurePaths(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0 || paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one valid path is required.", nameof(paths));
        }
    }

    private static void DeleteWorkingTreeFile(string repositoryRoot, string relativePath)
    {
        string root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            throw new GitOperationException($"Refusing to remove a path outside the repository: {relativePath}");
        }

        string repositoryRelativePath = Path.GetRelativePath(root, candidate);
        string firstSegment = repositoryRelativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (string.Equals(firstSegment, ".git", comparison))
        {
            throw new GitOperationException("Refusing to remove Git repository metadata.");
        }

        if (File.Exists(candidate))
        {
            File.Delete(candidate);
            RemoveEmptyParentDirectories(root, Path.GetDirectoryName(candidate));
        }
        else if (Directory.Exists(candidate))
        {
            bool isLink = File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint);
            Directory.Delete(candidate, recursive: !isLink);
            RemoveEmptyParentDirectories(root, Path.GetDirectoryName(candidate));
        }
    }

    private static void DeleteUntrackedPath(string repositoryRoot, string relativePath)
    {
        string root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new GitOperationException($"Refusing to remove a path outside the repository: {relativePath}");
        }

        if (File.Exists(candidate)) File.Delete(candidate);
        else if (Directory.Exists(candidate)) Directory.Delete(candidate, recursive: true);
    }

    private static void RemoveEmptyParentDirectories(string repositoryRoot, string? directory)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (!string.IsNullOrWhiteSpace(directory) &&
               !string.Equals(directory, repositoryRoot, comparison) &&
               Directory.Exists(directory) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
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

    private static string GetManagedWorktreeRoot(string repositoryPath)
    {
        string repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        string parent = Path.GetDirectoryName(repository)
                        ?? throw new GitOperationException("The repository has no parent directory for isolated worktrees.");
        return Path.Combine(parent, ".cyrevision-worktrees", SanitizeWorktreeName(Path.GetFileName(repository)));
    }

    private static string GetManagedWorktreeMarkerPath(string worktreePath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreePath)) + ".cyrevision-worktree.json";

    private static bool IsManagedWorktreeMarkerValid(string worktreePath, string commonGitDirectory)
    {
        try
        {
            string markerPath = GetManagedWorktreeMarkerPath(worktreePath);
            if (!File.Exists(markerPath)) return false;
            ManagedWorktreeMarker? marker = JsonSerializer.Deserialize<ManagedWorktreeMarker>(File.ReadAllText(markerPath));
            return marker is not null && string.Equals(
                NormalizeComparablePath(marker.CommonGitDirectory),
                NormalizeComparablePath(commonGitDirectory),
                PathComparison);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    private static string SanitizeWorktreeName(string value)
    {
        string normalized = value.Replace('/', '-').Replace('\\', '-');
        return string.Concat(normalized.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_'));
    }

    private static bool IsWithinDirectory(string candidate, string directory)
    {
        string fullCandidate = NormalizeComparablePath(candidate);
        string fullDirectory = NormalizeComparablePath(directory);
        return fullCandidate.StartsWith(fullDirectory + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string NormalizeComparablePath(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!OperatingSystem.IsWindows()) return full;

        // Git for Windows emits canonical long paths even when TEMP or the repository was
        // supplied through an 8.3 path (for example CYBERA~1). Expand both operands before
        // checking the managed worktree boundary so valid worktrees remain recognizable.
        StringBuilder expanded = new(32_768);
        uint length = GetLongPathName(full, expanded, (uint)expanded.Capacity);
        return length is > 0 and < 32_768
            ? Path.TrimEndingDirectorySeparator(expanded.ToString())
            : full;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record ManagedWorktreeMarker(string CommonGitDirectory, DateTimeOffset CreatedAt);

    [GeneratedRegex(@"(?:ahead (?<ahead>\d+))|(?:behind (?<behind>\d+))", RegexOptions.CultureInvariant)]
    private static partial Regex AheadBehindRegex();

    [GeneratedRegex(@"^(?<oid>[0-9a-fA-F]{64})\s+[-*]\s+(?<path>.+?)(?:\s+\((?<size>[0-9]+(?:\.[0-9]+)?)\s+(?<unit>[KMGT]?B)\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex LfsListLineRegex();

    [GeneratedRegex(@"^version https://git-lfs\.github\.com/spec/v1\noid sha256:(?<oid>[0-9a-fA-F]{64})\nsize (?<size>\d+)\n?$", RegexOptions.CultureInvariant)]
    private static partial Regex LfsPointerRegex();
}
