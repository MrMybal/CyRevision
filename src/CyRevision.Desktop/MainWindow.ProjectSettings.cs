using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private async void OnSaveProjectDisplayNameClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SetSelectedProjectDisplayNameAsync();

    private async void OnNewProjectGroupClick(object? sender, RoutedEventArgs e)
    {
        string? groupName = await ShowNewProjectGroupDialogAsync();
        if (groupName is null) return;

        _viewModel.SidebarGroupDraft = groupName;
        await _viewModel.SetSelectedProjectSidebarGroupAsync();
    }

    private async Task<string?> ShowNewProjectGroupDialogAsync()
    {
        HashSet<string> existingGroups = new(
            _viewModel.ProjectSidebarGroupOptions,
            StringComparer.OrdinalIgnoreCase);
        TextBox groupName = new()
        {
            PlaceholderText = "Team, Unreal plugins, Client work...",
            MaxLength = 80,
            MinWidth = 390
        };
        TextBlock validation = new()
        {
            IsVisible = false,
            Foreground = Avalonia.Media.Brush.Parse("#E06C75"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        Button cancel = new()
        {
            Content = "Cancel",
            MinWidth = 88
        };
        Button create = new()
        {
            Content = "Create group",
            MinWidth = 112,
            IsEnabled = false
        };
        Window dialog = new()
        {
            Title = "New project group",
            Width = 500,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1E1F22")
        };

        groupName.TextChanged += (_, _) =>
        {
            create.IsEnabled = !string.IsNullOrWhiteSpace(groupName.Text);
            validation.IsVisible = false;
        };
        cancel.Click += (_, _) => dialog.Close(null);
        create.Click += (_, _) =>
        {
            string name = groupName.Text?.Trim() ?? string.Empty;
            if (name.Length == 0) return;
            if (existingGroups.Contains(name))
            {
                validation.Text = $"The group '{name}' already exists. Select it from the list instead.";
                validation.IsVisible = true;
                return;
            }

            dialog.Close(name);
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Create a sidebar group",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "The current project will be moved into this group. Existing groups remain available from the list.",
                    Classes = { "muted" },
                    TextWrapping = TextWrapping.Wrap
                },
                groupName,
                validation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, create }
                }
            }
        };

        dialog.Opened += (_, _) => groupName.Focus();
        return await dialog.ShowDialog<string?>(this);
    }
}