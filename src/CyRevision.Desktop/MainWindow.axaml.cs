using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CyRevision.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnCreateProjectClick(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sélectionnez un profil de projet pour commencer.";
    }

    private void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        string preset = sender is Button button ? button.Content?.ToString() ?? "Projet" : "Projet";
        StatusText.Text = $"{preset} — assistant de configuration prêt pour la prochaine étape.";
    }
}

