using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CyRevision.Desktop.Controls;

internal sealed class GraphViewport : UserControl
{
    private const double MinimumZoom = 0.3;
    private const double MaximumZoom = 2.5;
    private readonly Control _graph;
    private readonly LayoutTransformControl _layoutTransform;
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBlock _zoomLabel;
    private bool _isPanning;
    private Point _panStart;
    private Vector _panStartOffset;
    private double _zoom = 1;

    public GraphViewport(Control graph)
    {
        _graph = graph;
        _layoutTransform = new LayoutTransformControl
        {
            Child = graph,
            LayoutTransform = new ScaleTransform(1, 1)
        };
        _scrollViewer = new ScrollViewer
        {
            Content = _layoutTransform,
            Background = new SolidColorBrush(Color.Parse("#0F1729")),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        _scrollViewer.PointerPressed += OnPointerPressed;
        _scrollViewer.PointerMoved += OnPointerMoved;
        _scrollViewer.PointerReleased += OnPointerReleased;
        _scrollViewer.PointerCaptureLost += OnPointerCaptureLost;
        _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;

        _zoomLabel = new TextBlock
        {
            Text = "100 %",
            MinWidth = 48,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#D6DCF0")),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Button zoomOut = CreateToolButton("−", "Réduire");
        Button zoomIn = CreateToolButton("+", "Agrandir");
        Button fit = CreateToolButton("Ajuster", "Afficher l'ensemble du graphe");
        Button reset = CreateToolButton("100 %", "Revenir à la taille réelle");
        zoomOut.Click += (_, _) => SetZoom(_zoom / 1.2, null);
        zoomIn.Click += (_, _) => SetZoom(_zoom * 1.2, null);
        fit.Click += (_, _) => FitToView();
        reset.Click += (_, _) => ResetView();

        Border tools = new()
        {
            Background = new SolidColorBrush(Color.Parse("#111A2DDD")),
            BorderBrush = new SolidColorBrush(Color.Parse("#34415D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { zoomOut, _zoomLabel, zoomIn, fit, reset }
            }
        };
        Border hint = new()
        {
            Background = new SolidColorBrush(Color.Parse("#111A2DBB")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Glisser pour déplacer · molette pour zoomer · double-clic pour ajuster",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.Parse("#AEB9D1"))
            }
        };

        Content = new Grid { Children = { _scrollViewer, tools, hint } };
    }

    public void ResetView()
    {
        SetZoom(1, null);
        Dispatcher.UIThread.Post(() => _scrollViewer.Offset = default, DispatcherPriority.Background);
    }

    public void FitToView()
    {
        double graphWidth = Math.Max(1, _graph.Bounds.Width);
        double graphHeight = Math.Max(1, _graph.Bounds.Height);
        double viewportWidth = Math.Max(1, _scrollViewer.Viewport.Width - 36);
        double viewportHeight = Math.Max(1, _scrollViewer.Viewport.Height - 36);
        double fit = Math.Min(viewportWidth / graphWidth, viewportHeight / graphHeight);
        SetZoom(Math.Clamp(fit, MinimumZoom, 1.25), null);
        Dispatcher.UIThread.Post(() => _scrollViewer.Offset = default, DispatcherPriority.Background);
    }

    private static Button CreateToolButton(string content, string toolTip)
    {
        Button button = new()
        {
            Content = content,
            MinWidth = content.Length > 2 ? 58 : 28,
            Height = 28,
            Padding = new Thickness(7, 2),
            FontSize = 10,
            Background = new SolidColorBrush(Color.Parse("#202B44")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A4968")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        ToolTip.SetTip(button, toolTip);
        return button;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(_scrollViewer);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            FitToView();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(_scrollViewer);
        _panStartOffset = _scrollViewer.Offset;
        e.Pointer.Capture(_scrollViewer);
        _scrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        Point current = e.GetPosition(_scrollViewer);
        Vector delta = current - _panStart;
        SetOffset(_panStartOffset - delta);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        e.Pointer.Capture(null);
        _scrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPanning = false;
        _scrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) < double.Epsilon)
        {
            return;
        }

        double factor = Math.Pow(1.14, e.Delta.Y);
        SetZoom(_zoom * factor, e.GetPosition(_scrollViewer));
        e.Handled = true;
    }

    private void SetZoom(double requestedZoom, Point? anchor)
    {
        double newZoom = Math.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
        if (Math.Abs(newZoom - _zoom) < 0.001)
        {
            return;
        }

        Vector oldOffset = _scrollViewer.Offset;
        Point focus = anchor ?? new Point(_scrollViewer.Viewport.Width / 2, _scrollViewer.Viewport.Height / 2);
        double ratio = newZoom / _zoom;
        Vector desiredOffset = new(
            (oldOffset.X + focus.X) * ratio - focus.X,
            (oldOffset.Y + focus.Y) * ratio - focus.Y);
        _zoom = newZoom;
        _layoutTransform.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        _zoomLabel.Text = $"{Math.Round(_zoom * 100):0} %";
        Dispatcher.UIThread.Post(() => SetOffset(desiredOffset), DispatcherPriority.Background);
    }

    private void SetOffset(Vector offset)
    {
        Vector maximum = _scrollViewer.ScrollBarMaximum;
        _scrollViewer.Offset = new Vector(
            Math.Clamp(offset.X, 0, Math.Max(0, maximum.X)),
            Math.Clamp(offset.Y, 0, Math.Max(0, maximum.Y)));
    }
}
