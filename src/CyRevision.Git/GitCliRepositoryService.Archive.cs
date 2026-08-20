using System.IO.Compression;
using System.Text.Json;

namespace CyRevision.Git;

public sealed record GitArchiveProfile(
    string Id,
    string Name,
    string Description,
    int ArchiveAfterDays,
    int MinimumRecentBranches,
    bool RemoveAfterVerifiedArchive = false)
{
    public static IReadOnlyList<GitArchiveProfile> BuiltIn { get; } =
    [
        new("safe", "Safe", "Archive branches untouched for 180 days; keep at least 10 recent branches. Never delete automatically.", 180, 10),
        new("balanced", "Balanced", "Archive branches untouched for 90 days; keep at least 5 recent branches. Never delete automatically.", 90, 5),
        new("space", "Space saver", "Archive branches untouched for 30 days; keep at least 2 recent branches. Deletion still requires explicit opt-in.", 30, 2)
    ];
}

public sealed record GitArchiveCandidate(
    string Branch,
    string Tip,
    DateTimeOffset LastCommitAt,
    int AgeDays,
    bool HasRemoteCopy = false,
    string? RemoteReference = null)
{
    public string ShortTip => Tip.Length > 10 ? Tip[..10] : Tip;
}

public sealed record GitArchivedBranch(
    string Branch,
    string Tip,
    DateTimeOffset LastCommitAt,
    DateTimeOffset ArchivedAt,
    string ProfileId,
    bool SourceBranchRemoved,
    string ArchivePath);

public sealed partial class GitCliRepositoryService
{
    private static readonly JsonSerializerOptions ArchiveJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<GitArchiveCandidate>> GetArchiveCandidatesAsync(
        string repositoryPath,
        GitArchiveProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string current = (await RunGitAsync(repositoryPath, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false)).Trim();
        string output = await RunGitAsync(
            repositoryPath,
            ["for-each-ref", "--sort=-committerdate", "--format=%(refname:short)%00%(objectname)%00%(committerdate:iso-strict)%00%(upstream:short)", "refs/heads"],
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset threshold = DateTimeOffset.UtcNow.AddDays(-profile.ArchiveAfterDays);
        GitArchiveCandidate[] candidates = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseArchiveCandidate)
            .Where(item => item is not null)
            .Cast<GitArchiveCandidate>()
            .Where(item => !item.Branch.Equals(current, StringComparison.OrdinalIgnoreCase))
            .Skip(profile.MinimumRecentBranches)
            .Where(item => item.LastCommitAt <= threshold)
            .ToArray();
        List<GitArchiveCandidate> enriched = [];
        foreach (GitArchiveCandidate candidate in candidates)
        {
            string remoteReference = string.IsNullOrWhiteSpace(candidate.RemoteReference)
                ? $"origin/{candidate.Branch}"
                : candidate.RemoteReference;
            ProcessResult remote = await RunGitResultAsync(
                repositoryPath,
                ["rev-parse", "--verify", remoteReference],
                cancellationToken).ConfigureAwait(false);
            string? remoteTip = remote.Succeeded ? remote.StandardOutput.Trim() : null;
            enriched.Add(candidate with
            {
                HasRemoteCopy = remoteTip is not null && remoteTip.Equals(candidate.Tip, StringComparison.OrdinalIgnoreCase),
                RemoteReference = remoteTip is null ? null : remoteReference
            });
        }
        return enriched;
    }

    public async Task<GitArchivedBranch> ArchiveBranchAsync(
        string repositoryPath,
        string branch,
        string archiveDirectory,
        GitArchiveProfile profile,
        bool removeAfterVerifiedArchive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveDirectory);
        string current = (await RunGitAsync(repositoryPath, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false)).Trim();
        if (branch.Equals(current, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current branch cannot be archived.");
        string tip = (await RunGitAsync(repositoryPath, ["rev-parse", $"refs/heads/{branch}"], cancellationToken).ConfigureAwait(false)).Trim();
        DateTimeOffset lastCommit = DateTimeOffset.Parse((await RunGitAsync(
            repositoryPath, ["show", "-s", "--format=%cI", tip], cancellationToken).ConfigureAwait(false)).Trim());
        string safeName = string.Concat(branch.Select(character => Path.GetInvalidFileNameChars().Contains(character) || character == '/' ? '_' : character));
        Directory.CreateDirectory(archiveDirectory);
        string archivePath = Path.Combine(archiveDirectory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeName}-{tip[..10]}.cygitarchive");
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "cyrevision-git-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        string bundlePath = Path.Combine(temporaryRoot, "branch.bundle");
        try
        {
            await RunGitWithoutOutputAsync(repositoryPath, ["bundle", "create", bundlePath, $"refs/heads/{branch}"], cancellationToken).ConfigureAwait(false);
            await RunGitWithoutOutputAsync(repositoryPath, ["bundle", "verify", bundlePath], cancellationToken).ConfigureAwait(false);
            GitArchivedBranch manifest = new(branch, tip, lastCommit, DateTimeOffset.UtcNow, profile.Id, false, archivePath);
            string manifestPath = Path.Combine(temporaryRoot, "archive.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, ArchiveJsonOptions), cancellationToken).ConfigureAwait(false);
            string temporaryArchive = archivePath + ".tmp";
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
            using (ZipArchive zip = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(bundlePath, "branch.bundle", CompressionLevel.SmallestSize);
                zip.CreateEntryFromFile(manifestPath, "archive.json", CompressionLevel.Optimal);
            }
            File.Move(temporaryArchive, archivePath);
            await VerifyColdArchiveAsync(repositoryPath, archivePath, branch, tip, cancellationToken).ConfigureAwait(false);
            if (removeAfterVerifiedArchive)
            {
                await RunGitWithoutOutputAsync(repositoryPath, ["branch", "-D", branch], cancellationToken).ConfigureAwait(false);
                manifest = manifest with { SourceBranchRemoved = true };
                RewriteArchiveManifest(archivePath, manifest);
            }
            return manifest;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    public async Task<IReadOnlyList<GitArchivedBranch>> ListArchivedBranchesAsync(
        string archiveDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(archiveDirectory)) return [];
        List<GitArchivedBranch> results = [];
        foreach (string path in Directory.EnumerateFiles(archiveDirectory, "*.cygitarchive"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(path);
                ZipArchiveEntry? entry = zip.GetEntry("archive.json");
                if (entry is null) continue;
                await using Stream stream = entry.Open();
                GitArchivedBranch? item = await JsonSerializer.DeserializeAsync<GitArchivedBranch>(stream, ArchiveJsonOptions, cancellationToken).ConfigureAwait(false);
                if (item is not null) results.Add(item with { ArchivePath = path });
            }
            catch (InvalidDataException)
            {
                // A partial or foreign archive is ignored and never deleted.
            }
        }
        return results.OrderByDescending(item => item.ArchivedAt).ToArray();
    }

    public async Task RestoreArchivedBranchAsync(
        string repositoryPath,
        GitArchivedBranch archive,
        string restoredBranchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoredBranchName);
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "cyrevision-git-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            using (ZipArchive zip = ZipFile.OpenRead(archive.ArchivePath))
            {
                ZipArchiveEntry bundle = zip.GetEntry("branch.bundle") ?? throw new InvalidDataException("The archive bundle is missing.");
                bundle.ExtractToFile(Path.Combine(temporaryRoot, "branch.bundle"));
            }
            string bundlePath = Path.Combine(temporaryRoot, "branch.bundle");
            await RunGitWithoutOutputAsync(repositoryPath, ["bundle", "verify", bundlePath], cancellationToken).ConfigureAwait(false);
            await RunGitWithoutOutputAsync(
                repositoryPath,
                ["fetch", bundlePath, $"refs/heads/{archive.Branch}:refs/heads/{restoredBranchName}"],
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    private static GitArchiveCandidate? ParseArchiveCandidate(string line)
    {
        string[] columns = line.Split('\0');
        if (columns.Length != 4 || !DateTimeOffset.TryParse(columns[2], out DateTimeOffset updated)) return null;
        return new GitArchiveCandidate(
            columns[0],
            columns[1],
            updated,
            Math.Max(0, (int)(DateTimeOffset.UtcNow - updated).TotalDays),
            RemoteReference: string.IsNullOrWhiteSpace(columns[3]) ? null : columns[3]);
    }

    private static void RewriteArchiveManifest(string archivePath, GitArchivedBranch manifest)
    {
        using ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        zip.GetEntry("archive.json")?.Delete();
        ZipArchiveEntry entry = zip.CreateEntry("archive.json", CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, ArchiveJsonOptions);
    }

    private async Task VerifyColdArchiveAsync(
        string repositoryPath,
        string archivePath,
        string expectedBranch,
        string expectedTip,
        CancellationToken cancellationToken)
    {
        string verificationRoot = Path.Combine(Path.GetTempPath(), "cyrevision-git-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verificationRoot);
        try
        {
            string bundle = Path.Combine(verificationRoot, "branch.bundle");
            GitArchivedBranch manifest;
            using (ZipArchive zip = ZipFile.OpenRead(archivePath))
            {
                ZipArchiveEntry bundleEntry = zip.GetEntry("branch.bundle")
                    ?? throw new InvalidDataException("The cold archive bundle is missing.");
                bundleEntry.ExtractToFile(bundle);
                ZipArchiveEntry manifestEntry = zip.GetEntry("archive.json")
                    ?? throw new InvalidDataException("The cold archive manifest is missing.");
                using Stream stream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<GitArchivedBranch>(stream, ArchiveJsonOptions)
                    ?? throw new InvalidDataException("The cold archive manifest is invalid.");
            }
            if (!manifest.Branch.Equals(expectedBranch, StringComparison.Ordinal) ||
                !manifest.Tip.Equals(expectedTip, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The cold archive manifest does not match the selected branch.");
            await RunGitWithoutOutputAsync(repositoryPath, ["bundle", "verify", bundle], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(verificationRoot)) Directory.Delete(verificationRoot, true);
        }
    }
}
