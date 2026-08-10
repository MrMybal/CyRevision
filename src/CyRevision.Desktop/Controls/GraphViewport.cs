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
    private const double MinimumZoom = 0.15;
    private const double MaximumZoom = 2.5;
    private readonly Control _graph;
    private readonly Canvas _surface;
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBlock _zoomLabel;
    private bool _isPanning;
    private Point _panStart;
    private Vector _panStartOffset;
    private double _zoom = 1;
    private Vector? _pendingOffset;
    private bool _offsetUpdateQueued;

    public GraphViewport(Control graph)
    {
        _graph = graph;
        _graph.RenderTransformOrigin = RelativePoint.TopLeft;
        _graph.RenderTransform = new ScaleTransform(1, 1);
        _surface = new Canvas
        {
            Background = Brushes.Transparent,
            ClipToBounds = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { graph }
        };
        _scrollViewer = new ScrollViewer
        {
            Content = _surface,
            Background = new SolidColorBrush(Color.Parse("#1E1F22")),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        _scrollViewer.PointerPressed += OnPointerPressed;
        _scrollViewer.PointerMoved += OnPointerMoved;
        _scrollViewer.PointerReleased += OnPointerReleased;
        _scrollViewer.PointerCaptureLost += OnPointerCaptureLost;
        _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
        _graph.SizeChanged += (_, _) => UpdateSurfaceExtent();

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
            Background = new SolidColorBrush(Color.Parse("#2B2D30EE")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4B4D52")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(3),
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { zoomOut, _zoomLabel, zoomIn, fit, reset }
            }
        };
        Border hint = new()
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(7, 0),
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Glisser pour déplacer · Ctrl + molette pour zoomer · double-clic pour ajuster",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.Parse("#AEB9D1"))
            }
        };

        Grid toolbar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Height = 34,
            Background = new SolidColorBrush(Color.Parse("#2B2D30"))
        };
        toolbar.Children.Add(hint);
        Grid.SetColumn(tools, 1);
        toolbar.Children.Add(tools);

        Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(toolbar);
        Grid.SetRow(_scrollViewer, 1);
        root.Children.Add(_scrollViewer);
        Content = root;
        UpdateSurfaceExtent();
    }

    public void ResetView()
    {
        _pendingOffset = default(Vector);
        SetZoom(1, null);
        ScheduleOffsetUpdate();
    }

    public void FitToView()
    {
        (double graphWidth, double graphHeight) = GetGraphSize();
        double viewportWidth = Math.Max(1, _scrollViewer.Viewport.Width - 36);
        double viewportHeight = Math.Max(1, _scrollViewer.Viewport.Height - 36);
        double fit = Math.Min(viewportWidth / graphWidth, viewportHeight / graphHeight);
        double fittedZoom = Math.Clamp(fit, MinimumZoom, 1.25);
        SetZoom(fittedZoom, null);
        _pendingOffset = new Vector(
            Math.Max(0, graphWidth * fittedZoom - _scrollViewer.Viewport.Width) / 2,
            Math.Max(0, graphHeight * fittedZoom - _scrollViewer.Viewport.Height) / 2);
        ScheduleOffsetUpdate();
    }

    private static Button CreateToolButton(string content, string toolTip)
    {
        Button button = new()
        {
            Content = content,
            MinWidth = content.Length > 2 ? 58 : 28,
            Height = 24,
            Padding = new Thickness(6, 1),
            FontSize = 10,
            Background = new SolidColorBrush(Color.Parse("#393B40")),
            BorderBrush = new SolidColorBrush(Color.Parse("#55575E")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
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
        _panStartOffset = _pendingOffset ?? _scrollViewer.Offset;
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
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (Math.Abs(e.Delta.Y) < double.Epsilon)
        {
            return;
        }

        double wheelStep = Math.Clamp(e.Delta.Y, -1, 1);
        double factor = Math.Pow(1.12, wheelStep);
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

        Vector oldOffset = _pendingOffset ?? _scrollViewer.Offset;
        Point focus = anchor ?? new Point(_scrollViewer.Viewport.Width / 2, _scrollViewer.Viewport.Height / 2);
        double ratio = newZoom / _zoom;
        Vector desiredOffset = new(
            (oldOffset.X + focus.X) * ratio - focus.X,
            (oldOffset.Y + focus.Y) * ratio - focus.Y);
        _zoom = newZoom;
        _graph.RenderTransform = new ScaleTransform(_zoom, _zoom);
        UpdateSurfaceExtent();
        _zoomLabel.Text = $"{Math.Round(_zoom * 100):0} %";
        _pendingOffset = desiredOffset;
        ScheduleOffsetUpdate();
    }

    private void UpdateSurfaceExtent()
    {
        (double width, double height) = GetGraphSize();
        _surface.Width = Math.Max(1, width * _zoom);
        _surface.Height = Math.Max(1, height * _zoom);
    }

    private (double Width, double Height) GetGraphSize()
    {
        double width = !double.IsNaN(_graph.Width) && _graph.Width > 0
            ? _graph.Width
            : Math.Max(1, _graph.Bounds.Width);
        double height = !double.IsNaN(_graph.Height) && _graph.Height > 0
            ? _graph.Height
            : Math.Max(1, _graph.Bounds.Height);
        return (width, height);
    }

    private void ScheduleOffsetUpdate()
    {
        if (_offsetUpdateQueued)
        {
            return;
        }

        _offsetUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _offsetUpdateQueued = false;
            if (_pendingOffset is not Vector offset)
            {
                return;
            }

            _pendingOffset = null;
            SetOffset(offset);
        }, DispatcherPriority.Background);
    }

    private void SetOffset(Vector offset)
    {
        Vector maximum = _scrollViewer.ScrollBarMaximum;
        _scrollViewer.Offset = new Vector(
            Math.Clamp(offset.X, 0, Math.Max(0, maximum.X)),
            Math.Clamp(offset.Y, 0, Math.Max(0, maximum.Y)));
    }
}
