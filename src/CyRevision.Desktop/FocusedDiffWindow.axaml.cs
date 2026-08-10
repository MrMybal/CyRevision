using Avalonia.Controls;
using Avalonia.Interactivity;
using CyRevision.Desktop.Localization;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class FocusedDiffWindow : Window
{
    private UiLocalizer? _uiLocalizer;

    public FocusedDiffWindow()
    {
        InitializeComponent();
    }

    public FocusedDiffWindow(MainWindowViewModel viewModel, LocalizationService localization) : this()
    {
        DataContext = viewModel;
        _uiLocalizer = new UiLocalizer(this, localization);
        Closed += (_, _) => _uiLocalizer?.Dispose();
    }

    private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) =>
        Topmost = AlwaysOnTopToggle.IsChecked == true;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
