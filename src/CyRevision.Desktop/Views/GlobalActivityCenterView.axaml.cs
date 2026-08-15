using Avalonia.Controls;
using Avalonia.Interactivity;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Views;

public partial class GlobalActivityCenterView : UserControl
{
    public GlobalActivityCenterView() => InitializeComponent();

    private void OnClearHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel) viewModel.ClearRecentOperations();
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is Button { DataContext: OperationTaskViewModel task })
            viewModel.DismissRecentOperation(task);
    }

    private void OnDismissCenterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel) viewModel.DismissActivityCenter();
    }
}
