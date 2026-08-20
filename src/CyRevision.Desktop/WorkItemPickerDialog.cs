using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop;

internal sealed record WorkItemPickerResult(
    IReadOnlyList<WorkItemReference> WorkItems,
    bool PrefixPullRequestTitle);

internal sealed class WorkItemPickerDialog : Window
{
    private readonly Guid _projectId;
    private readonly Func<string, string> _translate;
    private readonly ComboBox _provider;
    private readonly TextBox _baseUrl;
    private readonly TextBlock _scopeLabel;
    private readonly TextBox _scope;
    private readonly TextBlock _accountLabel;
    private readonly TextBox _account;
    private readonly TextBox _tokenEnvironment;
    private readonly TextBox _sessionToken;
    private readonly TextBox _query;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly DataGrid _resultsGrid;
    private readonly ObservableCollection<WorkItemReference> _results = [];
    private readonly CheckBox _prefixPullRequestTitle;
    private readonly Button _searchButton;
    private readonly Button _testButton;
    private readonly Button _addButton;
    private CancellationTokenSource? _operationCancellation;

    public WorkItemPickerDialog(
        Guid projectId,
        IReadOnlyList<IWorkItemIntegrationPlugin> plugins,
        bool forPullRequest,
        Func<string, string> translate)
    {
        _projectId = projectId;
        _translate = translate;
        Title = translate(forPullRequest ? "Add task links to pull request" : "Add task links to commit");
        Width = 920;
        Height = 680;
        MinWidth = 720;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1F22"));

        _provider = new ComboBox
        {
            ItemsSource = plugins.Select(plugin => new ProviderChoice(plugin)).ToArray(),
            SelectedIndex = 0,
            MinWidth = 180
        };
        _baseUrl = new TextBox { PlaceholderText = "https://…" };
        _scopeLabel = Label("Scope");
        _scope = new TextBox();
        _accountLabel = Label("Account");
        _account = new TextBox();
        _tokenEnvironment = new TextBox();
        _sessionToken = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = translate("Optional session token — never saved")
        };
        _query = new TextBox
        {
            PlaceholderText = translate("Search by task key, title, status, list, or folder…")
        };
        _status = new TextBlock
        {
            Text = translate("Configure the selected provider, then test or search."),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#B8C1D8")),
            TextWrapping = TextWrapping.Wrap
        };
        _progress = new ProgressBar { Height = 3, IsIndeterminate = true, IsVisible = false };
        _resultsGrid = new DataGrid
        {
            ItemsSource = _results,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        _resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = translate("Task"),
            Binding = new Avalonia.Data.Binding(nameof(WorkItemReference.DisplayKey)),
            Width = new DataGridLength(125)
        });
        _resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = translate("Title"),
            Binding = new Avalonia.Data.Binding(nameof(WorkItemReference.Title)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = translate("Status"),
            Binding = new Avalonia.Data.Binding(nameof(WorkItemReference.Status)),
            Width = new DataGridLength(145)
        });
        _resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = translate("Provider"),
            Binding = new Avalonia.Data.Binding(nameof(WorkItemReference.ProviderName)),
            Width = new DataGridLength(95)
        });

        _prefixPullRequestTitle = new CheckBox
        {
            Content = translate("Prefix the pull request title with the first selected task key"),
            IsChecked = true,
            IsVisible = forPullRequest
        };
        _searchButton = Button(translate("Search tasks"), "#3574F0");
        _testButton = Button(translate("Test & save settings"), "#393B40");
        _addButton = Button(
            translate(forPullRequest ? "Add to pull request" : "Add to commit"),
            "#3574F0");
        Button selectAll = Button(translate("Select all results"), "#393B40");
        Button cancel = Button(translate("Cancel"), "#393B40");

        Grid connection = new()
        {
            ColumnDefinitions = new ColumnDefinitions("150,*,150,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 7
        };
        Add(connection, Label(translate("Provider")), 0, 0);
        Add(connection, _provider, 0, 1);
        Add(connection, Label(translate("API / site URL")), 0, 2);
        Add(connection, _baseUrl, 0, 3);
        Add(connection, _scopeLabel, 1, 0);
        Add(connection, _scope, 1, 1);
        Add(connection, _accountLabel, 1, 2);
        Add(connection, _account, 1, 3);
        Add(connection, Label(translate("Token environment")), 2, 0);
        Add(connection, _tokenEnvironment, 2, 1);
        Add(connection, Label(translate("Session token")), 2, 2);
        Add(connection, _sessionToken, 2, 3);

        Grid searchRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };
        searchRow.Children.Add(_query);
        Grid.SetColumn(_testButton, 1);
        searchRow.Children.Add(_testButton);
        Grid.SetColumn(_searchButton, 2);
        searchRow.Children.Add(_searchButton);

        StackPanel footerButtons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { selectAll, cancel, _addButton }
        };
        Grid root = new()
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 9
        };
        Border connectionCard = Card(connection);
        root.Children.Add(connectionCard);
        Grid.SetRow(searchRow, 1);
        root.Children.Add(searchRow);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        Grid.SetRow(_progress, 3);
        root.Children.Add(_progress);
        Border resultsCard = Card(_resultsGrid);
        Grid.SetRow(resultsCard, 4);
        root.Children.Add(resultsCard);
        Grid.SetRow(_prefixPullRequestTitle, 5);
        root.Children.Add(_prefixPullRequestTitle);
        Grid.SetRow(footerButtons, 6);
        root.Children.Add(footerButtons);
        Content = root;

        ToolTip.SetTip(_sessionToken, translate("Used for this window only. The value is never written to disk or Git."));
        ToolTip.SetTip(_tokenEnvironment, translate("Name of an environment variable that contains the API token."));
        ToolTip.SetTip(_resultsGrid, translate("Use Ctrl or Shift to select several tasks."));

        _provider.SelectionChanged += async (_, _) => await LoadProviderAsync();
        _testButton.Click += async (_, _) => await TestAsync();
        _searchButton.Click += async (_, _) => await SearchAsync();
        _query.KeyDown += async (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            args.Handled = true;
            await SearchAsync();
        };
        selectAll.Click += (_, _) => _resultsGrid.SelectAll();
        cancel.Click += (_, _) =>
        {
            _operationCancellation?.Cancel();
            Close(null);
        };
        _addButton.Click += (_, _) =>
        {
            WorkItemReference[] selected = _resultsGrid.SelectedItems
                .OfType<WorkItemReference>()
                .ToArray();
            if (selected.Length == 0 && _resultsGrid.SelectedItem is WorkItemReference single)
                selected = [single];
            if (selected.Length == 0)
            {
                _status.Text = _translate("Select at least one task.");
                return;
            }
            Close(new WorkItemPickerResult(selected, _prefixPullRequestTitle.IsChecked == true));
        };
        Opened += async (_, _) => await LoadProviderAsync();
        Closed += (_, _) => _operationCancellation?.Cancel();
    }

    private ProviderChoice? SelectedProvider => _provider.SelectedItem as ProviderChoice;

    private async Task LoadProviderAsync()
    {
        ProviderChoice? choice = SelectedProvider;
        if (choice is null) return;
        await RunAsync(async cancellationToken =>
        {
            WorkItemConnectionSettings settings = await choice.Plugin.LoadConnectionAsync(_projectId, cancellationToken);
            _baseUrl.Text = settings.BaseUrl;
            _scope.Text = settings.ScopeId;
            _account.Text = settings.AccountName;
            _tokenEnvironment.Text = settings.TokenEnvironmentVariable;
            WorkItemProviderDescriptor provider = choice.Plugin.Provider;
            _scopeLabel.Text = _translate(provider.ScopeLabel);
            _scope.PlaceholderText = _translate(provider.ScopePlaceholder);
            _accountLabel.Text = _translate(provider.AccountLabel);
            _account.PlaceholderText = _translate(provider.AccountPlaceholder);
            _status.Text = _translate(provider.Description);
            _results.Clear();
        });
    }

    private async Task TestAsync()
    {
        ProviderChoice? choice = SelectedProvider;
        if (choice is null) return;
        await RunAsync(async cancellationToken =>
        {
            WorkItemConnectionSettings settings = ReadSettings();
            await choice.Plugin.SaveConnectionAsync(settings, cancellationToken);
            WorkItemConnectionTestResult result = await choice.Plugin.TestConnectionAsync(
                settings,
                _sessionToken.Text,
                cancellationToken);
            _status.Text = result.Message;
        });
    }

    private async Task SearchAsync()
    {
        ProviderChoice? choice = SelectedProvider;
        if (choice is null) return;
        await RunAsync(async cancellationToken =>
        {
            WorkItemConnectionSettings settings = ReadSettings();
            await choice.Plugin.SaveConnectionAsync(settings, cancellationToken);
            IReadOnlyList<WorkItemReference> results = await choice.Plugin.SearchAsync(
                settings,
                _sessionToken.Text,
                _query.Text ?? string.Empty,
                100,
                cancellationToken);
            _results.Clear();
            foreach (WorkItemReference item in results) _results.Add(item);
            _status.Text = results.Count == 0
                ? _translate("No task matched this search.")
                : $"{results.Count:N0} {_translate("task(s) found. Use Ctrl or Shift for multiple selection.")}";
        });
    }

    private WorkItemConnectionSettings ReadSettings() => new(
        _projectId,
        _baseUrl.Text?.Trim() ?? string.Empty,
        _scope.Text?.Trim() ?? string.Empty,
        _account.Text?.Trim() ?? string.Empty,
        _tokenEnvironment.Text?.Trim() ?? string.Empty);

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        _progress.IsVisible = true;
        _provider.IsEnabled = _baseUrl.IsEnabled = _scope.IsEnabled = _account.IsEnabled = false;
        _tokenEnvironment.IsEnabled = _sessionToken.IsEnabled = _query.IsEnabled = false;
        _searchButton.IsEnabled = _testButton.IsEnabled = _addButton.IsEnabled = false;
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
                _status.Text = _translate("Operation cancelled.");
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
                _status.Text = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _progress.IsVisible = false;
                _provider.IsEnabled = _baseUrl.IsEnabled = _scope.IsEnabled = _account.IsEnabled = true;
                _tokenEnvironment.IsEnabled = _sessionToken.IsEnabled = _query.IsEnabled = true;
                _searchButton.IsEnabled = _testButton.IsEnabled = _addButton.IsEnabled = true;
            }
        }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.Parse("#B8C1D8"))
    };

    private static Button Button(string text, string color) => new()
    {
        Content = text,
        Padding = new Thickness(13, 7),
        Background = new SolidColorBrush(Color.Parse(color)),
        Foreground = Brushes.White
    };

    private static Border Card(Control content) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#25272B")),
        BorderBrush = new SolidColorBrush(Color.Parse("#393B40")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(10),
        Child = content
    };

    private static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private sealed record ProviderChoice(IWorkItemIntegrationPlugin Plugin)
    {
        public override string ToString() => Plugin.Provider.Name;
    }
}
