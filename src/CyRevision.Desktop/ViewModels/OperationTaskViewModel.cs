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

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value)) OnPropertyChanged(nameof(IsRunning));
        }
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}
