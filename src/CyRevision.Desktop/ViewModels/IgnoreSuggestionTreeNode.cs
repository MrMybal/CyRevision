using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CyRevision.Desktop.ViewModels;

public sealed class IgnoreSuggestionTreeNode : ObservableObject
{
    private readonly IgnoreSuggestionViewModel? _suggestion;

    public IgnoreSuggestionTreeNode(string name, string relativePath, IgnoreSuggestionViewModel? suggestion)
    {
        Name = name;
        RelativePath = relativePath;
        _suggestion = suggestion;
        if (_suggestion is not null) _suggestion.PropertyChanged += OnSuggestionPropertyChanged;
    }

    public string Name { get; }

    public string RelativePath { get; }

    public ObservableCollection<IgnoreSuggestionTreeNode> Children { get; } = [];

    public bool IsSelected
    {
        get => _suggestion?.IsSelected == true;
        set
        {
            if (_suggestion is null || _suggestion.IsSelected == value) return;
            _suggestion.IsSelected = value;
            OnPropertyChanged();
        }
    }

    public string CountText => _suggestion?.CountText ?? string.Empty;

    private void OnSuggestionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IgnoreSuggestionViewModel.IsSelected))
            OnPropertyChanged(nameof(IsSelected));
    }
}
