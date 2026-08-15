using Avalonia.Media;

namespace CyRevision.Desktop.ViewModels;

public sealed class OperationTaskViewModel : ObservableObject
{
    private string _state = "Running";
    private string _detail;

    public OperationTaskViewModel(string title, string projectName, string detail = "")
    {
        Id = Guid.NewGuid();
        Title = title;
        ProjectName = projectName;
        _detail = detail;
        StartedAt = DateTimeOffset.Now;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string ProjectName { get; }
    public DateTimeOffset StartedAt { get; }
    public string StartedText => StartedAt.ToString("HH:mm:ss");
    public bool IsRunning => string.Equals(State, "Running", StringComparison.Ordinal);
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool IsAttention => State is "Failed" or "Cancelled";
    public string DurationText => ((CompletedAt ?? DateTimeOffset.Now) - StartedAt).TotalSeconds < 1
        ? "<1 s"
        : ((CompletedAt ?? DateTimeOffset.Now) - StartedAt).TotalMinutes < 1
            ? $"{((CompletedAt ?? DateTimeOffset.Now) - StartedAt).TotalSeconds:N0} s"
            : $"{((CompletedAt ?? DateTimeOffset.Now) - StartedAt).TotalMinutes:N1} min";
    public IBrush StateBrush => State switch
    {
        "Failed" => Brush.Parse("#FF9BAB"),
        "Cancelled" or "Queued" => Brush.Parse("#F2C66D"),
        "Completed" => Brush.Parse("#78D7B7"),
        _ => Brush.Parse("#61AFEF")
    };

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsAttention));
                OnPropertyChanged(nameof(StateBrush));
            }
        }
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public void Complete(string state, string detail)
    {
        State = state;
        Detail = detail;
        CompletedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(CompletedAt));
        OnPropertyChanged(nameof(DurationText));
    }
}
