using CyRevision.Git;

namespace CyRevision.Discord;

public interface IDiscordProjectSnapshotProvider
{
    Task<DiscordProjectSnapshot> GetSnapshotAsync(
        string repositoryPath,
        int maximumCommitCount = 100,
        CancellationToken cancellationToken = default);
}

public sealed class GitDiscordProjectSnapshotProvider(IGitRepositoryService gitService)
    : IDiscordProjectSnapshotProvider
{
    public async Task<DiscordProjectSnapshot> GetSnapshotAsync(
        string repositoryPath,
        int maximumCommitCount = 100,
        CancellationToken cancellationToken = default)
    {
        Task<GitRepositoryStatus> statusTask = gitService.GetStatusAsync(repositoryPath, cancellationToken);
        Task<IReadOnlyList<GitRevision>> historyTask = gitService.GetHistoryAsync(
            repositoryPath,
            maximumCommitCount,
            cancellationToken);
        await Task.WhenAll(statusTask, historyTask);

        GitRepositoryStatus status = await statusTask;
        IReadOnlyList<GitRevision> history = await historyTask;
        return new DiscordProjectSnapshot(
            status.IsDetachedHead ? $"HEAD {status.CurrentBranch}" : status.CurrentBranch,
            history.FirstOrDefault()?.Hash,
            history.Select(revision => new DiscordCommit(
                revision.Hash,
                revision.ShortHash,
                revision.AuthorName,
                revision.AuthoredAt,
                revision.Subject)).ToArray());
    }
}
