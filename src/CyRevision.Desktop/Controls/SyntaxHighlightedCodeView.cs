using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CyRevision.Desktop.Controls;

public sealed class SyntaxHighlightedCodeView : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, string>(
        nameof(Text), string.Empty);

    public static readonly StyledProperty<string> FilePathProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, string>(
        nameof(FilePath), string.Empty);

    public static readonly StyledProperty<string> TargetFilePathProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, string>(
        nameof(TargetFilePath), string.Empty);

    public static readonly StyledProperty<int> TargetLineProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, int>(
        nameof(TargetLine));

    public static readonly StyledProperty<int> TargetColumnProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, int>(
        nameof(TargetColumn));

    public static readonly StyledProperty<int> TargetLengthProperty = AvaloniaProperty.Register<SyntaxHighlightedCodeView, int>(
        nameof(TargetLength));

    private static readonly IBrush PlainBrush = Brush("#A9B7C6");
    private static readonly IBrush KeywordBrush = Brush("#CC7832");
    private static readonly IBrush TypeBrush = Brush("#FFC66D");
    private static readonly IBrush StringBrush = Brush("#6A8759");
    private static readonly IBrush CommentBrush = Brush("#7F8C8D");
    private static readonly IBrush NumberBrush = Brush("#6897BB");
    private static readonly IBrush MemberBrush = Brush("#9876AA");
    private readonly TextBlock _lineNumbers;
    private readonly SelectableTextBlock _code;
    private readonly ScrollViewer _scrollViewer;

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
        _scrollViewer = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brush("#0F1115")
        };
        Content = _scrollViewer;
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

    public string TargetFilePath
    {
        get => GetValue(TargetFilePathProperty);
        set => SetValue(TargetFilePathProperty, value);
    }

    public int TargetLine
    {
        get => GetValue(TargetLineProperty);
        set => SetValue(TargetLineProperty, value);
    }

    public int TargetColumn
    {
        get => GetValue(TargetColumnProperty);
        set => SetValue(TargetColumnProperty, value);
    }

    public int TargetLength
    {
        get => GetValue(TargetLengthProperty);
        set => SetValue(TargetLengthProperty, value);
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
        else if (change.Property == TargetFilePathProperty || change.Property == TargetLineProperty ||
                 change.Property == TargetColumnProperty || change.Property == TargetLengthProperty)
        {
            UpdateTargetLocation();
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
        UpdateTargetLocation();
    }

    private void UpdateTargetLocation()
    {
        Dispatcher.UIThread.Post(() =>
        {
            string text = Text ?? string.Empty;
            string currentPath = (FilePath ?? string.Empty).Replace('\\', '/');
            string targetPath = (TargetFilePath ?? string.Empty).Replace('\\', '/');
            if (text.Length == 0 || TargetLine <= 0 ||
                !string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int line = Math.Max(1, TargetLine);
            int lineStart = 0;
            for (int currentLine = 1; currentLine < line && lineStart < text.Length; currentLine++)
            {
                int nextLine = text.IndexOf('\n', lineStart);
                if (nextLine < 0) return;
                lineStart = nextLine + 1;
            }

            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;
            int selectionStart = Math.Clamp(lineStart + Math.Max(0, TargetColumn - 1), lineStart, lineEnd);
            int selectionEnd = Math.Clamp(selectionStart + Math.Max(1, TargetLength), selectionStart, lineEnd);
            _code.SelectionStart = selectionStart;
            _code.SelectionEnd = selectionEnd;

            double estimatedLineHeight = _code.FontSize * 1.55;
            double centeredOffset = (line - 1) * estimatedLineHeight - (_scrollViewer.Viewport.Height * 0.35);
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Max(0, centeredOffset));
        }, DispatcherPriority.Background);
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
