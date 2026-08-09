namespace CyRevision.Sync;

public enum SyncEngineState
{
    Disabled,
    Stopped,
    Starting,
    Running,
    Paused,
    Faulted
}

public sealed record SyncEngineStatus(
    SyncEngineState State,
    int ConnectedPeers,
    long PendingBytes,
    string? Message = null);

public interface ISyncEngine
{
    SyncEngineStatus Status { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);

    Task<SyncEngineStatus> RefreshStatusAsync(CancellationToken cancellationToken = default);

    Task StopOwnedInstanceAsync(CancellationToken cancellationToken = default);
}
