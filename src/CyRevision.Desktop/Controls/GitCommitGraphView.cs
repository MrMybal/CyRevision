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
    private readonly Dictionary<string, Border> _nodes = new(StringComparer.Ordinal);
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

        const double nodeWidth = 276;
        const double nodeHeight = 70;
        const double horizontalGap = 54;
        const double verticalGap = 30;
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
            double x = 26 + lane * (nodeWidth + horizontalGap);
            double y = 76 + index * (nodeHeight + verticalGap);
            positions[commit.Hash] = new NodePosition(x, y, lane);
            activeLanes[lane] = commit.ParentHashes.FirstOrDefault();
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
        }

        _canvas.Width = Math.Max(900, 52 + (largestLane + 1) * (nodeWidth + horizontalGap));
        _canvas.Height = 110 + commits.Length * (nodeHeight + verticalGap);
        AddGridBackground(_canvas.Width, _canvas.Height);

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
                Line edge = new()
                {
                    StartPoint = new Point(source.X + nodeWidth / 2, source.Y + nodeHeight),
                    EndPoint = new Point(parent.X + nodeWidth / 2, parent.Y),
                    Stroke = new SolidColorBrush(color) { Opacity = parent.Lane == source.Lane ? 0.72 : 0.48 },
                    StrokeThickness = parent.Lane == source.Lane ? 2.2 : 1.6
                };
                _canvas.Children.Add(edge);
            }
        }

        foreach (GitGraphCommit commit in commits)
        {
            NodePosition position = positions[commit.Hash];
            Color laneColor = LaneColors[position.Lane % LaneColors.Length];
            bool isHead = commit.Decorations.Contains("HEAD", StringComparison.OrdinalIgnoreCase);
            Border node = CreateNode(commit, laneColor, isHead, nodeWidth, nodeHeight);
            _nodes[commit.Hash] = node;
            Canvas.SetLeft(node, position.X);
            Canvas.SetTop(node, position.Y);
            _canvas.Children.Add(node);
        }

        ApplySelection();
        _viewport.ResetView();
    }

    private Border CreateNode(
        GitGraphCommit commit,
        Color laneColor,
        bool isHead,
        double width,
        double height)
    {
        TextBlock title = new()
        {
            Text = $"{commit.ShortHash}  {commit.Subject}",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 22
        };
        TextBlock details = new()
        {
            Text = $"{commit.AuthorName} · {commit.AuthoredAt.LocalDateTime:g}",
            Foreground = new SolidColorBrush(Color.Parse("#9DA8BE")),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 22
        };
        StackPanel content = new() { Spacing = 4, Children = { title, details } };
        Border node = new()
        {
            Width = width,
            Height = height,
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(isHead ? Color.Parse("#302A61") : Color.Parse("#1A2740")),
            BorderBrush = new SolidColorBrush(laneColor),
            BorderThickness = new Thickness(isHead ? 2.2 : 1.2),
            Child = content
        };
        ToolTip.SetTip(node,
            $"{commit.Hash}\n{commit.Subject}\nAuteur : {commit.AuthorName}\n" +
            $"Parents : {(commit.ParentHashes.Count == 0 ? "racine" : string.Join(", ", commit.ParentHashes.Select(hash => hash[..Math.Min(8, hash.Length)])))}\n" +
            $"Références : {(string.IsNullOrWhiteSpace(commit.Decorations) ? "aucune" : commit.Decorations)}");
        node.PointerPressed += (_, _) =>
        {
            _selectedHash = commit.Hash;
            ApplySelection();
            CommitSelected?.Invoke(this, new GitCommitSelectedEventArgs(commit));
        };
        return node;
    }

    private void ApplySelection()
    {
        foreach ((string hash, Border node) in _nodes)
        {
            bool selected = string.Equals(hash, _selectedHash, StringComparison.Ordinal);
            node.Background = new SolidColorBrush(Color.Parse(selected ? "#35306B" : "#1A2740"));
            node.BorderThickness = new Thickness(selected ? 2.8 : 1.2);
            node.Opacity = selected || _selectedHash is null ? 1 : 0.76;
        }
    }

    private void AddEmptyMessage(string message)
    {
        _canvas.Width = 900;
        _canvas.Height = 500;
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

    private void AddGridBackground(double width, double height)
    {
        SolidColorBrush brush = new(Color.Parse("#35425D")) { Opacity = 0.22 };
        for (double x = 40; x < width; x += 100)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0), EndPoint = new Point(x, height),
                Stroke = brush, StrokeThickness = 0.6, IsHitTestVisible = false
            });
        }

        for (double y = 40; y < height; y += 100)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y), EndPoint = new Point(width, y),
                Stroke = brush, StrokeThickness = 0.6, IsHitTestVisible = false
            });
        }
    }

    private readonly record struct NodePosition(double X, double Y, int Lane);
}

public sealed class GitCommitSelectedEventArgs(GitGraphCommit commit) : EventArgs
{
    public GitGraphCommit Commit { get; } = commit;
}
