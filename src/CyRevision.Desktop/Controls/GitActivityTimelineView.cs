using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Git;

namespace CyRevision.Desktop.Controls;

public sealed class GitActivityTimelineView : UserControl
{
    public static readonly StyledProperty<IEnumerable<GitDailyActivity>?> ActivityProperty =
        AvaloniaProperty.Register<GitActivityTimelineView, IEnumerable<GitDailyActivity>?>(nameof(Activity));

    private static readonly Color[] HeatColors =
    [
        Color.Parse("#272B31"),
        Color.Parse("#17433E"),
        Color.Parse("#1E7164"),
        Color.Parse("#2AA88F"),
        Color.Parse("#65D7B6")
    ];

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.Parse("#17191D")) };
    private readonly TextBlock _summary = new()
    {
        FontSize = 10.5,
        Foreground = new SolidColorBrush(Color.Parse("#B6BBC6")),
        VerticalAlignment = VerticalAlignment.Center
    };

    public GitActivityTimelineView()
    {
        Grid header = new()
        {
            Height = 34,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = new SolidColorBrush(Color.Parse("#24262B"))
        };
        _summary.Margin = new Thickness(9, 0);
        header.Children.Add(_summary);

        StackPanel legend = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        legend.Children.Add(CreateLegendLabel("Less"));
        foreach (Color color in HeatColors)
            legend.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(color) });
        legend.Children.Add(CreateLegendLabel("More"));
        Grid.SetColumn(legend, 1);
        header.Children.Add(legend);

        ScrollViewer scroller = new()
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.Parse("#17191D"))
        };

        Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(header);
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        Content = root;
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
        if (change.Property == ActivityProperty) Rebuild();
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        GitDailyActivity[] points = Activity?
            .GroupBy(point => point.Day)
            .Select(group => new GitDailyActivity(
                group.Key,
                group.Sum(point => point.CommitCount),
                group.Sum(point => point.FilesTouched),
                group.Sum(point => point.AddedLines),
                group.Sum(point => point.DeletedLines)))
            .OrderBy(point => point.Day)
            .TakeLast(371)
            .ToArray() ?? [];

        if (points.Length == 0)
        {
            _canvas.Width = 960;
            _canvas.Height = 150;
            _summary.Text = "No activity data yet";
            AddText("Analyze the repository to build the activity heatmap.", 24, 44, 12, "#8D96AC");
            return;
        }

        Dictionary<DateOnly, GitDailyActivity> byDay = points.ToDictionary(point => point.Day);
        DateOnly latest = points[^1].Day;
        int latestMondayIndex = ((int)latest.DayOfWeek + 6) % 7;
        DateOnly finalSunday = latest.AddDays(6 - latestMondayIndex);
        const int weeks = 53;
        DateOnly firstMonday = finalSunday.AddDays(-(weeks * 7 - 1));

        const double labelWidth = 43;
        const double top = 27;
        const double cell = 14;
        const double gap = 3;
        const double step = cell + gap;
        _canvas.Width = labelWidth + weeks * step + 30;
        _canvas.Height = top + 7 * step + 12;

        int maxCommits = Math.Max(1, points.Max(point => point.CommitCount));
        int totalCommits = points.Sum(point => point.CommitCount);
        int activeDays = points.Count(point => point.CommitCount > 0);
        GitDailyActivity busiest = points.MaxBy(point => point.CommitCount)!;
        int streak = CalculateCurrentStreak(byDay, latest);
        _summary.Text = $"{totalCommits:N0} commits · {activeDays} active days · {streak}-day streak · busiest {busiest.Day:d} ({busiest.CommitCount})";

        AddText("Mon", 7, top + step * 0 - 1, 9, "#7F8797");
        AddText("Wed", 7, top + step * 2 - 1, 9, "#7F8797");
        AddText("Fri", 7, top + step * 4 - 1, 9, "#7F8797");

        int previousMonth = -1;
        for (int week = 0; week < weeks; week++)
        {
            DateOnly weekStart = firstMonday.AddDays(week * 7);
            if (weekStart.Month != previousMonth && weekStart.Day <= 7)
            {
                AddText(weekStart.ToString("MMM"), labelWidth + week * step, 5, 9, "#929AAA");
                previousMonth = weekStart.Month;
            }

            for (int dayIndex = 0; dayIndex < 7; dayIndex++)
            {
                DateOnly day = weekStart.AddDays(dayIndex);
                byDay.TryGetValue(day, out GitDailyActivity? point);
                int level = HeatLevel(point?.CommitCount ?? 0, maxCommits);
                Border cellView = new()
                {
                    Width = cell,
                    Height = cell,
                    CornerRadius = new CornerRadius(2.5),
                    Background = new SolidColorBrush(HeatColors[level]),
                    BorderBrush = new SolidColorBrush(level == 0 ? Color.Parse("#34383F") : HeatColors[level]),
                    BorderThickness = new Thickness(1)
                };
                Canvas.SetLeft(cellView, labelWidth + week * step);
                Canvas.SetTop(cellView, top + dayIndex * step);
                ToolTip.SetTip(cellView, point is null
                    ? $"{day:dddd, d MMMM yyyy}\nNo commit"
                    : $"{day:dddd, d MMMM yyyy}\n{point.CommitCount} commit(s) · {point.FilesTouched} file(s)\n+{point.AddedLines:N0} / -{point.DeletedLines:N0} lines");
                _canvas.Children.Add(cellView);
            }
        }
    }

    private static int HeatLevel(int commits, int maximum)
    {
        if (commits <= 0) return 0;
        double ratio = commits / (double)Math.Max(1, maximum);
        if (ratio <= 0.2) return 1;
        if (ratio <= 0.45) return 2;
        if (ratio <= 0.72) return 3;
        return 4;
    }

    private static int CalculateCurrentStreak(IReadOnlyDictionary<DateOnly, GitDailyActivity> activity, DateOnly latest)
    {
        int streak = 0;
        for (DateOnly day = latest; activity.TryGetValue(day, out GitDailyActivity? point) && point.CommitCount > 0; day = day.AddDays(-1))
            streak++;
        return streak;
    }

    private static TextBlock CreateLegendLabel(string value) => new()
    {
        Text = value,
        FontSize = 9,
        Foreground = new SolidColorBrush(Color.Parse("#8D96AC")),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void AddText(string value, double x, double y, double size, string color)
    {
        TextBlock text = new()
        {
            Text = value,
            FontSize = size,
            Foreground = new SolidColorBrush(Color.Parse(color))
        };
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, y);
        _canvas.Children.Add(text);
    }
}