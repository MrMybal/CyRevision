using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        { OnOpenCurrentPageDocumentationClick(null, new RoutedEventArgs()); e.Handled = true; }
    }

    private void OnCyDocRequested(object? sender, string id) => OpenCyDoc(id);

    private void OpenCyDoc(string id)
    {
        try
        {
            string url = _viewModel.GetNativeDocumentationLink(id);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { _viewModel.PublishApplicationError("Documentation unavailable", error.Message); }
    }
}
