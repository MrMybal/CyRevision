using System.Globalization;
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
                ["status", "--porcelain=v1", "-z", "--branch", $"--untracked-files={untrackedFilesMode}"],
                cancellationToken)
            .ConfigureAwait(false);

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
            "--date-order",
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
        IReadOnlyList<GitCommitFileChange> files = await GetRevisionChangesAsync(
                repositoryPath,
                revision,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return new GitCommitDetails(
            parsedRevision,
            fields[6].Split(' ', StringSplitOptions.RemoveEmptyEntries),
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

    public Task<string> GetCommitDiffAsync(
        string repositoryPath,
        string revision,
        string? relativePath = null,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(revision);
        List<string> arguments = ["show", "--format=", "--no-ext-diff", "--no-color", "--no-renames", revision];
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            arguments.Add("--");
            arguments.Add(relativePath);
        }

        return RunGitAsync(repositoryPath, arguments, cancellationToken);
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
                    "--date=iso-strict", "--format=%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e",
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
        foreach (string entry in result.StandardOutput.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = entry.Trim().Split('\x1f');
            if (fields.Length != 6 ||
                !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset authoredAt))
            {
                continue;
            }

            GitRevision itemRevision = new(fields[0], fields[1], fields[2], fields[3], authoredAt, fields[5]);
            IReadOnlyList<GitCommitFileChange> changes = await GetRevisionChangesAsync(
                    repositoryPath,
                    fields[0],
                    null,
                    cancellationToken,
                    relativePath)
                .ConfigureAwait(false);
            GitCommitFileChange? change = changes.FirstOrDefault();
            if (change is null)
            {
                continue;
            }

            history.Add(new GitFileRevision(
                itemRevision,
                change.Path,
                change.Kind,
                change.AddedLines,
                change.DeletedLines,
                change.IsBinary,
                change.LfsPointer));
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

        GitBranch[] remoteBranches = branches.Where(branch => branch.IsRemote).ToArray();
        return branches.Select(branch =>
        {
            if (branch.IsRemote || !string.IsNullOrWhiteSpace(branch.RemoteName))
            {
                return branch;
            }

            GitBranch? counterpart = remoteBranches.FirstOrDefault(remote =>
                remote.Name.Equals($"origin/{branch.Name}", StringComparison.Ordinal))
                ?? remoteBranches.FirstOrDefault(remote =>
                    remote.Name.EndsWith("/" + branch.Name, StringComparison.Ordinal));
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
        foreach (string[] batch in paths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Chunk(150))
        {
            ProcessResult result = await RunGitResultAsync(
                    repositoryPath,
                    ["check-attr", "-z", "filter", "--", .. batch],
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                continue;
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
        List<GitCommitFileChange> result = [];
        foreach (MutableRevisionChange change in changes.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            LfsPointerInfo? pointer = change.Kind == GitChangeKind.Deleted
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

    private static void DeleteUntrackedPath(string repositoryRoot, string relativePath)
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

        if (File.Exists(candidate))
        {
            File.Delete(candidate);
        }
        else if (Directory.Exists(candidate))
        {
            Directory.Delete(candidate, recursive: true);
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

    [GeneratedRegex(@"^(?<oid>[0-9a-fA-F]{64})\s+[-*]\s+(?<path>.+?)(?:\s+\((?<size>[0-9]+(?:\.[0-9]+)?)\s+(?<unit>[KMGT]?B)\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex LfsListLineRegex();

    [GeneratedRegex(@"^version https://git-lfs\.github\.com/spec/v1\noid sha256:(?<oid>[0-9a-fA-F]{64})\nsize (?<size>\d+)\n?$", RegexOptions.CultureInvariant)]
    private static partial Regex LfsPointerRegex();
}
