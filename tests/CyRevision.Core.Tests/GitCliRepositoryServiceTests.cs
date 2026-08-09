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
