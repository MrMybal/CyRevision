using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CyRevision.Desktop.Localization;

public sealed class UiLocalizer : IDisposable
{
    private readonly Window _window;
    private readonly LocalizationService _localization;
    private readonly Dictionary<PropertyTarget, LocalizedValue> _values = new();
    private bool _refreshQueued;
    private bool _disposed;

    public UiLocalizer(Window window, LocalizationService localization)
    {
        _window = window;
        _localization = localization;
        _window.LayoutUpdated += OnLayoutUpdated;
        _localization.LanguageChanged += OnLanguageChanged;
        QueueRefresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.LayoutUpdated -= OnLayoutUpdated;
        _localization.LanguageChanged -= OnLanguageChanged;
        _values.Clear();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => QueueRefresh();

    private void OnLanguageChanged(object? sender, EventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_refreshQueued || _disposed)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (!_disposed)
            {
                Refresh();
            }
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        LocalizeWindow(_window);
        foreach (Visual visual in _window.GetVisualDescendants())
        {
            switch (visual)
            {
                case TextBlock textBlock:
                    Localize(textBlock, TextBlock.TextProperty, textBlock.Text);
                    break;
                case TextBox textBox:
                    Localize(textBox, TextBox.PlaceholderTextProperty, textBox.PlaceholderText);
                    break;
                case ContentControl contentControl when contentControl.Content is string content:
                    Localize(contentControl, ContentControl.ContentProperty, content);
                    break;
            }

            if (visual is Control control && ToolTip.GetTip(control) is string tip)
            {
                LocalizeToolTip(control, tip);
            }

            if (visual is DataGrid dataGrid)
            {
                foreach (DataGridColumn column in dataGrid.Columns)
                {
                    if (column.Header is string header)
                    {
                        Localize(column, DataGridColumn.HeaderProperty, header);
                    }
                }
            }
        }
    }

    private void LocalizeWindow(Window window) => Localize(window, Window.TitleProperty, window.Title);

    private void Localize(AvaloniaObject target, AvaloniaProperty property, string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        PropertyTarget key = new(target, property);
        if (!_values.TryGetValue(key, out LocalizedValue? state))
        {
            state = new LocalizedValue(current, current);
            _values[key] = state;
        }
        else if (!string.Equals(current, state.Applied, StringComparison.Ordinal))
        {
            state.Source = current;
        }

        string translated = _localization.Translate(state.Source);
        state.Applied = translated;
        if (!string.Equals(current, translated, StringComparison.Ordinal))
        {
            target.SetCurrentValue(property, translated);
        }
    }

    private void LocalizeToolTip(Control target, string current)
    {
        PropertyTarget key = new(target, ToolTip.TipProperty);
        if (!_values.TryGetValue(key, out LocalizedValue? state))
        {
            state = new LocalizedValue(current, current);
            _values[key] = state;
        }
        else if (!string.Equals(current, state.Applied, StringComparison.Ordinal))
        {
            state.Source = current;
        }

        string translated = _localization.Translate(state.Source);
        state.Applied = translated;
        if (!string.Equals(current, translated, StringComparison.Ordinal))
        {
            ToolTip.SetTip(target, translated);
        }
    }

    private sealed class LocalizedValue(string source, string applied)
    {
        public string Source { get; set; } = source;

        public string Applied { get; set; } = applied;
    }

    private readonly record struct PropertyTarget(AvaloniaObject Target, AvaloniaProperty Property);
}
