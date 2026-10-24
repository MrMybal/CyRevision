namespace CyRevision.Desktop.Workspace;

/// <summary>
/// Keeps heavyweight workspace tabs lazy and single-flight per project. A failed or
/// cancelled load is intentionally retryable; only a completed lease is cached.
/// </summary>
public sealed class WorkspaceLoadCoordinator
{
    private readonly object _gate = new();
    private readonly HashSet<WorkspaceLoadKey> _loaded = [];
    private readonly Dictionary<WorkspaceLoadKey, object> _loading = [];

    public WorkspaceLoadLease? TryBegin(Guid projectId, string workspaceName)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project id is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(workspaceName))
            throw new ArgumentException("A workspace name is required.", nameof(workspaceName));

        WorkspaceLoadKey key = new(projectId, workspaceName.Trim());
        object identity = new();
        lock (_gate)
        {
            if (_loaded.Contains(key) || !_loading.TryAdd(key, identity)) return null;
        }
        return new WorkspaceLoadLease(this, key, identity);
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
            foreach (WorkspaceLoadKey key in _loading.Keys.Where(key => key.ProjectId == projectId).ToArray())
                _loading.Remove(key);
        }
    }

    public void Invalidate(Guid projectId, string workspaceName)
    {
        WorkspaceLoadKey key = new(projectId, workspaceName.Trim());
        lock (_gate)
        {
            _loaded.Remove(key);
            _loading.Remove(key);
        }
    }

    private void End(WorkspaceLoadKey key, object identity, bool completed)
    {
        lock (_gate)
        {
            if (!_loading.TryGetValue(key, out object? active) || !ReferenceEquals(active, identity)) return;
            _loading.Remove(key);
            if (completed) _loaded.Add(key);
        }
    }

    internal readonly record struct WorkspaceLoadKey(Guid ProjectId, string WorkspaceName);

    public sealed class WorkspaceLoadLease : IDisposable
    {
        private WorkspaceLoadCoordinator? _owner;
        private readonly WorkspaceLoadKey _key;
        private readonly object _identity;
        private bool _completed;

        internal WorkspaceLoadLease(WorkspaceLoadCoordinator owner, WorkspaceLoadKey key, object identity)
        {
            _owner = owner;
            _key = key;
            _identity = identity;
        }

        public void Complete() => _completed = true;

        public void Dispose()
        {
            WorkspaceLoadCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            owner?.End(_key, _identity, _completed);
        }
    }
}
