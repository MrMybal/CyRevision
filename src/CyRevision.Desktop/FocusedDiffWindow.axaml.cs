using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public enum DiffWindowSource
{
    History,
    WorkingTree,
    PullRequest,
    MultiRestore,
    CherryPick
}

public partial class FocusedDiffWindow : Window
{
    private UiLocalizer? _uiLocalizer;
    private LiveDiffWindowContent? _liveContent;

    public FocusedDiffWindow()
    {
        InitializeComponent();
    }

    public FocusedDiffWindow(MainWindowViewModel viewModel, LocalizationService localization) :
        this(viewModel, DiffWindowSource.History, localization)
    {
    }

    public FocusedDiffWindow(
        MainWindowViewModel viewModel,
        DiffWindowSource source,
        LocalizationService? localization = null) : this()
    {
        _liveContent = new LiveDiffWindowContent(viewModel, source);
        DataContext = _liveContent;
        if (localization is not null) _uiLocalizer = new UiLocalizer(this, localization);
        Closed += OnWindowClosed;
    }

    public FocusedDiffWindow(
        string title,
        string filePath,
        string diffText,
        LocalizationService localization) : this()
    {
        DataContext = new DiffWindowContent(title, filePath, diffText);
        _uiLocalizer = new UiLocalizer(this, localization);
        Closed += OnWindowClosed;
    }

    public FocusedDiffWindow(string title, string filePath, string diffText) : this()
    {
        DataContext = new DiffWindowContent(title, filePath, diffText);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _liveContent?.Dispose();
        _liveContent = null;
        _uiLocalizer?.Dispose();
        _uiLocalizer = null;
    }

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) =>
        Topmost = AlwaysOnTopToggle.IsChecked == true;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private sealed record DiffWindowContent(string WindowTitle, string FilePath, string DiffText);

    private sealed class LiveDiffWindowContent : INotifyPropertyChanged, IDisposable
    {
        private readonly MainWindowViewModel _source;
        private readonly DiffWindowSource _kind;
        private string _windowTitle = "Diff";
        private string _filePath = string.Empty;
        private string _diffText = "Select an item to inspect its diff.";
        private bool _isLoading;

        public LiveDiffWindowContent(MainWindowViewModel source, DiffWindowSource kind)
        {
            _source = source;
            _kind = kind;
            _source.PropertyChanged += OnSourcePropertyChanged;
            Refresh();
            if (_kind == DiffWindowSource.WorkingTree) _ = RefreshWorkingTreeAsync();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string WindowTitle
        {
            get => _windowTitle;
            private set => SetField(ref _windowTitle, value);
        }

        public string FilePath
        {
            get => _filePath;
            private set => SetField(ref _filePath, value);
        }

        public string DiffText
        {
            get => _diffText;
            private set => SetField(ref _diffText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetField(ref _isLoading, value);
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_kind == DiffWindowSource.WorkingTree && e.PropertyName == nameof(MainWindowViewModel.SelectedChange))
            {
                _ = RefreshWorkingTreeAsync();
                return;
            }
            Refresh();
        }

        private async Task RefreshWorkingTreeAsync()
        {
            await _source.LoadSelectedDiffForExternalAsync();
            Refresh();
        }

        private void Refresh()
        {
            switch (_kind)
            {
                case DiffWindowSource.History:
                    WindowTitle = "Commit diff";
                    FilePath = _source.SelectedExplorerFile?.Path ?? "Select a committed file";
                    DiffText = _source.ExplorerDiff;
                    IsLoading = _source.IsExplorerAnyLoading;
                    break;
                case DiffWindowSource.WorkingTree:
                    WindowTitle = "Working tree diff";
                    FilePath = _source.SelectedChange?.Path ?? "Select a changed file";
                    DiffText = _source.DiffText;
                    IsLoading = _source.IsWorkingTreeDiffLoading;
                    break;
                case DiffWindowSource.PullRequest:
                    WindowTitle = "Pull request diff";
                    FilePath = _source.SelectedPullRequestFile?.Path ?? "Select a pull-request file";
                    DiffText = string.IsNullOrWhiteSpace(_source.SelectedPullRequestFile?.Patch)
                        ? "The provider did not supply a text patch for this file."
                        : _source.SelectedPullRequestFile.Patch;
                    IsLoading = _source.IsPullRequestLoading;
                    break;
                case DiffWindowSource.MultiRestore:
                    WindowTitle = "Multi restore diff";
                    FilePath = _source.SelectedMultiRestoreFile?.Path ?? "Select a restore file";
                    DiffText = _source.MultiRestoreDiff;
                    IsLoading = _source.IsMultiRestoreDiffLoading;
                    break;
                case DiffWindowSource.CherryPick:
                    WindowTitle = "Cherry-pick diff";
                    FilePath = _source.SelectedCherryPickCommit?.Subject ?? "Select a commit";
                    DiffText = _source.CherryPickDiff;
                    IsLoading = _source.IsCherryPickDiffLoading;
                    break;
            }
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose() => _source.PropertyChanged -= OnSourcePropertyChanged;
    }
}
