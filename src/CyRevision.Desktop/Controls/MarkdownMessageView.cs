using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyRevision.Desktop.Controls;

/// <summary>
/// Lightweight, dependency-free Markdown presentation for chat messages. It deliberately
/// supports the structures commonly streamed by coding agents (headings, lists, quotes and
/// fenced code) while preserving the original text when rendering is disabled.
/// </summary>
public sealed class MarkdownMessageView : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarkdownMessageView, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<bool> RenderMarkdownProperty =
        AvaloniaProperty.Register<MarkdownMessageView, bool>(nameof(RenderMarkdown), true);

    public MarkdownMessageView()
    {
        Rebuild();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool RenderMarkdown
    {
        get => GetValue(RenderMarkdownProperty);
        set => SetValue(RenderMarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == RenderMarkdownProperty) Rebuild();
    }

    private void Rebuild()
    {
        string source = Text ?? string.Empty;
        if (!RenderMarkdown)
        {
            Content = CreateText(source, 11, FontWeight.Normal, new FontFamily("Cascadia Mono, JetBrains Mono, Consolas"));
            return;
        }

        StackPanel panel = new() { Spacing = 4 };
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        List<string> code = [];
        bool inCode = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) AddCode(panel, code);
                inCode = !inCode;
                code.Clear();
                continue;
            }
            if (inCode)
            {
                code.Add(line);
                continue;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                panel.Children.Add(CreateText(trimmed[4..], 12, FontWeight.SemiBold));
            else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                panel.Children.Add(CreateText(trimmed[3..], 13, FontWeight.SemiBold));
            else if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                panel.Children.Add(CreateText(trimmed[2..], 14, FontWeight.Bold));
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                panel.Children.Add(CreateText("• " + trimmed[2..], 11, FontWeight.Normal));
            else if (trimmed.StartsWith("> ", StringComparison.Ordinal))
                panel.Children.Add(CreateText("│ " + trimmed[2..], 11, FontWeight.Normal));
            else
                panel.Children.Add(CreateText(line, 11, FontWeight.Normal));
        }
        if (code.Count > 0) AddCode(panel, code);
        Content = panel;
    }

    private static TextBlock CreateText(
        string text,
        double size,
        FontWeight weight,
        FontFamily? family = null)
    {
        TextBlock block = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (family is not null) block.FontFamily = family;
        return block;
    }

    private static void AddCode(Panel panel, IEnumerable<string> lines)
    {
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B1221")),
            BorderBrush = new SolidColorBrush(Color.Parse("#35445B")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = CreateText(string.Join(Environment.NewLine, lines), 10.5, FontWeight.Normal,
                new FontFamily("Cascadia Mono, JetBrains Mono, Consolas"))
        });
    }
}
