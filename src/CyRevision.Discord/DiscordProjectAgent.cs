namespace CyRevision.Discord;

public sealed class DiscordProjectAgent : IAsyncDisposable
{
    private readonly IDiscordProjectSnapshotProvider _snapshotProvider;
    private readonly JsonDiscordAgentStore _store;
    private readonly DiscordWebhookClient _webhookClient;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private DiscordAgentProfile? _profile;
    private string? _repositoryPath;
    private string? _projectName;

    public DiscordProjectAgent(
        IDiscordProjectSnapshotProvider snapshotProvider,
        JsonDiscordAgentStore store,
        DiscordWebhookClient webhookClient)
    {
        _snapshotProvider = snapshotProvider;
        _store = store;
        _webhookClient = webhookClient;
    }

    public event EventHandler<DiscordAgentStatus>? StatusChanged;

    public bool IsRunning => _runCancellation is { IsCancellationRequested: false };

    public async Task StartAsync(
        DiscordAgentProfile profile,
        string repositoryPath,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            _profile = profile;
            _repositoryPath = Path.GetFullPath(repositoryPath);
            _projectName = projectName.Trim();
            _runCancellation = new CancellationTokenSource();
            Publish(new DiscordAgentStatus(DiscordAgentRuntimeState.Starting, "Reading the current Git baseline…"));
            await PollCoreAsync(_runCancellation.Token);
            _runTask = RunAsync(_runCancellation.Token);
        }
        catch
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _profile = null;
            _repositoryPath = null;
            _projectName = null;
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task PollNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The Discord agent is not running.");
        }

        try
        {
            await PollCoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(new DiscordAgentStatus(DiscordAgentRuntimeState.Error, exception.Message));
            throw;
        }
    }

    public Task SendTestAsync(
        DiscordAgentProfile profile,
        string projectName,
        CancellationToken cancellationToken = default) =>
        _webhookClient.SendTestAsync(profile, projectName, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
        _pollGate.Dispose();
        _webhookClient.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        DiscordAgentProfile profile = _profile!;
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(profile.PollIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await PollCoreAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Publish(new DiscordAgentStatus(
                        DiscordAgentRuntimeState.Error,
                        exception.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the user stops the agent or changes project.
        }
    }

    private async Task PollCoreAsync(CancellationToken cancellationToken)
    {
        await _pollGate.WaitAsync(cancellationToken);
        try
        {
            DiscordAgentProfile profile = _profile
                ?? throw new InvalidOperationException("The Discord agent has no active profile.");
            string repositoryPath = _repositoryPath
                ?? throw new InvalidOperationException("The Discord agent has no repository path.");
            string projectName = _projectName
                ?? throw new InvalidOperationException("The Discord agent has no project name.");

            DiscordProjectSnapshot snapshot = await _snapshotProvider.GetSnapshotAsync(
                repositoryPath,
                100,
                cancellationToken);
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            DiscordAgentCheckpoint? checkpoint = await _store.GetCheckpointAsync(
                profile.ProjectId,
                cancellationToken);

            if (checkpoint is null || string.IsNullOrWhiteSpace(checkpoint.LastAnnouncedCommitHash))
            {
                DiscordAgentCheckpoint baseline = new(
                    profile.ProjectId,
                    snapshot.HeadHash,
                    snapshot.Branch,
                    checkedAt);
                await _store.SaveCheckpointAsync(baseline, cancellationToken);
                Publish(new DiscordAgentStatus(
                    DiscordAgentRuntimeState.Watching,
                    snapshot.HeadHash is null
                        ? "Watching an empty repository. No history was published."
                        : "Watching from the current commit. Existing history was not published.",
                    snapshot.Branch,
                    checkedAt));
                return;
            }

            IReadOnlyList<DiscordCommit> newCommits = [];
            bool historyRewritten = false;
            if (!string.IsNullOrWhiteSpace(snapshot.HeadHash) &&
                !string.Equals(snapshot.HeadHash, checkpoint.LastAnnouncedCommitHash, StringComparison.OrdinalIgnoreCase))
            {
                int baselineIndex = snapshot.Commits
                    .Select((commit, index) => (commit, index))
                    .Where(item => string.Equals(
                        item.commit.Hash,
                        checkpoint.LastAnnouncedCommitHash,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.index)
                    .DefaultIfEmpty(-1)
                    .First();
                if (baselineIndex >= 0)
                {
                    newCommits = snapshot.Commits.Take(baselineIndex).ToArray();
                }
                else
                {
                    newCommits = snapshot.Commits.Take(1).ToArray();
                    historyRewritten = true;
                }
            }

            bool branchChanged = !string.IsNullOrWhiteSpace(checkpoint.LastBranch) &&
                                 !string.Equals(checkpoint.LastBranch, snapshot.Branch, StringComparison.Ordinal);
            bool shouldSend = profile.NotifyCommits && newCommits.Count > 0 ||
                              profile.NotifyBranchChanges && branchChanged;
            DateTimeOffset? notificationAt = checkpoint.LastNotificationAt;
            if (shouldSend)
            {
                Publish(new DiscordAgentStatus(
                    DiscordAgentRuntimeState.Sending,
                    "Sending the detected project update…",
                    snapshot.Branch,
                    checkedAt,
                    notificationAt));
                await _webhookClient.SendProjectUpdateAsync(
                    profile,
                    projectName,
                    snapshot,
                    newCommits,
                    checkpoint.LastBranch,
                    historyRewritten,
                    cancellationToken);
                notificationAt = DateTimeOffset.UtcNow;
            }

            DiscordAgentCheckpoint updated = checkpoint with
            {
                LastAnnouncedCommitHash = snapshot.HeadHash ?? checkpoint.LastAnnouncedCommitHash,
                LastBranch = snapshot.Branch,
                LastCheckedAt = checkedAt,
                LastNotificationAt = notificationAt
            };
            await _store.SaveCheckpointAsync(updated, cancellationToken);
            Publish(new DiscordAgentStatus(
                DiscordAgentRuntimeState.Watching,
                shouldSend
                    ? $"Published {newCommits.Count} new commit(s)."
                    : "No new project update.",
                snapshot.Branch,
                checkedAt,
                notificationAt));
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? cancellation = _runCancellation;
        Task? runTask = _runTask;
        _runCancellation = null;
        _runTask = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
        }

        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // Expected while stopping.
            }
        }

        cancellation?.Dispose();
        _profile = null;
        _repositoryPath = null;
        _projectName = null;
        Publish(new DiscordAgentStatus(DiscordAgentRuntimeState.Stopped, "Discord agent stopped."));
    }

    private void Publish(DiscordAgentStatus status) => StatusChanged?.Invoke(this, status);
}
