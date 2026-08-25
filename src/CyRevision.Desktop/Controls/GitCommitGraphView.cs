using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CyRevision.Git;

namespace CyRevision.Desktop.Controls;

public sealed class GitCommitGraphView : UserControl
{
    public static readonly StyledProperty<IEnumerable<GitGraphCommit>?> CommitsProperty =
        AvaloniaProperty.Register<GitCommitGraphView, IEnumerable<GitGraphCommit>?>(nameof(Commits));

    private static readonly Color[] LaneColors =
    [
        Color.Parse("#7B6CF6"), Color.Parse("#36C5A1"), Color.Parse("#E5A94D"),
        Color.Parse("#55A7F3"), Color.Parse("#E56B91"), Color.Parse("#A477D4")
    ];

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.Parse("#0F1729")) };
    private readonly GraphViewport _viewport;
    private readonly Dictionary<string, CommitRowView> _nodes = new(StringComparer.Ordinal);
    private string? _selectedHash;

    public event EventHandler<GitCommitSelectedEventArgs>? CommitSelected;

    public GitCommitGraphView()
    {
        _viewport = new GraphViewport(_canvas);
        Content = _viewport;
        Rebuild();
    }

    public IEnumerable<GitGraphCommit>? Commits
    {
        get => GetValue(CommitsProperty);
        set => SetValue(CommitsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommitsProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _nodes.Clear();
        GitGraphCommit[] commits = Commits?.Take(300).ToArray() ?? [];
        if (commits.Length == 0)
        {
            AddEmptyMessage("Cliquez sur « Analyser le dépôt » pour construire le graphe des commits.");
            return;
        }

        const double rowHeight = 31;
        const double headerHeight = 28;
        const double laneSpacing = 17;
        Dictionary<string, NodePosition> positions = new(StringComparer.Ordinal);
        List<string?> activeLanes = [];
        int largestLane = 0;
        for (int index = 0; index < commits.Length; index++)
        {
            GitGraphCommit commit = commits[index];
            int lane = activeLanes.FindIndex(value => string.Equals(value, commit.Hash, StringComparison.Ordinal));
            if (lane < 0)
            {
                lane = activeLanes.FindIndex(value => value is null);
                if (lane < 0)
                {
                    lane = activeLanes.Count;
                    activeLanes.Add(null);
                }
            }

            largestLane = Math.Max(largestLane, lane);
            double x = 24 + lane * laneSpacing;
            double y = headerHeight + index * rowHeight + rowHeight / 2;
            positions[commit.Hash] = new NodePosition(x, y, lane);

            string? primaryParent = commit.ParentHashes.FirstOrDefault();
            int existingPrimaryLane = -1;
            if (primaryParent is not null)
            {
                for (int activeLane = 0; activeLane < activeLanes.Count; activeLane++)
                {
                    if (activeLane != lane &&
                        string.Equals(activeLanes[activeLane], primaryParent, StringComparison.Ordinal))
                    {
                        existingPrimaryLane = activeLane;
                        break;
                    }
                }
            }
            activeLanes[lane] = existingPrimaryLane >= 0 ? null : primaryParent;

            foreach (string additionalParent in commit.ParentHashes.Skip(1))
            {
                if (activeLanes.Any(value => string.Equals(value, additionalParent, StringComparison.Ordinal)))
                {
                    continue;
                }

                int freeLane = activeLanes.FindIndex(value => value is null);
                if (freeLane < 0)
                {
                    activeLanes.Add(additionalParent);
                }
                else
                {
                    activeLanes[freeLane] = additionalParent;
                }
            }

            while (activeLanes.Count > 0 && activeLanes[^1] is null)
            {
                activeLanes.RemoveAt(activeLanes.Count - 1);
            }
        }

        double graphWidth = 48 + (largestLane + 1) * laneSpacing;
        double subjectStart = Math.Max(150, graphWidth + 16);
        double canvasWidth = Math.Max(1280, subjectStart + 1100);
        double dateStart = canvasWidth - 195;
        double authorStart = dateStart - 190;
        double decorationStart = authorStart - 190;
        _canvas.Width = canvasWidth;
        _canvas.Height = headerHeight + commits.Length * rowHeight;
        _canvas.Background = new SolidColorBrush(Color.Parse("#1E1F22"));
        AddColumnHeaders(subjectStart, authorStart, dateStart, canvasWidth, headerHeight);

        for (int index = 0; index < commits.Length; index++)
        {
            GitGraphCommit commit = commits[index];
            bool isHead = commit.Decorations.Contains("HEAD", StringComparison.OrdinalIgnoreCase);
            Border background = new()
            {
                Width = canvasWidth,
                Height = rowHeight,
                Background = new SolidColorBrush(Color.Parse(isHead
                    ? "#2D3440"
                    : index % 2 == 0 ? "#232427" : "#1E1F22")),
                BorderBrush = new SolidColorBrush(Color.Parse("#303238")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                IsHitTestVisible = false
            };
            Canvas.SetTop(background, headerHeight + index * rowHeight);
            _canvas.Children.Add(background);
        }

        foreach (GitGraphCommit commit in commits)
        {
            NodePosition source = positions[commit.Hash];
            foreach (string parentHash in commit.ParentHashes)
            {
                if (!positions.TryGetValue(parentHash, out NodePosition parent))
                {
                    continue;
                }

                Color color = LaneColors[source.Lane % LaneColors.Length];
                if (source.Lane == parent.Lane)
                {
                    AddEdgeSegment(source.X, source.Y, parent.X, parent.Y, color, 2);
                }
                else
                {
                    double bendY = Math.Min(parent.Y - 6, source.Y + rowHeight * 0.58);
                    AddEdgeSegment(source.X, source.Y, source.X, bendY, color, 1.7);
                    AddEdgeSegment(source.X, bendY, parent.X, bendY, color, 1.7);
                    AddEdgeSegment(parent.X, bendY, parent.X, parent.Y, color, 1.7);
                }
            }
        }

        for (int index = 0; index < commits.Length; index++)
        {
            GitGraphCommit commit = commits[index];
            NodePosition position = positions[commit.Hash];
            Color laneColor = LaneColors[position.Lane % LaneColors.Length];
            bool isHead = commit.Decorations.Contains("HEAD", StringComparison.OrdinalIgnoreCase);
            AddCommitRow(
                commit, position, laneColor, isHead, index, subjectStart, authorStart, dateStart,
                decorationStart, canvasWidth, rowHeight, headerHeight);
        }

        ApplySelection();
        _viewport.FitToViewDeferred();
    }

    private void AddCommitRow(
        GitGraphCommit commit,
        NodePosition position,
        Color laneColor,
        bool isHead,
        int index,
        double subjectStart,
        double authorStart,
        double dateStart,
        double decorationStart,
        double canvasWidth,
        double rowHeight,
        double headerHeight)
    {
        Ellipse marker = new()
        {
            Width = isHead ? 11 : 9,
            Height = isHead ? 11 : 9,
            Fill = new SolidColorBrush(isHead ? Color.Parse("#DDE3F0") : Color.Parse("#2B2D30")),
            Stroke = new SolidColorBrush(laneColor),
            StrokeThickness = isHead ? 3 : 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(marker, position.X - marker.Width / 2);
        Canvas.SetTop(marker, position.Y - marker.Height / 2);
        _canvas.Children.Add(marker);

        TextBlock hash = CreateCell(commit.ShortHash, subjectStart, position.Y, 72, "#8CB4FF", 10.5);
        hash.FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace");
        _canvas.Children.Add(hash);

        TextBlock subject = CreateCell(
            commit.Subject,
            subjectStart + 76,
            position.Y,
            Math.Max(180, decorationStart - subjectStart - 94),
            isHead ? "#F2F3F5" : "#D7D8DC",
            11.5);
        subject.FontWeight = isHead ? FontWeight.SemiBold : FontWeight.Normal;
        _canvas.Children.Add(subject);

        if (!string.IsNullOrWhiteSpace(commit.Decorations))
        {
            Border decoration = new()
            {
                MaxWidth = 175,
                Height = 20,
                Background = new SolidColorBrush(Color.Parse("#243C35")),
                BorderBrush = new SolidColorBrush(Color.Parse("#3C8A76")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1),
                Child = new TextBlock
                {
                    Text = commit.Decorations,
                    Foreground = new SolidColorBrush(Color.Parse("#83D9C2")),
                    FontSize = 9.5,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(decoration, decorationStart);
            Canvas.SetTop(decoration, position.Y - 10);
            _canvas.Children.Add(decoration);
        }

        _canvas.Children.Add(CreateCell(commit.AuthorName, authorStart, position.Y, 170, "#B4B6BC", 10.5));
        _canvas.Children.Add(CreateCell(commit.AuthoredAt.LocalDateTime.ToString("g"), dateStart, position.Y, 185, "#9B9DA3", 10.5));

        Border hitTarget = new()
        {
            Width = canvasWidth,
            Height = rowHeight,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0)
        };
        Canvas.SetTop(hitTarget, headerHeight + index * rowHeight);
        ToolTip.SetTip(hitTarget,
            $"{commit.Hash}\n{commit.Subject}\nAuteur : {commit.AuthorName}\n" +
            $"Parents : {(commit.ParentHashes.Count == 0 ? "racine" : string.Join(", ", commit.ParentHashes.Select(hash => hash[..Math.Min(8, hash.Length)])))}\n" +
            $"Références : {(string.IsNullOrWhiteSpace(commit.Decorations) ? "aucune" : commit.Decorations)}");
        hitTarget.PointerPressed += (_, _) =>
        {
            _selectedHash = commit.Hash;
            ApplySelection();
            CommitSelected?.Invoke(this, new GitCommitSelectedEventArgs(commit));
        };
        _nodes[commit.Hash] = new CommitRowView(hitTarget, index, isHead);
        _canvas.Children.Add(hitTarget);
    }

    private void ApplySelection()
    {
        foreach ((string hash, CommitRowView view) in _nodes)
        {
            bool selected = string.Equals(hash, _selectedHash, StringComparison.Ordinal);
            view.HitTarget.Background = new SolidColorBrush(Color.Parse(selected ? "#31415A" : "#00000000"));
            view.HitTarget.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#4B8DFF" : "#00000000"));
        }
    }

    private static TextBlock CreateCell(
        string text,
        double x,
        double centerY,
        double width,
        string color,
        double fontSize)
    {
        TextBlock cell = new()
        {
            Text = text,
            Width = width,
            Height = 22,
            Foreground = new SolidColorBrush(Color.Parse(color)),
            FontSize = fontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(cell, x);
        Canvas.SetTop(cell, centerY - 10);
        return cell;
    }

    private void AddColumnHeaders(
        double subjectStart,
        double authorStart,
        double dateStart,
        double width,
        double height)
    {
        Border background = new()
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.Parse("#2B2D30")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45474D")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            IsHitTestVisible = false
        };
        _canvas.Children.Add(background);
        _canvas.Children.Add(CreateCell("Graph", 20, height / 2 + 1, Math.Max(90, subjectStart - 28), "#9B9DA3", 10));
        _canvas.Children.Add(CreateCell("Commit", subjectStart, height / 2 + 1, authorStart - subjectStart - 10, "#9B9DA3", 10));
        _canvas.Children.Add(CreateCell("Author", authorStart, height / 2 + 1, 170, "#9B9DA3", 10));
        _canvas.Children.Add(CreateCell("Date", dateStart, height / 2 + 1, 185, "#9B9DA3", 10));
    }

    private void AddEdgeSegment(
        double startX,
        double startY,
        double endX,
        double endY,
        Color color,
        double thickness)
    {
        _canvas.Children.Add(new Line
        {
            StartPoint = new Point(startX, startY),
            EndPoint = new Point(endX, endY),
            Stroke = new SolidColorBrush(color) { Opacity = 0.78 },
            StrokeThickness = thickness,
            IsHitTestVisible = false
        });
    }

    private void AddEmptyMessage(string message)
    {
        _canvas.Width = 1200;
        _canvas.Height = 420;
        _canvas.Background = new SolidColorBrush(Color.Parse("#1E1F22"));
        TextBlock text = new()
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#8D96AC")),
            FontSize = 13
        };
        Canvas.SetLeft(text, 28);
        Canvas.SetTop(text, 28);
        _canvas.Children.Add(text);
    }

    private readonly record struct NodePosition(double X, double Y, int Lane);

    private sealed record CommitRowView(Border HitTarget, int Index, bool IsHead);
}

public sealed class GitCommitSelectedEventArgs(GitGraphCommit commit) : EventArgs
{
    public GitGraphCommit Commit { get; } = commit;
}
