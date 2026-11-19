using Avalonia.Controls;
using Avalonia.Interactivity;
using CyRevision.Desktop.ViewModels;
namespace CyRevision.Desktop.Controls;
public sealed partial class CyMcpAssistantView : UserControl
{
    public CyMcpAssistantView() => InitializeComponent();
    private async void Run(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not MainWindowViewModel model || sender is not Button { Tag: string action }) return;
        if (model.IsAiSdkBusy && action is not ("ApproveAiSdkOperation" or "RejectAiSdkOperation" or "CancelAiSdkTurn")) return;
        if (typeof(MainWindowViewModel).GetMethod(action, Type.EmptyTypes)?.Invoke(model, null) is Task task) await task;
    }
}
