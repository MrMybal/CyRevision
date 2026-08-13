using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CyRevision.Desktop.Controls;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public enum DetachedWorkspaceSection
{
    History,
    Code,
    MultiRestore,
    CherryPick
}

public partial class DetachedWorkspaceWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;
    private UiLocalizer? _uiLocalizer;

    public DetachedWorkspaceWindow()
    {
        InitializeComponent();
    }

    public DetachedWorkspaceWindow(
        MainWindowViewModel viewModel,
        LocalizationService localization,
        DetachedWorkspaceSection section) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _uiLocalizer = new UiLocalizer(this, localization);
        SelectSection(section);
        Closed += (_, _) => _uiLocalizer?.Dispose();
    }

    private void SelectSection(DetachedWorkspaceSection section)
    {
        switch (section)
        {
            case DetachedWorkspaceSection.History:
                DetachedTabs.SelectedItem = DetachedHistoryTab;
                break;
            case DetachedWorkspaceSection.Code:
                DetachedTabs.SelectedItem = DetachedCodeTab;
                break;
            case DetachedWorkspaceSection.MultiRestore:
                DetachedTabs.SelectedItem = DetachedCompositionTab;
                DetachedCompositionView.SelectSection(RevisionCompositionSection.MultiRestore);
                break;
            case DetachedWorkspaceSection.CherryPick:
                DetachedTabs.SelectedItem = DetachedCompositionTab;
                DetachedCompositionView.SelectSection(RevisionCompositionSection.CherryPick);
                break;
        }
    }

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) => Topmost = AlwaysOnTopToggle.IsChecked == true;
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private async void OnCompareHistoryClick(object? sender, RoutedEventArgs e) =>
        await (_viewModel?.CompareExplorerCommitsAsync() ?? Task.CompletedTask);
    private async void OnCodeSearchClick(object? sender, RoutedEventArgs e) =>
        await (_viewModel?.SearchCodeAsync() ?? Task.CompletedTask);
    private async void OnRefreshCodeClick(object? sender, RoutedEventArgs e) =>
        await (_viewModel?.RefreshCodeWorkspaceAsync() ?? Task.CompletedTask);

    private async void OnCodeSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true;
        await (_viewModel?.SearchCodeAsync() ?? Task.CompletedTask);
    }
}
