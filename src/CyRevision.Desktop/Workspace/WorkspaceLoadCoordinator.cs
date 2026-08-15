namespace CyRevision.Desktop.Workspace;

/// <summary>
/// Keeps heavyweight workspace tabs lazy and single-flight per project. A failed or
/// cancelled load is intentionally retryable; only a completed lease is cached.
/// </summary>
public sealed class WorkspaceLoadCoordinator
{
    private readonly object _gate = new();
    private readonly HashSet<WorkspaceLoadKey> _loaded = [];
    private readonly HashSet<WorkspaceLoadKey> _loading = [];

    public WorkspaceLoadLease? TryBegin(Guid projectId, string workspaceName)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project id is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(workspaceName))
            throw new ArgumentException("A workspace name is required.", nameof(workspaceName));

        WorkspaceLoadKey key = new(projectId, workspaceName.Trim());
        lock (_gate)
        {
            if (_loaded.Contains(key) || !_loading.Add(key)) return null;
        }
        return new WorkspaceLoadLease(this, key);
    }

    public bool IsLoaded(Guid projectId, string workspaceName)
    {
        WorkspaceLoadKey key = new(projectId, workspaceName.Trim());
        lock (_gate) return _loaded.Contains(key);
    }

    public void InvalidateProject(Guid projectId)
    {
        lock (_gate)
        {
            _loaded.RemoveWhere(key => key.ProjectId == projectId);
            _loading.RemoveWhere(key => key.ProjectId == projectId);
        }
    }

    public void Invalidate(Guid projectId, string workspaceName)
    {
        WorkspaceLoadKey key = new(projectId, workspaceName.Trim());
        lock (_gate) _loaded.Remove(key);
    }

    private void End(WorkspaceLoadKey key, bool completed)
    {
        lock (_gate)
        {
            _loading.Remove(key);
            if (completed) _loaded.Add(key);
        }
    }

    internal readonly record struct WorkspaceLoadKey(Guid ProjectId, string WorkspaceName);

    public sealed class WorkspaceLoadLease : IDisposable
    {
        private WorkspaceLoadCoordinator? _owner;
        private readonly WorkspaceLoadKey _key;
        private bool _completed;

        internal WorkspaceLoadLease(WorkspaceLoadCoordinator owner, WorkspaceLoadKey key)
        {
            _owner = owner;
            _key = key;
        }

        public void Complete() => _completed = true;

        public void Dispose()
        {
            WorkspaceLoadCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            owner?.End(_key, _completed);
        }
    }
}
