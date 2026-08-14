namespace CyRevision.Desktop.ViewModels;

public sealed class IgnoreSuggestionViewModel : ObservableObject
{
    private bool _isSelected;

    public IgnoreSuggestionViewModel(string name, string pattern, int fileCount, string kind)
    {
        Name = name;
        Pattern = pattern;
        FileCount = fileCount;
        Kind = kind;
    }

    public string Name { get; }

    public string Pattern { get; }

    public int FileCount { get; }

    public string Kind { get; }

    public string Detail => $"{FileCount:N0} file(s) · {Pattern}";

    public string CountText => $"{FileCount:N0}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
