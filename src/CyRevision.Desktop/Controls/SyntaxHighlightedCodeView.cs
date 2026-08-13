using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyRevision.Desktop.Controls;

public sealed class SyntaxHighlightedCodeView : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, string>(
        nameof(Text), string.Empty);

    public static readonly StyledProperty<string> FilePathProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, string>(
        nameof(FilePath), string.Empty);

    private static readonly IBrush PlainBrush = Brush("#A9B7C6");
    private static readonly IBrush KeywordBrush = Brush("#CC7832");
    private static readonly IBrush TypeBrush = Brush("#FFC66D");
    private static readonly IBrush StringBrush = Brush("#6A8759");
    private static readonly IBrush CommentBrush = Brush("#7F8C8D");
    private static readonly IBrush NumberBrush = Brush("#6897BB");
    private static readonly IBrush MemberBrush = Brush("#9876AA");
    private readonly TextBlock _lineNumbers;
    private readonly SelectableTextBlock _code;

    public SyntaxHighlightedCodeView()
    {
        _lineNumbers = new TextBlock
        {
            FontFamily = MonospaceFont(),
            FontSize = 11.5,
            Foreground = Brush("#606A7B"),
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(8, 7, 7, 10),
            Background = Brush("#15181D")
        };
        _code = new SelectableTextBlock
        {
            FontFamily = MonospaceFont(),
            FontSize = 11.5,
            Foreground = PlainBrush,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(10, 7, 24, 10),
            Background = Brush("#0F1115"),
            SelectionBrush = Brush("#355A7C"),
            SelectionForegroundBrush = Brushes.White
        };

        Grid content = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        content.Children.Add(_lineNumbers);
        Grid.SetColumn(_code, 1);
        content.Children.Add(_code);
        Content = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brush("#0F1115")
        };
        UpdatePresentation();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public int SelectionStart => _code.SelectionStart;
    public int SelectionEnd => _code.SelectionEnd;
    public string SelectedText => _code.SelectedText;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FilePathProperty)
        {
            UpdatePresentation();
        }
    }

    private void UpdatePresentation()
    {
        string text = Text ?? string.Empty;
        int lineCount = text.Length == 0 ? 1 : text.Count(character => character == '\n') + 1;
        _lineNumbers.Text = string.Join('\n', Enumerable.Range(1, lineCount));

        InlineCollection inlines = new();
        int lineStart = 0;
        while (lineStart < text.Length)
        {
            int newLine = text.IndexOf('\n', lineStart);
            int contentEnd = newLine < 0 ? text.Length : newLine;
            int highlightEnd = contentEnd > lineStart && text[contentEnd - 1] == '\r' ? contentEnd - 1 : contentEnd;
            string line = text[lineStart..highlightEnd];
            foreach (HighlightedToken token in CodeHighlighter.Highlight(line, FilePath))
            {
                inlines.Add(new Run(token.Text) { Foreground = TokenBrush(token.Kind) });
            }

            if (highlightEnd < contentEnd)
            {
                inlines.Add(new Run("\r") { Foreground = PlainBrush });
            }
            if (newLine >= 0)
            {
                inlines.Add(new Run("\n") { Foreground = PlainBrush });
                lineStart = newLine + 1;
            }
            else
            {
                lineStart = text.Length;
            }
        }

        if (text.Length == 0)
        {
            inlines.Add(new Run(string.Empty) { Foreground = PlainBrush });
        }
        _code.Inlines = inlines;
    }

    private static IBrush TokenBrush(HighlightKind kind) => kind switch
    {
        HighlightKind.Keyword => KeywordBrush,
        HighlightKind.Type => TypeBrush,
        HighlightKind.String => StringBrush,
        HighlightKind.Comment => CommentBrush,
        HighlightKind.Number => NumberBrush,
        HighlightKind.Member => MemberBrush,
        _ => PlainBrush
    };

    private static FontFamily MonospaceFont() => new("Cascadia Mono,JetBrains Mono,Consolas,Menlo,monospace");
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
