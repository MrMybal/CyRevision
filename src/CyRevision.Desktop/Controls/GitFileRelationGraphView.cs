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

    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };

    public GitFileRelationGraphView()
    {
        Content = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
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
        Point[] positions = CreateLayout(files, relations, indexes, width, height);

        foreach (GitFileRelation relation in relations.OrderBy(item => item.CoChangeCount))
        {
            if (!indexes.TryGetValue(relation.SourcePath, out int sourceIndex) ||
                !indexes.TryGetValue(relation.TargetPath, out int targetIndex))
            {
                continue;
            }

            double strength = Math.Min(1, 0.18 + Math.Log2(relation.CoChangeCount + 1) * 0.17);
            Line edge = new()
            {
                StartPoint = positions[sourceIndex],
                EndPoint = positions[targetIndex],
                Stroke = new SolidColorBrush(Color.Parse("#6D7EA5")) { Opacity = strength },
                StrokeThickness = 0.8 + Math.Min(4.2, Math.Log2(relation.CoChangeCount + 1))
            };
            ToolTip.SetTip(edge, $"Modifiés ensemble {relation.CoChangeCount} fois");
            _canvas.Children.Add(edge);
        }

        int maximumChanges = Math.Max(1, files.Max(file => file.ChangeCount));
        foreach ((GitFileActivity file, int index) in files.Select((file, index) => (file, index)))
        {
            double scale = Math.Log2(file.ChangeCount + 1) / Math.Log2(maximumChanges + 1);
            double nodeWidth = 118 + 58 * scale;
            double nodeHeight = 52 + 18 * scale;
            Border node = CreateNode(file, nodeWidth, nodeHeight);
            Canvas.SetLeft(node, positions[index].X - nodeWidth / 2);
            Canvas.SetTop(node, positions[index].Y - nodeHeight / 2);
            _canvas.Children.Add(node);
        }
    }

    private static Point[] CreateLayout(
        IReadOnlyList<GitFileActivity> files,
        IReadOnlyList<GitFileRelation> relations,
        IReadOnlyDictionary<string, int> indexes,
        double width,
        double height)
    {
        Point[] positions = new Point[files.Count];
        Vector[] velocities = new Vector[files.Count];
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(files.Count * width / height)));
        int rows = (int)Math.Ceiling(files.Count / (double)columns);
        double horizontalSpacing = (width - 250) / Math.Max(1, columns - 1);
        double verticalSpacing = (height - 150) / Math.Max(1, rows - 1);
        for (int index = 0; index < files.Count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            if (row % 2 == 1)
            {
                column = columns - 1 - column;
            }

            positions[index] = new Point(
                125 + column * horizontalSpacing,
                75 + row * verticalSpacing);
        }

        List<(int Source, int Target, int Weight)> edges = relations
            .Where(relation => indexes.ContainsKey(relation.SourcePath) && indexes.ContainsKey(relation.TargetPath))
            .Select(relation => (indexes[relation.SourcePath], indexes[relation.TargetPath], relation.CoChangeCount))
            .ToList();
        for (int iteration = 0; iteration < 150; iteration++)
        {
            for (int left = 0; left < positions.Length; left++)
            {
                for (int right = left + 1; right < positions.Length; right++)
                {
                    Vector delta = positions[left] - positions[right];
                    double squared = Math.Max(400, delta.X * delta.X + delta.Y * delta.Y);
                    double distance = Math.Sqrt(squared);
                    Vector force = delta / distance * (52000 / squared);
                    velocities[left] += force;
                    velocities[right] -= force;

                    double overlapX = 190 - Math.Abs(delta.X);
                    double overlapY = 88 - Math.Abs(delta.Y);
                    if (overlapX > 0 && overlapY > 0)
                    {
                        if (overlapX / 190 < overlapY / 88)
                        {
                            double direction = delta.X >= 0 ? 1 : -1;
                            Vector collision = new(direction * overlapX * 0.12, 0);
                            velocities[left] += collision;
                            velocities[right] -= collision;
                        }
                        else
                        {
                            double direction = delta.Y >= 0 ? 1 : -1;
                            Vector collision = new(0, direction * overlapY * 0.16);
                            velocities[left] += collision;
                            velocities[right] -= collision;
                        }
                    }
                }
            }

            foreach ((int source, int target, int weight) in edges)
            {
                Vector delta = positions[target] - positions[source];
                double distance = Math.Max(1, Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y));
                double desired = 230 - Math.Min(55, weight * 4);
                Vector force = delta / distance * ((distance - desired) * 0.0018 * Math.Min(3, Math.Sqrt(weight)));
                velocities[source] += force;
                velocities[target] -= force;
            }

            for (int index = 0; index < positions.Length; index++)
            {
                Vector centerForce = new(width / 2 - positions[index].X, height / 2 - positions[index].Y);
                velocities[index] = (velocities[index] + centerForce * 0.00025) * 0.72;
                double speed = Math.Sqrt(velocities[index].X * velocities[index].X + velocities[index].Y * velocities[index].Y);
                if (speed > 15)
                {
                    velocities[index] = velocities[index] / speed * 15;
                }
                Point next = positions[index] + velocities[index];
                positions[index] = new Point(
                    Math.Clamp(next.X, 105, width - 105),
                    Math.Clamp(next.Y, 65, height - 65));
            }
        }

        return positions;
    }

    private static Border CreateNode(GitFileActivity file, double width, double height)
    {
        TextBlock name = new()
        {
            Text = System.IO.Path.GetFileName(file.Path),
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 18,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        TextBlock count = new()
        {
            Text = $"{file.ChangeCount} modification{(file.ChangeCount > 1 ? "s" : string.Empty)}",
            Foreground = new SolidColorBrush(Color.Parse("#D9DEF0")),
            FontSize = 9,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        Color color = GetKindColor(file.Kind);
        Border node = new()
        {
            Width = width,
            Height = height,
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(height / 2),
            Background = new SolidColorBrush(color) { Opacity = 0.28 },
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(1.4),
            Child = new StackPanel { Spacing = 3, Children = { name, count } }
        };
        ToolTip.SetTip(node,
            $"{file.Path}\nType : {file.Kind}\nCommits modifiant le fichier : {file.ChangeCount}\n" +
            $"Lignes : +{file.AddedLines} / -{file.DeletedLines}\nModifications binaires : {file.BinaryChangeCount}\n" +
            $"Dernière modification : {file.LastChangedAt.LocalDateTime:g}");
        node.PointerPressed += (_, _) =>
        {
            node.Background = new SolidColorBrush(color) { Opacity = 0.55 };
            node.BorderThickness = new Thickness(2.5);
        };
        return node;
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
