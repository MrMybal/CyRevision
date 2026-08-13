using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitChangeViewModel : ObservableObject
{
    private bool _isIncluded;
    private bool _isLocalOnly;

    public GitChangeViewModel(
        GitChange change,
        bool isIncluded = true,
        bool isLocalOnly = false,
        LfsFileLock? fileLock = null)
    {
        Change = change;
        _isIncluded = isIncluded && !isLocalOnly;
        _isLocalOnly = isLocalOnly && change.Kind == GitChangeKind.Untracked;
        FileLock = fileLock;
    }

    public event EventHandler? PreparationChanged;

    public GitChange Change { get; }

    public LfsFileLock? FileLock { get; }

    public string Path => Change.Path.Replace('\\', '/');

    public string FileName => System.IO.Path.GetFileName(Path);

    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') is { Length: > 0 } directory
        ? directory
        : "/";

    public string State => Change.Kind switch
    {
        GitChangeKind.Added => "Added",
        GitChangeKind.Modified => "Modified",
        GitChangeKind.Deleted => "Deleted",
        GitChangeKind.Renamed => "Renamed",
        GitChangeKind.Untracked => "Untracked",
        GitChangeKind.Conflicted => "Conflict",
        _ => Change.Kind.ToString()
    };

    public string Area => Change.IsStaged ? "Staged" : "Working tree";

    public string Lfs => Change.IsLfsObject ? "LFS" : string.Empty;

    public bool IsUntracked => Change.Kind == GitChangeKind.Untracked;

    public bool IsTracked => !IsUntracked;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            bool next = value && !IsLocalOnly;
            if (SetProperty(ref _isIncluded, next))
            {
                OnPropertyChanged(nameof(PreparationState));
                OnPropertyChanged(nameof(PreparationColor));
                PreparationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsLocalOnly
    {
        get => _isLocalOnly;
        set
        {
            bool next = value && IsUntracked;
            if (!SetProperty(ref _isLocalOnly, next))
            {
                return;
            }

            if (next && _isIncluded)
            {
                _isIncluded = false;
                OnPropertyChanged(nameof(IsIncluded));
            }

            OnPropertyChanged(nameof(PreparationState));
            OnPropertyChanged(nameof(PreparationColor));
            PreparationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string PreparationState => IsLocalOnly ? "Local" : IsIncluded ? "Commit" : "Keep";

    public string PreparationColor => IsLocalOnly ? "#7E8799" : IsIncluded ? "#78D7B7" : "#E5C07B";

    public bool HasLock => FileLock is not null;

    public bool HasForeignLock => FileLock is { IsOurs: false };

    public string LockOwner => FileLock is null
        ? string.Empty
        : FileLock.IsOurs ? "Locked by you" : $"Locked by {FileLock.OwnerName}";

    public string LockShort => FileLock is null
        ? string.Empty
        : FileLock.IsOurs ? "YOU" : FileLock.OwnerName;

    public string LockColor => FileLock?.IsOurs == true ? "#78D7B7" : "#E5C07B";

    public string StatusColor => Change.Kind switch
    {
        GitChangeKind.Added => "#78D7B7",
        GitChangeKind.Modified => "#61AFEF",
        GitChangeKind.Deleted => "#E06C75",
        GitChangeKind.Renamed => "#C678DD",
        GitChangeKind.Untracked => "#A9ABB2",
        GitChangeKind.Conflicted => "#FF7B72",
        _ => "#A9ABB2"
    };
}
