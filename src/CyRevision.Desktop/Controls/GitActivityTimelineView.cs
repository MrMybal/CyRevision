using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CyRevision.Git;

namespace CyRevision.Desktop.Controls;

public sealed class GitActivityTimelineView : UserControl
{
    public static readonly StyledProperty<IEnumerable<GitDailyActivity>?> ActivityProperty =
        AvaloniaProperty.Register<GitActivityTimelineView, IEnumerable<GitDailyActivity>?>(nameof(Activity));

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.Parse("#0F1729")) };
    private readonly GraphViewport _viewport;

    public GitActivityTimelineView()
    {
        _viewport = new GraphViewport(_canvas);
        Content = _viewport;
        Rebuild();
    }

    public IEnumerable<GitDailyActivity>? Activity
    {
        get => GetValue(ActivityProperty);
        set => SetValue(ActivityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActivityProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        GitDailyActivity[] points = Activity?.OrderBy(point => point.Day).TakeLast(365).ToArray() ?? [];
        if (points.Length == 0)
        {
            _canvas.Width = 900;
            _canvas.Height = 430;
            AddText("Analysez le dépôt pour afficher l'activité quotidienne.", 28, 28, 13, "#8D96AC");
            return;
        }

        const double left = 58;
        const double top = 42;
        const double chartHeight = 330;
        const double barWidth = 13;
        const double gap = 5;
        double width = Math.Max(900, left + points.Length * (barWidth + gap) + 50);
        _canvas.Width = width;
        _canvas.Height = 430;
        int maxCommits = Math.Max(1, points.Max(point => point.CommitCount));
        long maxLines = Math.Max(1, points.Max(point => point.AddedLines + point.DeletedLines));

        for (int lineIndex = 0; lineIndex <= 4; lineIndex++)
        {
            double y = top + chartHeight * lineIndex / 4;
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(left, y),
                EndPoint = new Point(width - 25, y),
                Stroke = new SolidColorBrush(Color.Parse("#35425D")) { Opacity = 0.4 },
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            });
        }

        Polyline churnLine = new()
        {
            Stroke = new SolidColorBrush(Color.Parse("#E5A94D")),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        int labelEvery = Math.Max(1, points.Length / 9);
        for (int index = 0; index < points.Length; index++)
        {
            GitDailyActivity point = points[index];
            double x = left + index * (barWidth + gap);
            double commitHeight = chartHeight * point.CommitCount / maxCommits;
            Rectangle bar = new()
            {
                Width = barWidth,
                Height = Math.Max(2, commitHeight),
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(Color.Parse("#6D5EF5"))
            };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, top + chartHeight - Math.Max(2, commitHeight));
            ToolTip.SetTip(bar,
                $"{point.Day:d}\n{point.CommitCount} commit(s) · {point.FilesTouched} file(s)\n" +
                $"+{point.AddedLines} / -{point.DeletedLines}");
            _canvas.Children.Add(bar);

            double churnY = top + chartHeight - chartHeight * (point.AddedLines + point.DeletedLines) / maxLines;
            churnLine.Points.Add(new Point(x + barWidth / 2, churnY));
            if (index % labelEvery == 0 || index == points.Length - 1)
            {
                AddText(point.Day.ToString("dd MMM"), x - 8, top + chartHeight + 13, 9, "#8D96AC", -35);
            }
        }

        _canvas.Children.Insert(0, churnLine);
        AddText("Commits", left + 330, 14, 11, "#9F94FF");
        AddText("Code churn", left + 406, 14, 11, "#E5A94D");
        _viewport.ResetView();
    }

    private void AddText(string value, double x, double y, double size, string color, double rotation = 0)
    {
        TextBlock text = new()
        {
            Text = value,
            FontSize = size,
            Foreground = new SolidColorBrush(Color.Parse(color)),
            RenderTransform = rotation == 0 ? null : new RotateTransform(rotation)
        };
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, y);
        _canvas.Children.Add(text);
    }
}
