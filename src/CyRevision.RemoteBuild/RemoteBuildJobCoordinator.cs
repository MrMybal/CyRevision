using System.Collections.Concurrent;

namespace CyRevision.RemoteBuild;

public sealed class RemoteBuildJobCoordinator : IAsyncDisposable
{
    private readonly string _jobsRoot;
    private readonly RemoteBuildJobRunner _runner;
    private readonly SemaphoreSlim _capacity;
    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();

    public RemoteBuildJobCoordinator(string jobsRoot, int maximumParallelJobs, RemoteBuildJobRunner? runner = null)
    {
        _jobsRoot = Path.GetFullPath(jobsRoot);
        Directory.CreateDirectory(_jobsRoot);
        _capacity = new SemaphoreSlim(maximumParallelJobs, maximumParallelJobs);
        _runner = runner ?? new RemoteBuildJobRunner();
    }

    public RemoteBuildJobStatus Start(
        RemoteBuildAgentProject project,
        RemoteBuildRecipe recipe,
        RemoteBuildSourceMode sourceMode,
        string? snapshotPath)
    {
        Guid jobId = Guid.NewGuid();
        JobEntry entry = new(new RemoteBuildJobStatus(
            jobId, project.ProjectId, recipe.Id, sourceMode, RemoteBuildJobState.Queued,
            DateTimeOffset.UtcNow, null, null, null, "Queued on the remote build agent.", string.Empty, false),
            new CancellationTokenSource());
        if (!_jobs.TryAdd(jobId, entry))
            throw new InvalidOperationException("Unable to allocate a remote build job.");
        entry.Execution = Task.Run(() => ExecuteAsync(entry, project, recipe, sourceMode, snapshotPath));
        return entry.Status;
    }

    public RemoteBuildJobStatus? Get(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out JobEntry? entry))
            return null;
        lock (entry.Gate)
            return entry.Status with { LogTail = ReadLogTail(jobId) };
    }

    public string? GetArtifactPath(Guid jobId)
    {
        RemoteBuildJobStatus? status = Get(jobId);
        if (status?.State != RemoteBuildJobState.Succeeded || !status.HasArtifacts)
            return null;
        string path = Path.Combine(_jobsRoot, jobId.ToString("N"), "artifacts.zip");
        return File.Exists(path) ? path : null;
    }

    public bool Cancel(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out JobEntry? entry))
            return false;
        entry.Cancellation.Cancel();
        return true;
    }

    public int RunningCount => _jobs.Values.Count(entry => entry.Status.State is
        RemoteBuildJobState.Queued or RemoteBuildJobState.Preparing or RemoteBuildJobState.Running or RemoteBuildJobState.Packaging);

    public async ValueTask DisposeAsync()
    {
        foreach (JobEntry job in _jobs.Values)
            job.Cancellation.Cancel();
        await Task.WhenAll(_jobs.Values.Select(job => job.Execution ?? Task.CompletedTask));
        foreach (JobEntry job in _jobs.Values)
            job.Cancellation.Dispose();
        _capacity.Dispose();
    }

    private async Task ExecuteAsync(
        JobEntry entry,
        RemoteBuildAgentProject project,
        RemoteBuildRecipe recipe,
        RemoteBuildSourceMode sourceMode,
        string? snapshotPath)
    {
        bool capacityHeld = false;
        try
        {
            await _capacity.WaitAsync(entry.Cancellation.Token);
            capacityHeld = true;
            Update(entry, RemoteBuildJobState.Preparing, "Remote build started.", started: true);
            RemoteBuildExecutionResult result = await _runner.RunAsync(
                entry.Status.JobId, project, recipe, sourceMode, snapshotPath, _jobsRoot,
                (state, message) => Update(entry, state, message), entry.Cancellation.Token);
            Update(entry, result.Succeeded ? RemoteBuildJobState.Succeeded : RemoteBuildJobState.Failed,
                result.Message, completed: true, exitCode: result.ExitCode,
                hasArtifacts: result.Succeeded && File.Exists(result.ArtifactPath));
        }
        catch (OperationCanceledException)
        {
            Update(entry, RemoteBuildJobState.Cancelled, "Remote build cancelled.", completed: true);
        }
        catch (Exception exception)
        {
            Update(entry, RemoteBuildJobState.Failed, exception.Message, completed: true);
        }
        finally
        {
            if (capacityHeld)
                _capacity.Release();
            if (!string.IsNullOrWhiteSpace(snapshotPath))
                File.Delete(snapshotPath);
        }
    }

    private static void Update(
        JobEntry entry,
        RemoteBuildJobState state,
        string message,
        bool started = false,
        bool completed = false,
        int? exitCode = null,
        bool hasArtifacts = false)
    {
        lock (entry.Gate)
            entry.Status = entry.Status with
            {
                State = state,
                Message = message,
                StartedAt = started ? DateTimeOffset.UtcNow : entry.Status.StartedAt,
                CompletedAt = completed ? DateTimeOffset.UtcNow : entry.Status.CompletedAt,
                ExitCode = exitCode ?? entry.Status.ExitCode,
                HasArtifacts = hasArtifacts || entry.Status.HasArtifacts
            };
    }

    private string ReadLogTail(Guid jobId)
    {
        string path = Path.Combine(_jobsRoot, jobId.ToString("N"), "build.log");
        if (!File.Exists(path))
            return string.Empty;
        try
        {
            string[] lines = File.ReadAllLines(path);
            return string.Join(Environment.NewLine, lines.TakeLast(200));
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private sealed class JobEntry(RemoteBuildJobStatus status, CancellationTokenSource cancellation)
    {
        public object Gate { get; } = new();
        public RemoteBuildJobStatus Status { get; set; } = status;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? Execution { get; set; }
    }
}
