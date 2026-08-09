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

        GitRepositoryStatus beforeCommit = await service.GetStatusAsync(_temporaryDirectory);
        Assert.Contains(beforeCommit.Changes, change => change.Path == "README.md" && change.Kind == GitChangeKind.Untracked);

        await service.CreateRevisionAsync(_temporaryDirectory, "Initial revision", ["README.md"]);

        GitRepositoryStatus afterCommit = await service.GetStatusAsync(_temporaryDirectory);
        IReadOnlyList<GitRevision> history = await service.GetHistoryAsync(_temporaryDirectory);
        Assert.Empty(afterCommit.Changes);
        Assert.Single(history);
        Assert.Equal("Initial revision", history[0].Subject);
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
