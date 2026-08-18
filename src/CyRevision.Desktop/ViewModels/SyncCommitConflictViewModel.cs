using CyRevision.Sync;

namespace CyRevision.Desktop.ViewModels;

public sealed class SyncCommitConflictViewModel : ObservableObject
{
    private SyncCommitConflictChoice _choice;

    public SyncCommitConflictViewModel(SyncCommitConflict conflict) => Conflict = conflict;

    public SyncCommitConflict Conflict { get; }
    public string Path => Conflict.Path;
    public string State => Conflict.State;

    public SyncCommitConflictChoice Choice
    {
        get => _choice;
        set
        {
            if (!SetProperty(ref _choice, value)) return;
            OnPropertyChanged(nameof(ChoiceText));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ChoiceText => Choice switch
    {
        SyncCommitConflictChoice.KeepLocal => "Keep local",
        SyncCommitConflictChoice.UseIncoming => "Use incoming",
        _ => "Unresolved"
    };

    public event EventHandler? Changed;
}
