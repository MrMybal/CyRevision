using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitCliRepositoryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"cyrevision-git-{Guid.NewGuid():N}");

    [Fact]
    public async Task RepositoryCanBeInitializedCommittedAndInspected()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "README.md"), "CyRevision test repository");
        byte[] binaryContents = [0, 1, 2, 127, 128, 254, 255];
        await File.WriteAllBytesAsync(Path.Combine(_temporaryDirectory, "asset.bin"), binaryContents);

        GitRepositoryStatus beforeCommit = await service.GetStatusAsync(_temporaryDirectory);
        Assert.Contains(beforeCommit.Changes, change => change.Path == "README.md" && change.Kind == GitChangeKind.Untracked);

        await service.CreateRevisionAsync(_temporaryDirectory, "Initial revision", ["README.md", "asset.bin"]);

        GitRepositoryStatus afterCommit = await service.GetStatusAsync(_temporaryDirectory);
        IReadOnlyList<GitRevision> history = await service.GetHistoryAsync(_temporaryDirectory);
        Assert.Empty(afterCommit.Changes);
        Assert.Single(history);
        Assert.Equal("Initial revision", history[0].Subject);

        string exportedPath = Path.Combine(_temporaryDirectory, "exported", "asset.bin");
        await service.ExportFileFromRevisionAsync(_temporaryDirectory, "asset.bin", "HEAD", exportedPath);
        Assert.Equal(binaryContents, await File.ReadAllBytesAsync(exportedPath));

        await File.AppendAllTextAsync(Path.Combine(_temporaryDirectory, "README.md"), "\nGraph visualization");
        await File.WriteAllBytesAsync(Path.Combine(_temporaryDirectory, "asset.bin"), [0, 9, 0, 8, 0, 7]);
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "GraphSample.cs"), "public sealed class GraphSample { }");
        await service.CreateRevisionAsync(
            _temporaryDirectory,
            "Add graph sample",
            ["README.md", "asset.bin", "GraphSample.cs"]);

        await service.CreateBranchAsync(_temporaryDirectory, "feature/graph-test");
        await File.AppendAllTextAsync(Path.Combine(_temporaryDirectory, "GraphSample.cs"), "\n// branch");
        await service.CreateRevisionAsync(_temporaryDirectory, "Update code on feature", ["GraphSample.cs"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "main");
        await File.AppendAllTextAsync(Path.Combine(_temporaryDirectory, "README.md"), "\nMain branch");
        await service.CreateRevisionAsync(_temporaryDirectory, "Update documentation on main", ["README.md"]);
        await service.MergeBranchAsync(_temporaryDirectory, "feature/graph-test");

        IReadOnlyList<GitGraphCommit> commitGraph = await service.GetCommitGraphAsync(_temporaryDirectory);
        GitFileActivityGraph fileGraph = await service.GetFileActivityGraphAsync(_temporaryDirectory);
        Assert.Equal(5, commitGraph.Count);
        Assert.Contains(commitGraph, commit => commit.IsMerge && commit.ParentHashes.Count == 2);
        Assert.Contains(commitGraph, commit => commit.Decorations.Contains("HEAD", StringComparison.Ordinal));
        Assert.Equal(3, fileGraph.TotalFileCount);
        Assert.Contains(fileGraph.Files, file => file.Path == "README.md" && file.ChangeCount == 3);
        Assert.Contains(fileGraph.Files, file =>
            file.Path == "asset.bin" && file.BinaryChangeCount == 2 && file.Kind == GitFileKind.Other);
        Assert.Contains(fileGraph.Files, file => file.Path == "GraphSample.cs" && file.Kind == GitFileKind.Code);
        Assert.Contains(fileGraph.Relations, relation =>
            relation.CoChangeCount == 2 &&
            new[] { relation.SourcePath, relation.TargetPath }.Contains("README.md") &&
            new[] { relation.SourcePath, relation.TargetPath }.Contains("asset.bin"));

        GitRepositoryInsights insights = await service.GetRepositoryInsightsAsync(_temporaryDirectory);
        Assert.Equal(5, insights.CommitCount);
        Assert.Equal(1, insights.MergeCount);
        Assert.Single(insights.Contributors);
        Assert.Equal(3, insights.FileCount);
        Assert.NotEmpty(insights.DailyActivity);
        Assert.Contains(insights.HotFiles, file => file.Path == "README.md");

        GitCommitDetails details = await service.GetCommitDetailsAsync(_temporaryDirectory, "HEAD^1");
        Assert.Equal("Update documentation on main", details.Revision.Subject);
        Assert.Contains(details.Files, file => file.Path == "README.md" && file.AddedLines > 0);

        GitCommitComparison comparison = await service.CompareCommitsAsync(_temporaryDirectory, "HEAD^1", "HEAD");
        Assert.Contains(comparison.Files, file => file.Path == "GraphSample.cs");
        Assert.Contains("GraphSample", await service.GetComparisonDiffAsync(_temporaryDirectory, "HEAD^1", "HEAD"));

        IReadOnlyList<GitFileRevision> readmeHistory = await service.GetFileHistoryAsync(
            _temporaryDirectory,
            "README.md");
        Assert.Equal(3, readmeHistory.Count);
        Assert.Equal("Update documentation on main", readmeHistory[0].Revision.Subject);

        IReadOnlyList<GitRevision> featureHistory = await service.GetHistoryForReferenceAsync(
            _temporaryDirectory,
            "feature/graph-test");
        Assert.Equal("Update code on feature", featureHistory[0].Subject);
        Assert.DoesNotContain(featureHistory, revision => revision.Subject == "Update documentation on main");
    }

    [Fact]
    public void BranchPresentationDistinguishesLocalPublishedAndTrackingStates()
    {
        GitBranch local = new("feature/local", "1234567", false);
        GitBranch ahead = new("feature/published", "7654321", true, false, "origin/feature/published", 3, 0, true);
        GitBranch remote = new("origin/main", "abcdef0", false, true);

        Assert.False(local.IsPublished);
        Assert.Equal("Local only", local.PublicationStatus);
        Assert.Contains("Not published", local.SyncStatus);
        Assert.True(ahead.IsPublished);
        Assert.Contains("↑3", ahead.SyncStatus);
        Assert.Equal("Remote ref", remote.PublicationStatus);
    }

    [Fact]
    public async Task StatusExpandsUntrackedFoldersAndDiscardRestoresTrackedAndUntrackedFiles()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        string trackedPath = Path.Combine(_temporaryDirectory, "Source", "Tracked.cs");
        string localPath = Path.Combine(_temporaryDirectory, "Saved", "local.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(trackedPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(trackedPath, "original");
        await service.CreateRevisionAsync(_temporaryDirectory, "Initial", ["Source/Tracked.cs"]);

        await File.WriteAllTextAsync(trackedPath, "modified");
        await File.WriteAllTextAsync(localPath, "local-only");
        GitRepositoryStatus status = await service.GetDetailedStatusAsync(_temporaryDirectory);
        GitChange tracked = Assert.Single(status.Changes, item => item.Path == "Source/Tracked.cs");
        GitChange untracked = Assert.Single(status.Changes, item => item.Path == "Saved/local.txt");

        await service.DiscardChangesAsync(_temporaryDirectory, [tracked, untracked]);

        Assert.Equal("original", await File.ReadAllTextAsync(trackedPath));
        Assert.False(File.Exists(localPath));
        Assert.Empty((await service.GetStatusAsync(_temporaryDirectory)).Changes);
    }

    [Fact]
    public async Task LfsTimeMachineFindsExportsAndRestoresLocalVersions()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await service.TrackLfsPatternAsync(_temporaryDirectory, "*.uasset");
        string assetPath = Path.Combine(_temporaryDirectory, "Content", "Hero.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        byte[] firstVersion = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        byte[] secondVersion = Enumerable.Range(0, 6144).Select(index => (byte)((index * 7) % 253)).ToArray();
        await File.WriteAllBytesAsync(assetPath, firstVersion);
        await service.CreateRevisionAsync(
            _temporaryDirectory,
            "Add hero asset",
            [".gitattributes", "Content/Hero.uasset"]);
        await File.WriteAllBytesAsync(assetPath, secondVersion);
        await service.CreateRevisionAsync(_temporaryDirectory, "Update hero asset", ["Content/Hero.uasset"]);

        IReadOnlyList<LfsTrackedFile> trackedFiles = await service.GetLfsTrackedFilesAsync(_temporaryDirectory);
        LfsTrackedFile tracked = Assert.Single(trackedFiles);
        Assert.Equal("Content/Hero.uasset", tracked.Path);
        Assert.True(tracked.IsAvailableLocally);
        Assert.Equal(secondVersion.LongLength, tracked.Pointer.Size);

        IReadOnlyList<LfsFileVersion> versions = await service.GetLfsFileVersionsAsync(
            _temporaryDirectory,
            tracked.Path);
        Assert.Equal(2, versions.Count);
        Assert.All(versions, version => Assert.True(version.IsAvailableLocally));
        Assert.True(versions[0].IsCurrent);
        Assert.Equal(firstVersion.LongLength, versions[1].Pointer.Size);

        GitCommitDetails latest = await service.GetCommitDetailsAsync(_temporaryDirectory, "HEAD");
        GitCommitFileChange changedAsset = Assert.Single(latest.Files);
        Assert.True(changedAsset.IsLfsObject);
        Assert.Equal(secondVersion.LongLength, changedAsset.LfsPointer!.Size);

        string exportPath = Path.Combine(_temporaryDirectory, "exports", "Hero-old.uasset");
        await service.ExportLfsFileVersionAsync(_temporaryDirectory, versions[1], exportPath);
        Assert.Equal(firstVersion, await File.ReadAllBytesAsync(exportPath));

        await service.RestoreLfsFileVersionAsync(_temporaryDirectory, versions[1]);
        Assert.Equal(firstVersion, await File.ReadAllBytesAsync(assetPath));
    }

    [Fact]
    public void LfsLockParserSeparatesOurLocksFromOtherUsers()
    {
        const string json = """
                            {
                              "ours": [
                                {
                                  "id": "lock-mine",
                                  "path": "Content/Hero.uasset",
                                  "locked_at": "2026-08-13T10:15:30Z",
                                  "owner": { "name": "CyberAlien" }
                                }
                              ],
                              "theirs": [
                                {
                                  "id": "lock-other",
                                  "path": "Content/World.umap",
                                  "locked_at": "2026-08-12T08:00:00Z",
                                  "owner": { "name": "Teammate" }
                                }
                              ]
                            }
                            """;

        IReadOnlyList<LfsFileLock> locks = GitCliRepositoryService.ParseLfsLocksJson(json);

        Assert.Equal(2, locks.Count);
        Assert.Contains(locks, item => item.Id == "lock-mine" && item.IsOurs && item.OwnerName == "CyberAlien");
        Assert.Contains(locks, item => item.Id == "lock-other" && !item.IsOurs && item.Path == "Content/World.umap");
    }

    [Fact]
    public void LfsLockParserCanMarkOfflineLocalCacheAsOurs()
    {
        const string json = """
                            {
                              "locks": [
                                { "id": "cached-lock", "path": "Content/Offline.uasset", "owner": { "name": "Local user" } }
                              ]
                            }
                            """;

        LfsFileLock item = Assert.Single(GitCliRepositoryService.ParseLfsLocksJson(
            json,
            defaultOurs: true,
            isCached: true));

        Assert.True(item.IsOurs);
        Assert.True(item.IsCached);
        Assert.Equal("Cached", item.Source);
    }

    [Fact]
    public async Task MultiRestoreComposesSeveralFileVersionsWithoutTouchingTheIndex()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        string alpha = Path.Combine(_temporaryDirectory, "alpha.txt");
        string beta = Path.Combine(_temporaryDirectory, "beta.txt");
        string gamma = Path.Combine(_temporaryDirectory, "gamma.txt");
        await File.WriteAllTextAsync(alpha, "alpha v1");
        await File.WriteAllTextAsync(beta, "beta v1");
        await service.CreateRevisionAsync(_temporaryDirectory, "Initial files", ["alpha.txt", "beta.txt"]);

        await File.WriteAllTextAsync(alpha, "alpha v2");
        File.Delete(beta);
        await File.WriteAllTextAsync(gamma, "gamma v1");
        await service.CreateRevisionAsync(_temporaryDirectory, "Change file set", ["alpha.txt", "beta.txt", "gamma.txt"]);
        GitRevision changedCommit = (await service.GetHistoryAsync(_temporaryDirectory, 1)).Single();

        await File.WriteAllTextAsync(alpha, "uncommitted work");
        GitMultiRestorePlan plan = await service.CreateMultiRestorePlanAsync(
            _temporaryDirectory,
            changedCommit.Hash,
            [
                new GitMultiRestoreSelection("alpha.txt", GitRestorePoint.BeforeCommit),
                new GitMultiRestoreSelection("beta.txt", GitRestorePoint.BeforeCommit),
                new GitMultiRestoreSelection("gamma.txt", GitRestorePoint.BeforeCommit)
            ]);

        Assert.True(plan.HasLocalChanges);
        await Assert.ThrowsAsync<GitOperationException>(() =>
            service.ApplyMultiRestorePlanAsync(_temporaryDirectory, plan, overwriteLocalChanges: false));

        GitMultiRestoreResult result = await service.ApplyMultiRestorePlanAsync(
            _temporaryDirectory,
            plan,
            overwriteLocalChanges: true);
        Assert.Equal("alpha v1", await File.ReadAllTextAsync(alpha));
        Assert.Equal("beta v1", await File.ReadAllTextAsync(beta));
        Assert.False(File.Exists(gamma));
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "manifest.json")));

        GitRepositoryStatus status = await service.GetStatusAsync(_temporaryDirectory);
        Assert.Contains(status.Changes, change => change.Path == "alpha.txt");
        Assert.Contains(status.Changes, change => change.Path == "beta.txt");
        Assert.Contains(status.Changes, change => change.Path == "gamma.txt");
        Assert.DoesNotContain(status.Changes, change => change.IsStaged);
    }

    [Fact]
    public async Task BranchComparisonAndCherryPickUseAnIsolatedTargetWorktree()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "base.txt"), "base");
        await service.CreateRevisionAsync(_temporaryDirectory, "Base", ["base.txt"]);
        await service.CreateBranchAsync(_temporaryDirectory, "feature/composer");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "one.txt"), "one");
        await service.CreateRevisionAsync(_temporaryDirectory, "Feature one", ["one.txt"]);
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "two.txt"), "two");
        await service.CreateRevisionAsync(_temporaryDirectory, "Feature two", ["two.txt"]);

        await service.CheckoutBranchAsync(_temporaryDirectory, "main");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "main.txt"), "main");
        await service.CreateRevisionAsync(_temporaryDirectory, "Main only", ["main.txt"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "feature/composer");

        GitBranchComparison comparison = await service.CompareBranchesAsync(
            _temporaryDirectory,
            "feature/composer",
            "main");
        Assert.Equal(2, comparison.SourceOnlyCount);
        Assert.Equal(1, comparison.TargetOnlyCount);

        string[] ordered = comparison.Commits
            .Where(commit => commit.Presence == GitBranchCommitPresence.SourceOnly)
            .Reverse()
            .Select(commit => commit.Revision.Hash)
            .ToArray();
        GitCherryPickPlan plan = await service.CreateCherryPickPlanAsync(
            _temporaryDirectory,
            "feature/composer",
            "main",
            ordered,
            GitCherryPickMode.KeepCommits);
        Assert.True(plan.UsesTemporaryWorktree);
        Assert.True(plan.CanApply);

        GitCherryPickResult result = await service.ApplyCherryPickPlanAsync(_temporaryDirectory, plan);
        Assert.Equal(2, result.AppliedCommitCount);
        Assert.Equal("feature/composer", (await service.GetStatusAsync(_temporaryDirectory)).CurrentBranch);

        GitBranchComparison after = await service.CompareBranchesAsync(
            _temporaryDirectory,
            "feature/composer",
            "main");
        Assert.Equal(0, after.SourceOnlyCount);
        Assert.True(after.EquivalentCount >= 2);
    }

    [Fact]
    public async Task BranchDetailsExposeExactTipAndClearlyInferredCreationMetadata()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable)
        {
            return;
        }

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "Base Author", "base@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "base.txt"), "base");
        await service.CreateRevisionAsync(_temporaryDirectory, "Base", ["base.txt"]);

        await service.CreateBranchAsync(_temporaryDirectory, "feature/details");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "first.txt"), "first");
        await service.CreateRevisionAsync(_temporaryDirectory, "First branch commit", ["first.txt"]);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "Latest Author", "latest@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "latest.txt"), "latest");
        await service.CreateRevisionAsync(_temporaryDirectory, "Latest branch commit", ["latest.txt"]);

        GitBranchDetails details = await service.GetBranchDetailsAsync(
            _temporaryDirectory,
            "feature/details");

        Assert.Equal("main", details.ComparisonBase);
        Assert.Equal(2, details.UniqueCommitCount);
        Assert.Equal("Base Author", details.InferredCreatorName);
        Assert.NotNull(details.InferredCreatedAt);
        Assert.Equal("Latest Author", details.LastAuthorName);
        Assert.Equal("Latest branch commit", details.LastSubject);
        Assert.NotNull(details.LastUpdatedAt);
    }

    [Fact]
    public async Task HistoricalBranchUsesManagedWorktreeWithoutSwitchingMainRepository()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "snapshot.txt"), "historical");
        await service.CreateRevisionAsync(_temporaryDirectory, "Historical snapshot", ["snapshot.txt"]);
        GitRevision revision = Assert.Single(await service.GetHistoryAsync(_temporaryDirectory));

        GitHistoricalWorktreeResult created = await service.CreateHistoricalWorktreeAsync(
            _temporaryDirectory, revision.Hash, "test/historical-snapshot");

        Assert.True(created.Succeeded);
        Assert.True(File.Exists(Path.Combine(created.WorktreePath, "snapshot.txt")));
        Assert.Equal("main", (await service.GetStatusAsync(_temporaryDirectory)).CurrentBranch);
        GitHistoricalWorktree managed = Assert.Single((await service.GetHistoricalWorktreesAsync(_temporaryDirectory))
            .Where(item => item.IsManagedByCyRevision));
        Assert.Equal("test/historical-snapshot", managed.Branch);

        await service.RemoveHistoricalWorktreeAsync(_temporaryDirectory, managed.Path);
        Assert.False(Directory.Exists(managed.Path));
        Assert.DoesNotContain(await service.GetHistoricalWorktreesAsync(_temporaryDirectory),
            item => string.Equals(item.Path, managed.Path, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(_temporaryDirectory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
