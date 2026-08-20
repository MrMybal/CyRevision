using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class GitCliRepositoryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"cyrevision-git-{Guid.NewGuid():N}");

    [Fact]
    public async Task LocalRemoteCanBeClonedIntoChosenDestination()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable || !tools.LfsAvailable) return;

        string source = Path.Combine(_temporaryDirectory, "source");
        string destination = Path.Combine(_temporaryDirectory, "workspace", "cloned-project");
        await service.InitializeAsync(source);
        await service.ConfigureIdentityAsync(source, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(source, "README.md"), "clone integration");
        await service.CreateRevisionAsync(source, "Initial clone source", ["README.md"]);

        await service.CloneAsync(source, destination);

        GitRepositoryStatus status = await service.GetStatusAsync(destination);
        Assert.Equal("cloned-project", new DirectoryInfo(status.RootPath).Name);
        Assert.True(Directory.Exists(status.RootPath));
        Assert.Equal("main", status.CurrentBranch);
        Assert.Equal("clone integration", await File.ReadAllTextAsync(Path.Combine(destination, "README.md")));
        Assert.Contains("Initial clone source", (await service.GetHistoryAsync(destination)).Select(item => item.Subject));
    }

    [Fact]
    public async Task BinaryConflictCanChooseIncomingWholeFileVersion()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        string file = Path.Combine(_temporaryDirectory, "Content", "Asset.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
        await service.CreateRevisionAsync(_temporaryDirectory, "Initial asset", ["Content/Asset.uasset"]);

        await service.CreateBranchAsync(_temporaryDirectory, "feature/asset");
        byte[] incoming = [1, 8, 8, 4];
        await File.WriteAllBytesAsync(file, incoming);
        await service.CreateRevisionAsync(_temporaryDirectory, "Incoming asset", ["Content/Asset.uasset"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "main");
        await File.WriteAllBytesAsync(file, [1, 9, 9, 4]);
        await service.CreateRevisionAsync(_temporaryDirectory, "Our asset", ["Content/Asset.uasset"]);

        await Assert.ThrowsAsync<GitOperationException>(() =>
            service.MergeBranchAsync(_temporaryDirectory, "feature/asset"));

        GitConflictFile conflict = Assert.Single((await service.GetConflictStateAsync(_temporaryDirectory)).Files);
        Assert.True(conflict.IsBinary);
        Assert.False(conflict.CanEditManually);

        await service.ResolveConflictAsync(
            _temporaryDirectory,
            conflict.Path,
            GitConflictResolutionChoice.Theirs);

        Assert.Equal(incoming, await File.ReadAllBytesAsync(file));
        Assert.Empty((await service.GetConflictStateAsync(_temporaryDirectory)).Files);
        await service.AbortConflictOperationAsync(_temporaryDirectory);
    }

    [Fact]
    public async Task ThreeWayConflictCanBeInspectedResolvedAndContinued()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        string file = Path.Combine(_temporaryDirectory, "Source", "Conflict.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "line one\nshared base\nline three\n");
        await service.CreateRevisionAsync(_temporaryDirectory, "Initial", ["Source/Conflict.cs"]);

        await service.CreateBranchAsync(_temporaryDirectory, "feature/conflict");
        await File.WriteAllTextAsync(file, "line one\nincoming change\nline three\n");
        await service.CreateRevisionAsync(_temporaryDirectory, "Incoming change", ["Source/Conflict.cs"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "main");
        await File.WriteAllTextAsync(file, "line one\nour change\nline three\n");
        await service.CreateRevisionAsync(_temporaryDirectory, "Our change", ["Source/Conflict.cs"]);

        await Assert.ThrowsAsync<GitOperationException>(() =>
            service.MergeBranchAsync(_temporaryDirectory, "feature/conflict"));

        GitConflictState conflictState = await service.GetConflictStateAsync(_temporaryDirectory);
        GitConflictFile conflict = Assert.Single(conflictState.Files);
        Assert.Equal(GitConflictOperation.Merge, conflictState.Operation);
        Assert.Equal("Source/Conflict.cs", conflict.Path);
        Assert.Contains("shared base", conflict.Base.Text);
        Assert.Contains("our change", conflict.Ours.Text);
        Assert.Contains("incoming change", conflict.Theirs.Text);
        Assert.Contains("<<<<<<<", conflict.WorkingText);
        Assert.True(conflict.CanEditManually);

        await service.ResolveConflictAsync(
            _temporaryDirectory,
            conflict.Path,
            GitConflictResolutionChoice.Manual,
            "line one\ncombined result\nline three\n");

        GitConflictState resolvedState = await service.GetConflictStateAsync(_temporaryDirectory);
        Assert.Empty(resolvedState.Files);
        Assert.Equal(GitConflictOperation.Merge, resolvedState.Operation);

        await service.ContinueConflictOperationAsync(_temporaryDirectory);

        Assert.Equal("line one\ncombined result\nline three\n", await File.ReadAllTextAsync(file));
        Assert.Empty((await service.GetStatusAsync(_temporaryDirectory)).Changes);
        Assert.Equal(GitConflictOperation.None, (await service.GetConflictStateAsync(_temporaryDirectory)).Operation);
    }

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
    public async Task RevisionCanStageAPathListLargerThanTheWindowsCommandLineLimit()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");

        List<string> paths = [];
        string filesDirectory = Path.Combine(_temporaryDirectory, "Generated");
        Directory.CreateDirectory(filesDirectory);
        for (int index = 0; index < 600; index++)
        {
            string relativePath = $"Generated/{index:D4}-long-selected-file-name-for-command-line-limit-validation.txt";
            await File.WriteAllTextAsync(
                Path.Combine(_temporaryDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                $"file {index}");
            paths.Add(relativePath);
        }

        Assert.True(paths.Sum(path => path.Length + 3) > 32767);
        await service.CreateRevisionAsync(_temporaryDirectory, "Commit a large selected path list", paths);

        GitRepositoryStatus status = await service.GetStatusAsync(_temporaryDirectory);
        GitCommitDetails details = await service.GetCommitDetailsAsync(_temporaryDirectory, "HEAD");
        Assert.Empty(status.Changes);
        Assert.Equal(paths.Count, details.Files.Count);
    }

    [Fact]
    public async Task WorkingTreeDeleteKeepsTrackedDeletionVisibleAndRemovesUntrackedFile()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        string tracked = Path.Combine(_temporaryDirectory, "Source", "Tracked.cs");
        string untracked = Path.Combine(_temporaryDirectory, "Generated", "Temporary.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(tracked)!);
        Directory.CreateDirectory(Path.GetDirectoryName(untracked)!);
        await File.WriteAllTextAsync(tracked, "tracked");
        await service.CreateRevisionAsync(_temporaryDirectory, "Add tracked file", ["Source/Tracked.cs"]);
        await File.WriteAllTextAsync(untracked, "temporary");

        await service.DeleteWorkingTreePathsAsync(
            _temporaryDirectory,
            ["Source/Tracked.cs", "Generated/Temporary.txt"]);

        Assert.False(File.Exists(tracked));
        Assert.False(File.Exists(untracked));
        GitRepositoryStatus status = await service.GetDetailedStatusAsync(_temporaryDirectory);
        Assert.Contains(status.Changes, change =>
            change.Path == "Source/Tracked.cs" && change.Kind == GitChangeKind.Deleted);
        Assert.DoesNotContain(status.Changes, change => change.Path == "Generated/Temporary.txt");
    }

    [Fact]
    public async Task WorkingTreeDeleteCanRemoveASelectedFolderRecursively()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        string tracked = Path.Combine(_temporaryDirectory, "Feature", "Tracked.cs");
        string untracked = Path.Combine(_temporaryDirectory, "Feature", "Generated", "Temporary.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(tracked)!);
        Directory.CreateDirectory(Path.GetDirectoryName(untracked)!);
        await File.WriteAllTextAsync(tracked, "tracked");
        await service.CreateRevisionAsync(_temporaryDirectory, "Add feature", ["Feature/Tracked.cs"]);
        await File.WriteAllTextAsync(untracked, "temporary");

        await service.DeleteWorkingTreePathsAsync(_temporaryDirectory, ["Feature"]);

        Assert.False(Directory.Exists(Path.Combine(_temporaryDirectory, "Feature")));
        GitRepositoryStatus status = await service.GetDetailedStatusAsync(_temporaryDirectory);
        Assert.Contains(status.Changes, change =>
            change.Path == "Feature/Tracked.cs" && change.Kind == GitChangeKind.Deleted);
        Assert.DoesNotContain(status.Changes, change => change.Path == "Feature/Generated/Temporary.txt");
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
        IReadOnlyList<GitHistoricalWorktree> worktrees = await service.GetHistoricalWorktreesAsync(_temporaryDirectory);
        GitHistoricalWorktree[] managedWorktrees = worktrees.Where(item => item.IsManagedByCyRevision).ToArray();
        Assert.True(managedWorktrees.Length == 1,
            $"Expected one managed worktree. Entries: {string.Join(" | ", worktrees.Select(item => $"{item.Path} managed={item.IsManagedByCyRevision}"))}");
        GitHistoricalWorktree managed = managedWorktrees[0];
        Assert.Equal("test/historical-snapshot", managed.Branch);

        await service.RemoveHistoricalWorktreeAsync(_temporaryDirectory, managed.Path);
        Assert.False(Directory.Exists(managed.Path));
        Assert.DoesNotContain(await service.GetHistoricalWorktreesAsync(_temporaryDirectory),
            item => string.Equals(item.Path, managed.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GitColdArchiveIsVerifiedRestorableAndNonDestructiveByDefault()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "base.txt"), "base");
        await service.CreateRevisionAsync(_temporaryDirectory, "Base", ["base.txt"]);
        await service.CreateBranchAsync(_temporaryDirectory, "feature/cold");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "cold.txt"), "cold history");
        await service.CreateRevisionAsync(_temporaryDirectory, "Cold history", ["cold.txt"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "main");
        string archiveDirectory = Path.Combine(_temporaryDirectory, ".cyrevision-test-archives");
        GitArchiveProfile profile = new("test", "Test", "Test profile", 1, 0);

        GitArchivedBranch archive = await service.ArchiveBranchAsync(
            _temporaryDirectory,
            "feature/cold",
            archiveDirectory,
            profile,
            removeAfterVerifiedArchive: false);

        Assert.True(File.Exists(archive.ArchivePath));
        Assert.False(archive.SourceBranchRemoved);
        Assert.Contains(await service.GetBranchesAsync(_temporaryDirectory), branch => branch.Name == "feature/cold");
        Assert.Single(await service.ListArchivedBranchesAsync(archiveDirectory));

        await service.RestoreArchivedBranchAsync(_temporaryDirectory, archive, "restored/cold");
        Assert.Contains(await service.GetBranchesAsync(_temporaryDirectory), branch => branch.Name == "restored/cold");
        Assert.Equal("main", (await service.GetStatusAsync(_temporaryDirectory)).CurrentBranch);
    }

    [Fact]
    public async Task ConflictResolutionBackupContainsEveryVersionAndTheProposedResult()
    {
        string root = Path.Combine(_temporaryDirectory, "conflict-recovery");
        GitConflictVersion Version(string text) => new(true, "0123456789abcdef", text.Length, text, false, false);
        GitConflictFile conflict = new(
            "Source/Conflict.cs",
            Version("base"),
            Version("ours"),
            Version("incoming"),
            "working markers",
            true);
        GitConflictResolutionBackupService service = new(root);

        GitConflictResolutionBackup backup = await service.CreateAsync(
            "Project", _temporaryDirectory, conflict, "reviewed result", "Manual result", 30);

        Assert.True(File.Exists(backup.ArchivePath));
        using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(backup.ArchivePath);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("base.txt"));
        Assert.NotNull(archive.GetEntry("ours.txt"));
        Assert.NotNull(archive.GetEntry("incoming.txt"));
        Assert.NotNull(archive.GetEntry("working-before.txt"));
        using StreamReader reader = new(archive.GetEntry("result.txt")!.Open());
        Assert.Equal("reviewed result", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task MergedLocalBranchCanBeRemovedWithoutChangingCurrentHistory()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "base.txt"), "base");
        await service.CreateRevisionAsync(_temporaryDirectory, "Base", ["base.txt"]);
        await service.CreateBranchAsync(_temporaryDirectory, "feature/finished");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "finished.txt"), "finished");
        await service.CreateRevisionAsync(_temporaryDirectory, "Finished work", ["finished.txt"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "main");
        await service.MergeBranchAsync(_temporaryDirectory, "feature/finished");

        GitLocalBranchRemovalAnalysis analysis = await service.AnalyzeLocalBranchRemovalAsync(
            _temporaryDirectory,
            "feature/finished");

        Assert.True(analysis.CanRemoveSafely);
        Assert.True(analysis.IsMergedIntoCurrent);
        await service.RemoveLocalBranchAsync(_temporaryDirectory, "feature/finished");
        Assert.DoesNotContain(
            await service.GetBranchesAsync(_temporaryDirectory),
            branch => branch.Name == "feature/finished");
        Assert.Equal("main", (await service.GetStatusAsync(_temporaryDirectory)).CurrentBranch);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "finished.txt")));
    }

    [Fact]
    public async Task LocalOnlyUnmergedBranchIsProtectedFromRemoval()
    {
        GitCliRepositoryService service = new();
        GitToolAvailability tools = await service.GetToolAvailabilityAsync();
        if (!tools.GitAvailable) return;

        await service.InitializeAsync(_temporaryDirectory);
        await service.ConfigureIdentityAsync(_temporaryDirectory, "CyRevision Tests", "tests@cyrevision.local");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "base.txt"), "base");
        await service.CreateRevisionAsync(_temporaryDirectory, "Base", ["base.txt"]);
        await service.CreateBranchAsync(_temporaryDirectory, "feature/local-only");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "unique.txt"), "unique");
        await service.CreateRevisionAsync(_temporaryDirectory, "Unique local work", ["unique.txt"]);
        await service.CheckoutBranchAsync(_temporaryDirectory, "main");

        GitLocalBranchRemovalAnalysis analysis = await service.AnalyzeLocalBranchRemovalAsync(
            _temporaryDirectory,
            "feature/local-only");

        Assert.False(analysis.CanRemoveSafely);
        Assert.False(analysis.IsMergedIntoCurrent);
        Assert.False(analysis.IsFullyPublished);
        await Assert.ThrowsAsync<GitOperationException>(() =>
            service.RemoveLocalBranchAsync(_temporaryDirectory, "feature/local-only"));
        Assert.Contains(
            await service.GetBranchesAsync(_temporaryDirectory),
            branch => branch.Name == "feature/local-only");

        await service.RemoveLocalBranchAsync(
            _temporaryDirectory,
            "feature/local-only",
            forceUnretained: true);
        Assert.DoesNotContain(
            await service.GetBranchesAsync(_temporaryDirectory),
            branch => branch.Name == "feature/local-only");
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
