using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CyRevision.Git;

namespace CyRevision.Desktop.Controls;

public sealed class GitFileRelationGraphView : UserControl
{
    public static readonly StyledProperty<IEnumerable<GitFileActivity>?> FilesProperty =
        AvaloniaProperty.Register<GitFileRelationGraphView, IEnumerable<GitFileActivity>?>(nameof(Files));

    public static readonly StyledProperty<IEnumerable<GitFileRelation>?> RelationsProperty =
        AvaloniaProperty.Register<GitFileRelationGraphView, IEnumerable<GitFileRelation>?>(nameof(Relations));

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.Parse("#0F1729")) };
    private readonly GraphViewport _viewport;
    private readonly List<(Line Line, GitFileRelation Relation)> _edgeViews = [];
    private readonly Dictionary<string, (Border Node, Color Color)> _nodeViews = new(StringComparer.Ordinal);

    public GitFileRelationGraphView()
    {
        _viewport = new GraphViewport(_canvas);
        Content = _viewport;
        _canvas.PointerPressed += (_, args) =>
        {
            if (ReferenceEquals(args.Source, _canvas))
            {
                ClearHighlight();
            }
        };
        Rebuild();
    }

    public IEnumerable<GitFileActivity>? Files
    {
        get => GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    public IEnumerable<GitFileRelation>? Relations
    {
        get => GetValue(RelationsProperty);
        set => SetValue(RelationsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilesProperty || change.Property == RelationsProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _edgeViews.Clear();
        _nodeViews.Clear();
        GitFileActivity[] files = Files?.Take(80).ToArray() ?? [];
        GitFileRelation[] relations = Relations?.ToArray() ?? [];
        if (files.Length == 0)
        {
            AddEmptyMessage("Lancez l'analyse pour visualiser les fichiers les plus modifiés et leurs relations.");
            return;
        }

        const double width = 1900;
        const double height = 1180;
        _canvas.Width = width;
        _canvas.Height = height;
        Dictionary<string, int> indexes = files
            .Select((file, index) => (file.Path, index))
            .ToDictionary(item => item.Path, item => item.index, StringComparer.Ordinal);
        GitFileRelation[] visibleRelations = relations
            .Where(relation => indexes.ContainsKey(relation.SourcePath) && indexes.ContainsKey(relation.TargetPath))
            .OrderByDescending(relation => relation.CoChangeCount)
            .ThenBy(relation => relation.SourcePath, StringComparer.Ordinal)
            .Take(110)
            .ToArray();
        Point[] positions = CreateLayout(files, width, height);
        AddGridBackground(width, height);

        foreach (GitFileRelation relation in visibleRelations.OrderBy(item => item.CoChangeCount))
        {
            if (!indexes.TryGetValue(relation.SourcePath, out int sourceIndex) ||
                !indexes.TryGetValue(relation.TargetPath, out int targetIndex))
            {
                continue;
            }

            double strength = Math.Min(0.68, 0.16 + Math.Log2(relation.CoChangeCount + 1) * 0.12);
            Line edge = new()
            {
                StartPoint = positions[sourceIndex],
                EndPoint = positions[targetIndex],
                Stroke = new SolidColorBrush(Color.Parse("#6D7EA5")) { Opacity = strength },
                StrokeThickness = 0.8 + Math.Min(3.2, Math.Log2(relation.CoChangeCount + 1) * 0.8),
                IsHitTestVisible = false
            };
            ToolTip.SetTip(edge, $"Modifiés ensemble {relation.CoChangeCount} fois");
            _canvas.Children.Add(edge);
            _edgeViews.Add((edge, relation));
        }

        int maximumChanges = Math.Max(1, files.Max(file => file.ChangeCount));
        foreach ((GitFileActivity file, int index) in files.Select((file, index) => (file, index)))
        {
            double scale = Math.Log2(file.ChangeCount + 1) / Math.Log2(maximumChanges + 1);
            double nodeWidth = 158 + 64 * scale;
            double nodeHeight = 68 + 16 * scale;
            Border node = CreateNode(file, nodeWidth, nodeHeight);
            Canvas.SetLeft(node, positions[index].X - nodeWidth / 2);
            Canvas.SetTop(node, positions[index].Y - nodeHeight / 2);
            _canvas.Children.Add(node);
            _nodeViews[file.Path] = (node, GetKindColor(file.Kind));
        }

        _viewport.ResetView();
    }

    private static Point[] CreateLayout(
        IReadOnlyList<GitFileActivity> files,
        double width,
        double height)
    {
        Point[] positions = new Point[files.Count];
        const double horizontalMargin = 135;
        const double topMargin = 135;
        const double bottomMargin = 70;
        int columns = Math.Min(
            Math.Max(1, files.Count),
            Math.Max(1, (int)Math.Floor((width - horizontalMargin * 2) / 220) + 1));
        int rows = (int)Math.Ceiling(files.Count / (double)columns);
        double horizontalSpacing = (width - horizontalMargin * 2) / Math.Max(1, columns - 1);
        double verticalSpacing = (height - topMargin - bottomMargin) / Math.Max(1, rows - 1);
        for (int index = 0; index < files.Count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            if (row % 2 == 1)
            {
                column = columns - 1 - column;
            }

            positions[index] = new Point(
                horizontalMargin + column * horizontalSpacing,
                topMargin + row * verticalSpacing);
        }

        return positions;
    }

    private Border CreateNode(GitFileActivity file, double width, double height)
    {
        TextBlock name = new()
        {
            Text = System.IO.Path.GetFileName(file.Path),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 18,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        string? directory = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/');
        if (directory?.Length > 30)
        {
            directory = "…" + directory[^29..];
        }
        TextBlock location = new()
        {
            Text = string.IsNullOrWhiteSpace(directory) ? "/" : directory,
            Foreground = new SolidColorBrush(Color.Parse("#B8C4DA")),
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 20,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        TextBlock count = new()
        {
            Text = $"{file.ChangeCount} modification{(file.ChangeCount > 1 ? "s" : string.Empty)}",
            Foreground = new SolidColorBrush(Color.Parse("#D9DEF0")),
            FontSize = 9.5,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        Color color = GetKindColor(file.Kind);
        Border node = new()
        {
            Width = width,
            Height = height,
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(height / 2),
            Background = new SolidColorBrush(color) { Opacity = 0.38 },
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(1.7),
            Child = new StackPanel { Spacing = 2, Children = { name, location, count } }
        };
        ToolTip.SetTip(node,
            $"{file.Path}\nType : {file.Kind}\nCommits modifiant le fichier : {file.ChangeCount}\n" +
            $"Lignes : +{file.AddedLines} / -{file.DeletedLines}\nModifications binaires : {file.BinaryChangeCount}\n" +
            $"Dernière modification : {file.LastChangedAt.LocalDateTime:g}");
        node.PointerPressed += (_, _) =>
        {
            Highlight(file.Path);
        };
        return node;
    }

    private void Highlight(string path)
    {
        HashSet<string> neighbors = new(StringComparer.Ordinal) { path };
        foreach ((Line line, GitFileRelation relation) in _edgeViews)
        {
            bool connected = string.Equals(relation.SourcePath, path, StringComparison.Ordinal) ||
                             string.Equals(relation.TargetPath, path, StringComparison.Ordinal);
            line.Opacity = connected ? 1 : 0.08;
            if (connected)
            {
                neighbors.Add(relation.SourcePath);
                neighbors.Add(relation.TargetPath);
            }
        }

        foreach ((string nodePath, (Border node, Color color)) in _nodeViews)
        {
            bool selected = string.Equals(nodePath, path, StringComparison.Ordinal);
            bool related = neighbors.Contains(nodePath);
            node.Opacity = related ? 1 : 0.26;
            node.BorderThickness = new Thickness(selected ? 3 : related ? 2 : 1.2);
            node.Background = new SolidColorBrush(color) { Opacity = selected ? 0.72 : related ? 0.44 : 0.18 };
        }
    }

    private void ClearHighlight()
    {
        foreach ((Line line, _) in _edgeViews)
        {
            line.Opacity = 1;
        }

        foreach ((_, (Border node, Color color)) in _nodeViews)
        {
            node.Opacity = 1;
            node.BorderThickness = new Thickness(1.7);
            node.Background = new SolidColorBrush(color) { Opacity = 0.38 };
        }
    }

    private void AddGridBackground(double width, double height)
    {
        SolidColorBrush brush = new(Color.Parse("#35425D")) { Opacity = 0.2 };
        for (double x = 50; x < width; x += 100)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0), EndPoint = new Point(x, height),
                Stroke = brush, StrokeThickness = 0.6, IsHitTestVisible = false
            });
        }

        for (double y = 50; y < height; y += 100)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y), EndPoint = new Point(width, y),
                Stroke = brush, StrokeThickness = 0.6, IsHitTestVisible = false
            });
        }
    }

    public static Color GetKindColor(GitFileKind kind) => kind switch
    {
        GitFileKind.Code => Color.Parse("#55A7F3"),
        GitFileKind.UnrealAsset => Color.Parse("#A477D4"),
        GitFileKind.Texture => Color.Parse("#E56B91"),
        GitFileKind.Model => Color.Parse("#E5A94D"),
        GitFileKind.Audio => Color.Parse("#36C5A1"),
        GitFileKind.Document => Color.Parse("#B9C3D8"),
        GitFileKind.Configuration => Color.Parse("#6D7EA5"),
        _ => Color.Parse("#8D96AC")
    };

    private void AddEmptyMessage(string message)
    {
        _canvas.Width = 1000;
        _canvas.Height = 520;
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
}
