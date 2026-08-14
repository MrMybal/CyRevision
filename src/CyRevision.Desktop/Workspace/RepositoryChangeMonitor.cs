namespace CyRevision.Desktop.Workspace;

internal sealed record RepositoryChangeBatch(
    IReadOnlyList<string> Paths,
    bool RequiresUntrackedScan,
    bool GitMetadataChanged,
    bool WatcherOverflowed);

internal sealed class RepositoryChangeMonitor : IDisposable
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Binaries", "Intermediate", "Saved", "DerivedDataCache", "node_modules", "BuildToolsOutput",
        ".vs", ".idea", ".cyrevision", "bin", "obj"
    };

    private readonly Action<RepositoryChangeBatch> _onChanges;
    private readonly TimeSpan _debounceDelay;
    private readonly object _sync = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCancellation;
    private string? _rootPath;
    private bool _requiresUntrackedScan;
    private bool _gitMetadataChanged;
    private bool _watcherOverflowed;

    public RepositoryChangeMonitor(Action<RepositoryChangeBatch> onChanges, TimeSpan? debounceDelay = null)
    {
        _onChanges = onChanges;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(800);
    }

    public void Start(string rootPath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        lock (_sync)
        {
            if (_watcher is not null && PathsEqual(_rootPath, root)) return;
            StopCore();
            if (!Directory.Exists(root)) return;

            FileSystemWatcher watcher = new(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 32 * 1024
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnCreatedOrDeleted;
            watcher.Deleted += OnCreatedOrDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            _rootPath = root;
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
    }

    public void Stop()
    {
        lock (_sync) StopCore();
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => QueuePath(args.FullPath, false);

    private void OnCreatedOrDeleted(object sender, FileSystemEventArgs args) => QueuePath(args.FullPath, true);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        QueuePath(args.OldFullPath, true);
        QueuePath(args.FullPath, true);
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        lock (_sync)
        {
            _watcherOverflowed = true;
            _requiresUntrackedScan = true;
            ScheduleFlushCore();
        }
    }

    private void QueuePath(string fullPath, bool requiresUntrackedScan)
    {
        lock (_sync)
        {
            if (_rootPath is null) return;
            string relativePath;
            try
            {
                relativePath = Path.GetRelativePath(_rootPath, fullPath).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return;
            }

            if (relativePath.StartsWith("../", StringComparison.Ordinal) || ShouldIgnore(relativePath)) return;
            bool gitMetadata = IsRelevantGitMetadata(relativePath);
            if (relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) && !gitMetadata) return;

            _pendingPaths.Add(relativePath);
            _gitMetadataChanged |= gitMetadata;
            _requiresUntrackedScan |= requiresUntrackedScan || gitMetadata;
            ScheduleFlushCore();
        }
    }

    private void ScheduleFlushCore()
    {
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _debounceCancellation = cancellation;
        _ = FlushAfterDelayAsync(cancellation);
    }

    private async Task FlushAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellation.Token).ConfigureAwait(false);
            RepositoryChangeBatch batch;
            lock (_sync)
            {
                if (!ReferenceEquals(_debounceCancellation, cancellation)) return;
                batch = new RepositoryChangeBatch(
                    _pendingPaths.ToArray(),
                    _requiresUntrackedScan,
                    _gitMetadataChanged,
                    _watcherOverflowed);
                _pendingPaths.Clear();
                _requiresUntrackedScan = false;
                _gitMetadataChanged = false;
                _watcherOverflowed = false;
                _debounceCancellation = null;
            }
            if (batch.Paths.Count > 0 || batch.WatcherOverflowed) _onChanges(batch);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_debounceCancellation, cancellation)) _debounceCancellation = null;
            cancellation.Dispose();
        }
    }

    private static bool ShouldIgnore(string relativePath)
    {
        string[] parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => ExcludedDirectories.Contains(part));
    }

    private static bool IsRelevantGitMetadata(string relativePath)
    {
        if (!relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) return false;
        string metadata = relativePath[5..];
        return metadata.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
               metadata.Equals("index", StringComparison.OrdinalIgnoreCase) ||
               metadata.Equals("index.lock", StringComparison.OrdinalIgnoreCase) ||
               metadata.Equals("packed-refs", StringComparison.OrdinalIgnoreCase) ||
               metadata.Equals("FETCH_HEAD", StringComparison.OrdinalIgnoreCase) ||
               metadata.Equals("ORIG_HEAD", StringComparison.OrdinalIgnoreCase) ||
               metadata.StartsWith("refs/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? first, string second) => first is not null && string.Equals(
        first,
        second,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void StopCore()
    {
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _debounceCancellation = null;
        _pendingPaths.Clear();
        _requiresUntrackedScan = false;
        _gitMetadataChanged = false;
        _watcherOverflowed = false;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnCreatedOrDeleted;
            _watcher.Deleted -= OnCreatedOrDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }
        _rootPath = null;
    }

    public void Dispose() => Stop();
}
