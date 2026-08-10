using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Diff;

namespace CyRevision.Desktop.Controls;

public sealed class CodeDiffView : UserControl
{
    public static readonly StyledProperty<string?> DiffTextProperty =
        AvaloniaProperty.Register<CodeDiffView, string?>(nameof(DiffText));

    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<CodeDiffView, string?>(nameof(FilePath));

    private const int MaximumRenderedLines = 5000;
    private static readonly IBrush CanvasBrush = Brush("#0B1020");
    private static readonly IBrush ContextBrush = Brush("#101827");
    private static readonly IBrush AddedBrush = Brush("#123128");
    private static readonly IBrush RemovedBrush = Brush("#3A1D27");
    private static readonly IBrush HunkBrush = Brush("#172A3D");
    private static readonly IBrush HeaderBrush = Brush("#141D30");
    private static readonly IBrush EmptySideBrush = Brush("#0D1422");
    private static readonly IBrush TextBrush = Brush("#DDE3F0");
    private static readonly IBrush MutedBrush = Brush("#71809D");
    private static readonly IBrush LineNumberBrush = Brush("#66738E");
    private static readonly IBrush AddedAccentBrush = Brush("#78D7B7");
    private static readonly IBrush RemovedAccentBrush = Brush("#FF8E9E");
    private static readonly IBrush HunkAccentBrush = Brush("#62C6FF");
    private static readonly IBrush KeywordBrush = Brush("#C792EA");
    private static readonly IBrush TypeBrush = Brush("#82AAFF");
    private static readonly IBrush StringBrush = Brush("#C3E88D");
    private static readonly IBrush CommentBrush = Brush("#72839F");
    private static readonly IBrush NumberBrush = Brush("#F78C6C");
    private static readonly IBrush MemberBrush = Brush("#89DDFF");

    private readonly CodeDiffParser _parser = new();
    private readonly Grid _root = new();
    private readonly StackPanel _rowsPanel = new()
    {
        Spacing = 0,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _positionText;
    private readonly Button _unifiedButton;
    private readonly Button _splitButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly List<Border> _changeRows = [];
    private ParsedCodeDiff _parsed = new([], [], 0, 0, 0);
    private bool _splitMode;
    private int _currentChangeIndex = -1;

    public CodeDiffView()
    {
        _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        _unifiedButton = CreateToolbarButton("Unifié");
        _splitButton = CreateToolbarButton("Côte à côte");
        _previousButton = CreateToolbarButton("‹ Précédent");
        _nextButton = CreateToolbarButton("Suivant ›");
        _summaryText = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _positionText = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 12,
            MinWidth = 54,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _unifiedButton.Click += (_, _) => SetMode(false);
        _splitButton.Click += (_, _) => SetMode(true);
        _previousButton.Click += (_, _) => NavigateChange(-1);
        _nextButton.Click += (_, _) => NavigateChange(1);

        Grid toolbarGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,16,*,Auto,Auto,Auto")
        };
        AddToGrid(toolbarGrid, _unifiedButton, 0);
        AddToGrid(toolbarGrid, _splitButton, 1);
        AddToGrid(toolbarGrid, _summaryText, 3);
        AddToGrid(toolbarGrid, _previousButton, 4);
        AddToGrid(toolbarGrid, _positionText, 5);
        AddToGrid(toolbarGrid, _nextButton, 6);

        Border toolbar = new()
        {
            Background = Brush("#0D1422"),
            BorderBrush = Brush("#26334D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7),
            Child = toolbarGrid
        };
        Grid.SetRow(toolbar, 0);
        _root.Children.Add(toolbar);

        _scrollViewer = new ScrollViewer
        {
            Background = CanvasBrush,
            Content = _rowsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        _scrollViewer.SizeChanged += (_, _) => UpdateContentWidth();
        Grid.SetRow(_scrollViewer, 1);
        _root.Children.Add(_scrollViewer);

        Content = _root;
        UpdateModeButtons();
        Rebuild();
    }

    public string? DiffText
    {
        get => GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DiffTextProperty || change.Property == FilePathProperty)
        {
            Rebuild();
        }
    }

    private void SetMode(bool splitMode)
    {
        if (_splitMode == splitMode)
        {
            return;
        }

        _splitMode = splitMode;
        _currentChangeIndex = -1;
        UpdateModeButtons();
        RenderRows();
    }

    private void Rebuild()
    {
        _parsed = _parser.Parse(DiffText);
        _currentChangeIndex = -1;
        _summaryText.Text = $"+{_parsed.AddedLineCount}  −{_parsed.RemovedLineCount}  ·  {_parsed.HunkCount} @@";
        RenderRows();
    }

    private void RenderRows()
    {
        _rowsPanel.Children.Clear();
        _changeRows.Clear();

        if (_parsed.Lines.Count == 0)
        {
            _rowsPanel.Children.Add(new TextBlock
            {
                Text = "Aucun diff à afficher.",
                Foreground = MutedBrush,
                Margin = new Thickness(18),
                FontStyle = FontStyle.Italic
            });
            UpdateNavigation();
            return;
        }

        if (_splitMode)
        {
            AddSplitHeader();
            foreach (CodeDiffSplitRow row in _parsed.SplitRows.Take(MaximumRenderedLines))
            {
                AddSplitRow(row);
            }
        }
        else
        {
            foreach (CodeDiffLine line in _parsed.Lines.Take(MaximumRenderedLines))
            {
                AddUnifiedRow(line);
            }
        }

        int renderedCount = _splitMode ? _parsed.SplitRows.Count : _parsed.Lines.Count;
        if (renderedCount > MaximumRenderedLines)
        {
            _rowsPanel.Children.Add(new Border
            {
                Background = HunkBrush,
                Padding = new Thickness(14, 8),
                Child = new TextBlock
                {
                    Text = $"Diff trop volumineux : affichage limité aux {MaximumRenderedLines:N0} premières lignes.",
                    Foreground = HunkAccentBrush
                }
            });
        }

        UpdateNavigation();
        UpdateContentWidth();
    }

    private void AddSplitHeader()
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,1,*"),
            Height = 28,
            Background = Brush("#17191D")
        };
        AddToGrid(header, CreatePaneHeader("Avant", FilePath), 0);
        AddToGrid(header, new Border { Background = Brush("#4B4D52") }, 1);
        AddToGrid(header, CreatePaneHeader("Après", FilePath), 2);
        _rowsPanel.Children.Add(header);
    }

    private static Border CreatePaneHeader(string label, string? filePath)
    {
        Grid content = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        AddToGrid(content, new TextBlock
        {
            Text = label,
            Foreground = Brush("#AFB1B7"),
            FontSize = 10.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        }, 0);
        AddToGrid(content, new TextBlock
        {
            Text = filePath ?? string.Empty,
            Foreground = MutedBrush,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        }, 1);
        return new Border
        {
            BorderBrush = Brush("#303238"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(9, 0),
            Child = content
        };
    }

    private void AddUnifiedRow(CodeDiffLine line)
    {
        if (line.Kind is CodeDiffLineKind.HunkHeader or CodeDiffLineKind.FileHeader or CodeDiffLineKind.Metadata)
        {
            _rowsPanel.Children.Add(CreateFullWidthRow(line));
            return;
        }

        Grid rowGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("48,48,20,*"),
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AddToGrid(rowGrid, CreateLineNumber(line.OldLineNumber), 0);
        AddToGrid(rowGrid, CreateLineNumber(line.NewLineNumber), 1);
        AddToGrid(rowGrid, new TextBlock
        {
            Text = line.Kind switch
            {
                CodeDiffLineKind.Added => "+",
                CodeDiffLineKind.Removed => "−",
                _ => " "
            },
            Foreground = GetAccent(line.Kind),
            FontFamily = MonospaceFont(),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        }, 2);
        AddToGrid(rowGrid, CreateCodeText(line.Content), 3);

        Border row = WrapRow(rowGrid, line.Kind);
        _rowsPanel.Children.Add(row);
        if (line.IsChange)
        {
            _changeRows.Add(row);
        }
    }

    private void AddSplitRow(CodeDiffSplitRow row)
    {
        if (row.FullWidth is not null)
        {
            _rowsPanel.Children.Add(CreateFullWidthRow(row.FullWidth));
            return;
        }

        Grid rowGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,1,*"),
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AddToGrid(rowGrid, CreateSplitCell(row.Left, true), 0);
        AddToGrid(rowGrid, new Border { Background = Brush("#34415C") }, 1);
        AddToGrid(rowGrid, CreateSplitCell(row.Right, false), 2);

        Border wrapper = new()
        {
            BorderBrush = Brush("#1C2940"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = rowGrid
        };
        _rowsPanel.Children.Add(wrapper);
        if (row.Left?.Kind == CodeDiffLineKind.Removed || row.Right?.Kind == CodeDiffLineKind.Added)
        {
            _changeRows.Add(wrapper);
        }
    }

    private Control CreateSplitCell(CodeDiffLine? line, bool oldSide)
    {
        if (line is null)
        {
            return new Border
            {
                Background = EmptySideBrush,
                Height = 22
            };
        }

        Grid cell = new()
        {
            ColumnDefinitions = new ColumnDefinitions("48,20,*"),
            ClipToBounds = true
        };
        AddToGrid(cell, CreateLineNumber(oldSide ? line.OldLineNumber : line.NewLineNumber), 0);
        AddToGrid(cell, new TextBlock
        {
            Text = line.Kind switch
            {
                CodeDiffLineKind.Added => "+",
                CodeDiffLineKind.Removed => "−",
                _ => " "
            },
            Foreground = GetAccent(line.Kind),
            FontFamily = MonospaceFont(),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, 1);
        AddToGrid(cell, CreateCodeText(line.Content), 2);

        return new Border
        {
            Background = GetBackground(line.Kind),
            ClipToBounds = true,
            Child = cell
        };
    }

    private Border CreateFullWidthRow(CodeDiffLine line)
    {
        IBrush foreground = line.Kind switch
        {
            CodeDiffLineKind.HunkHeader => HunkAccentBrush,
            CodeDiffLineKind.FileHeader => TypeBrush,
            _ => MutedBrush
        };
        return new Border
        {
            Background = line.Kind == CodeDiffLineKind.HunkHeader ? HunkBrush : HeaderBrush,
            BorderBrush = Brush("#26334D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(9, 3),
            MinHeight = 23,
            Child = new TextBlock
            {
                Text = line.Content,
                Foreground = foreground,
                FontFamily = MonospaceFont(),
                FontSize = 11.5,
                TextWrapping = TextWrapping.NoWrap
            }
        };
    }

    private Border WrapRow(Control child, CodeDiffLineKind kind)
    {
        return new Border
        {
            Background = GetBackground(kind),
            BorderBrush = Brush("#1C2940"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = child
        };
    }

    private TextBlock CreateLineNumber(int? lineNumber)
    {
        return new TextBlock
        {
            Text = lineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Foreground = LineNumberBrush,
            Background = Brush("#0D1422"),
            FontFamily = MonospaceFont(),
            FontSize = 10.5,
            Padding = new Thickness(6, 2),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private TextBlock CreateCodeText(string text)
    {
        TextBlock block = new()
        {
            Foreground = TextBrush,
            FontFamily = MonospaceFont(),
            FontSize = 11.5,
            Padding = new Thickness(6, 2, 12, 2),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (HighlightedToken token in CodeHighlighter.Highlight(text, FilePath))
        {
            block.Inlines?.Add(new Run(token.Text) { Foreground = GetTokenBrush(token.Kind) });
        }

        if (block.Inlines is null || block.Inlines.Count == 0)
        {
            block.Text = text;
        }

        ToolTip.SetTip(block, text);

        return block;
    }

    private void UpdateContentWidth()
    {
        double viewportWidth = _scrollViewer.Viewport.Width;
        if (viewportWidth <= 1)
        {
            viewportWidth = _scrollViewer.Bounds.Width;
        }

        if (viewportWidth <= 1)
        {
            return;
        }

        double availableWidth = Math.Max(640, viewportWidth - 2);
        if (_splitMode)
        {
            _rowsPanel.Width = availableWidth;
            return;
        }

        int longestLine = _parsed.Lines.Count == 0
            ? 0
            : _parsed.Lines.Max(line => line.Content.Length);
        double codeWidth = Math.Min(4200, 130 + longestLine * 7.1);
        _rowsPanel.Width = Math.Max(availableWidth, codeWidth);
    }

    private void NavigateChange(int direction)
    {
        if (_changeRows.Count == 0)
        {
            return;
        }

        _currentChangeIndex = _currentChangeIndex < 0
            ? (direction > 0 ? 0 : _changeRows.Count - 1)
            : (_currentChangeIndex + direction + _changeRows.Count) % _changeRows.Count;

        Border selected = _changeRows[_currentChangeIndex];
        selected.BorderBrush = Brush("#44D7B6");
        selected.BorderThickness = new Thickness(2, 1);
        selected.BringIntoView();

        for (int index = 0; index < _changeRows.Count; index++)
        {
            if (index == _currentChangeIndex)
            {
                continue;
            }

            _changeRows[index].BorderBrush = Brush("#1C2940");
            _changeRows[index].BorderThickness = new Thickness(0, 0, 0, 1);
        }

        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        bool hasChanges = _changeRows.Count > 0;
        _previousButton.IsEnabled = hasChanges;
        _nextButton.IsEnabled = hasChanges;
        _positionText.Text = hasChanges
            ? (_currentChangeIndex < 0 ? $"— / {_changeRows.Count}" : $"{_currentChangeIndex + 1} / {_changeRows.Count}")
            : "0 / 0";
    }

    private void UpdateModeButtons()
    {
        ApplyModeButtonState(_unifiedButton, !_splitMode);
        ApplyModeButtonState(_splitButton, _splitMode);
    }

    private static void ApplyModeButtonState(Button button, bool selected)
    {
        button.Background = selected ? Brush("#203D50") : Brush("#121B2B");
        button.Foreground = selected ? Brush("#7DE2CC") : TextBrush;
        button.BorderBrush = selected ? Brush("#3BAA96") : Brush("#34415C");
    }

    private static Button CreateToolbarButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = Brush("#121B2B"),
            Foreground = TextBrush,
            BorderBrush = Brush("#34415C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static void AddToGrid(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static IBrush GetBackground(CodeDiffLineKind kind) => kind switch
    {
        CodeDiffLineKind.Added => AddedBrush,
        CodeDiffLineKind.Removed => RemovedBrush,
        _ => ContextBrush
    };

    private static IBrush GetAccent(CodeDiffLineKind kind) => kind switch
    {
        CodeDiffLineKind.Added => AddedAccentBrush,
        CodeDiffLineKind.Removed => RemovedAccentBrush,
        _ => MutedBrush
    };

    private static IBrush GetTokenBrush(HighlightKind kind) => kind switch
    {
        HighlightKind.Keyword => KeywordBrush,
        HighlightKind.Type => TypeBrush,
        HighlightKind.String => StringBrush,
        HighlightKind.Comment => CommentBrush,
        HighlightKind.Number => NumberBrush,
        HighlightKind.Member => MemberBrush,
        _ => TextBrush
    };

    private static FontFamily MonospaceFont() => new("Cascadia Mono,Consolas,Menlo,monospace");

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}

internal enum HighlightKind
{
    Plain,
    Keyword,
    Type,
    String,
    Comment,
    Number,
    Member
}

internal readonly record struct HighlightedToken(string Text, HighlightKind Kind);

internal static class CodeHighlighter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "break", "case", "catch", "class", "const", "continue",
        "default", "delegate", "do", "else", "enum", "event", "explicit", "export", "extends", "extern", "false",
        "finally", "fixed", "for", "foreach", "from", "function", "get", "global", "goto", "if", "implements",
        "implicit", "import", "in", "interface", "internal", "is", "let", "lock", "match", "namespace", "new",
        "null", "operator", "out", "override", "package", "params", "private", "protected", "public", "readonly",
        "record", "ref", "return", "sealed", "set", "sizeof", "static", "struct", "switch", "this", "throw",
        "throws", "transient", "true", "try", "typeof", "unchecked", "unsafe", "using", "var", "virtual", "void",
        "volatile", "while", "with", "yield", "def", "elif", "except", "lambda", "None", "pass", "raise", "self",
        "and", "or", "not", "fn", "impl", "mod", "mut", "pub", "trait", "where", "use"
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int", "long", "object", "sbyte",
        "short", "string", "uint", "ulong", "ushort", "nint", "nuint", "number", "boolean", "any", "unknown",
        "never", "usize", "isize", "u8", "u16", "u32", "u64", "i8", "i16", "i32", "i64", "f32", "f64"
    };

    public static IReadOnlyList<HighlightedToken> Highlight(string text, string? filePath)
    {
        string extension = Path.GetExtension(filePath ?? string.Empty).ToLowerInvariant();
        if (extension is ".xml" or ".xaml" or ".html" or ".htm" or ".svg")
        {
            return HighlightMarkup(text);
        }

        if (extension is ".md" or ".markdown")
        {
            return HighlightMarkdown(text);
        }

        bool hashStartsComment = extension is ".py" or ".ps1" or ".sh" or ".bash" or ".yml" or ".yaml" or ".toml";
        return HighlightCode(text, hashStartsComment);
    }

    private static IReadOnlyList<HighlightedToken> HighlightCode(string text, bool hashStartsComment)
    {
        List<HighlightedToken> tokens = [];
        int index = 0;
        while (index < text.Length)
        {
            if (StartsComment(text, index, hashStartsComment))
            {
                tokens.Add(new HighlightedToken(text[index..], HighlightKind.Comment));
                break;
            }

            char character = text[index];
            if (character is '"' or '\'' or '`')
            {
                int end = ReadQuoted(text, index, character);
                tokens.Add(new HighlightedToken(text[index..end], HighlightKind.String));
                index = end;
                continue;
            }

            if (char.IsDigit(character))
            {
                int end = index + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '.' or '_' or 'x' or 'X'))
                {
                    end++;
                }

                tokens.Add(new HighlightedToken(text[index..end], HighlightKind.Number));
                index = end;
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                int end = index + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                {
                    end++;
                }

                string identifier = text[index..end];
                HighlightKind kind = Keywords.Contains(identifier)
                    ? HighlightKind.Keyword
                    : Types.Contains(identifier)
                        ? HighlightKind.Type
                        : IsMember(text, index)
                            ? HighlightKind.Member
                            : HighlightKind.Plain;
                tokens.Add(new HighlightedToken(identifier, kind));
                index = end;
                continue;
            }

            int plainEnd = index + 1;
            while (plainEnd < text.Length &&
                   !char.IsLetterOrDigit(text[plainEnd]) &&
                   text[plainEnd] is not ('_' or '"' or '\'' or '`') &&
                   !StartsComment(text, plainEnd, hashStartsComment))
            {
                plainEnd++;
            }

            tokens.Add(new HighlightedToken(text[index..plainEnd], HighlightKind.Plain));
            index = plainEnd;
        }

        return tokens;
    }

    private static IReadOnlyList<HighlightedToken> HighlightMarkup(string text)
    {
        if (text.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
        {
            return [new HighlightedToken(text, HighlightKind.Comment)];
        }

        List<HighlightedToken> tokens = [];
        int index = 0;
        while (index < text.Length)
        {
            char character = text[index];
            if (character == '<')
            {
                int end = index + 1;
                while (end < text.Length && text[end] != '>' && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                tokens.Add(new HighlightedToken(text[index..end], HighlightKind.Type));
                index = end;
            }
            else if (character is '"' or '\'')
            {
                int end = ReadQuoted(text, index, character);
                tokens.Add(new HighlightedToken(text[index..end], HighlightKind.String));
                index = end;
            }
            else if (char.IsLetter(character))
            {
                int end = index + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is ':' or '-' or '_'))
                {
                    end++;
                }

                tokens.Add(new HighlightedToken(text[index..end], HighlightKind.Keyword));
                index = end;
            }
            else
            {
                int end = index + 1;
                while (end < text.Length && !char.IsLetter(text[end]) && text[end] is not ('"' or '\'' or '<'))
                {
                    end++;
                }

                tokens.Add(new HighlightedToken(text[index..end], HighlightKind.Plain));
                index = end;
            }
        }

        return tokens;
    }

    private static IReadOnlyList<HighlightedToken> HighlightMarkdown(string text)
    {
        string trimmed = text.TrimStart();
        if (trimmed.StartsWith('#') || trimmed.StartsWith('>'))
        {
            return [new HighlightedToken(text, HighlightKind.Keyword)];
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return [new HighlightedToken(text, HighlightKind.Type)];
        }

        return HighlightCode(text, false);
    }

    private static bool StartsComment(string text, int index, bool hashStartsComment)
    {
        return index < text.Length - 1 && text[index] == '/' && text[index + 1] is '/' or '*' ||
               hashStartsComment && index < text.Length && text[index] == '#';
    }

    private static int ReadQuoted(string text, int start, char quote)
    {
        bool escaped = false;
        for (int index = start + 1; index < text.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[index] == quote)
            {
                return index + 1;
            }
        }

        return text.Length;
    }

    private static bool IsMember(string text, int identifierStart)
    {
        int index = identifierStart - 1;
        while (index >= 0 && char.IsWhiteSpace(text[index]))
        {
            index--;
        }

        return index >= 0 && text[index] == '.';
    }
}
