using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Desktop.Localization;

namespace CyRevision.Desktop.Controls;

/// <summary>Help launch surface only: CyDoc owns the reader, search, media and navigation.</summary>
public sealed class CyDocHelpView : UserControl
{
    public event EventHandler<string>? OpenRequested;
    public CyDocHelpView()
    {
        var language = new ComboBox { MinWidth = 150,
            ItemTemplate = new FuncDataTemplate<LanguageOption>((item, _) => new TextBlock { Text = item?.NativeName }) };
        language.Bind(ItemsControl.ItemsSourceProperty, new Binding("Languages"));
        language.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedLanguage") { Mode = BindingMode.TwoWay });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, id) in new[] { ("Git / LFS", "git-workflow"), ("PR / CI", "ci-actions"), ("Preferences", "preferences"), ("Technical references", "technical-references") })
            actions.Children.Add(OpenButton(label, id));
        Content = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = "CyDoc documentation", FontSize = 25, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Open the complete local guide in your browser. No server or Internet connection is required. Images enlarge inside the reader.", TextWrapping = TextWrapping.Wrap, FontSize = 14 },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { language, OpenButton("Open user guide", "getting-started") } },
            actions,
            new TextBlock { Text = "F1 opens help for the current view. Search, history, language and reading positions are managed by CyDoc.", TextWrapping = TextWrapping.Wrap }
        }};
    }
    private Button OpenButton(string label, string id)
    {
        var button = new Button { Content = label, Tag = id, Margin = new Thickness(0,0,8,8) };
        button.Bind(IsEnabledProperty, new Binding("IsDocumentationAvailable"));
        button.Click += (_, _) => OpenRequested?.Invoke(this, id);
        return button;
    }
}
