using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyRevision.Desktop;

/// <summary>
/// Hosts the real workspace control in another window and restores it to the source tab on
/// close. This keeps every selection, loader and command synchronized without maintaining a
/// second copy of large workspace views.
/// </summary>
public sealed class DetachedTabWindow : Window
{
    private readonly TabItem _source;
    private readonly object _workspaceContent;
    private readonly ContentControl _host = new();
    private bool _restored;

    public DetachedTabWindow(TabItem source, string title)
    {
        _source = source;
        DataContext = source.DataContext;
        _workspaceContent = source.Content
            ?? throw new InvalidOperationException("This workspace is already detached.");
        source.Content = CreatePlaceholder(title);

        Title = $"CyRevision — {title}";
        Width = 1500;
        Height = 900;
        MinWidth = 780;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Grid root = new() { RowDefinitions = new RowDefinitions("34,*") };
        Border header = new()
        {
            Background = new SolidColorBrush(Color.Parse("#242529")),
            BorderBrush = new SolidColorBrush(Color.Parse("#393B40")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(10, 4)
        };
        Grid headerGrid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        headerGrid.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        CheckBox topmost = new() { Content = "Always on top", VerticalAlignment = VerticalAlignment.Center };
        topmost.Click += (_, _) => Topmost = topmost.IsChecked == true;
        Grid.SetColumn(topmost, 1);
        headerGrid.Children.Add(topmost);
        Button close = new() { Content = "Close", Padding = new Avalonia.Thickness(9, 2) };
        close.Click += OnCloseClick;
        Grid.SetColumn(close, 2);
        headerGrid.Children.Add(close);
        header.Child = headerGrid;
        root.Children.Add(header);

        _host.Content = _workspaceContent;
        Grid.SetRow(_host, 1);
        root.Children.Add(_host);
        Content = root;
        Closed += (_, _) => Restore();
    }

    public TabItem Source => _source;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        _host.Content = null;
        _source.Content = _workspaceContent;
    }

    private static Control CreatePlaceholder(string title) => new Border
    {
        Margin = new Avalonia.Thickness(0, 8, 0, 0),
        Background = new SolidColorBrush(Color.Parse("#202225")),
        BorderBrush = new SolidColorBrush(Color.Parse("#393B40")),
        BorderThickness = new Avalonia.Thickness(1),
        CornerRadius = new Avalonia.CornerRadius(6),
        Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"{title} is open in an external window.", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Close the external window to restore it here.", Foreground = new SolidColorBrush(Color.Parse("#8F98A8")) }
            }
        }
    };
}
