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
