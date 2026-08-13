using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class MultiRestoreFileViewModel : ObservableObject
{
    private bool _isSelected;
    private GitRestorePoint _restorePoint = GitRestorePoint.BeforeCommit;

    public MultiRestoreFileViewModel(GitCommitFileChange change)
    {
        Change = change;
    }

    public event EventHandler? CompositionChanged;

    public GitCommitFileChange Change { get; }
    public string Path => Change.Path;
    public GitChangeKind Kind => Change.Kind;
    public string ChangeSummary => Change.ChangeSummary;
    public bool IsLfsObject => Change.IsLfsObject;
    public IReadOnlyList<GitRestorePoint> RestorePoints { get; } = Enum.GetValues<GitRestorePoint>();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                CompositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public GitRestorePoint RestorePoint
    {
        get => _restorePoint;
        set
        {
            if (SetProperty(ref _restorePoint, value))
            {
                OnPropertyChanged(nameof(SourceLabel));
                CompositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string SourceLabel => RestorePoint == GitRestorePoint.BeforeCommit
        ? "Version before commit"
        : "Version at commit";
}

public sealed class CherryPickCommitViewModel : ObservableObject
{
    private bool _isSelected;

    public CherryPickCommitViewModel(GitBranchComparisonCommit comparisonCommit)
    {
        ComparisonCommit = comparisonCommit;
        _isSelected = comparisonCommit.CanCherryPick;
    }

    public event EventHandler? CompositionChanged;

    public GitBranchComparisonCommit ComparisonCommit { get; }
    public GitRevision Revision => ComparisonCommit.Revision;
    public string Hash => Revision.Hash;
    public string ShortHash => Revision.ShortHash;
    public string Subject => Revision.Subject;
    public string Author => Revision.AuthorName;
    public DateTimeOffset Date => Revision.AuthoredAt;
    public GitBranchCommitPresence Presence => ComparisonCommit.Presence;
    public string Side => ComparisonCommit.Side;
    public bool CanCherryPick => ComparisonCommit.CanCherryPick;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            bool next = CanCherryPick && value;
            if (SetProperty(ref _isSelected, next))
            {
                CompositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
