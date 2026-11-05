using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using CyRevision.Desktop.Controls;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Localization;

namespace CyRevision.Guide.UiChecks;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<GuideCheckApp>().UsePlatformDetect()
        .WithInterFont().StartWithClassicDesktopLifetime(args);
}

public sealed class GuideCheckApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string output = desktop.Args?.FirstOrDefault() ?? throw new ArgumentException("Pass a capture output directory.");
            Directory.CreateDirectory(output);
            var window = new Window { Title = "CyRevision CyDoc help integration", Width = 1000, Height = 600, Background = new SolidColorBrush(Color.Parse("#242424")), Foreground = Brushes.White };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                try
                {
                    var service = new OfflineDocumentationService(Path.Combine(AppContext.BaseDirectory, "Help"));
                    var localization = new LocalizationService();
                    string profile = Path.Combine(output, "localization-fixture");
                    Directory.CreateDirectory(profile);
                    localization.Configure(profile);
                    foreach (string language in new[] { "en", "fr" })
                    {
                        localization.SetLanguage(language);
                        var view = new CyDocHelpView { DataContext = new HelpState { SelectedLanguage = new(language, language == "en" ? "English" : "Français") }, Margin = new Thickness(24) };
                        List<string> urls = [];
                        // Capture host callbacks without launching a browser; file:// transport needs target-browser verification.
                        view.OpenRequested += (_, id) => urls.Add(service.CreateLink(id, language));
                        window.Content = view;
                        using var localizer = new UiLocalizer(window, localization);
                        if (language == "fr" && localization.Translate("Open user guide") == "Open user guide")
                            throw new InvalidOperationException("The French launch-surface catalog is missing.");
                        var expected = new HashSet<string> { "getting-started", "git-workflow", "ci-actions", "preferences", "technical-references" };
                        Button[] buttons = [];
                        for (int attempt = 0; attempt < 40; attempt++)
                        {
                            await Task.Delay(100);
                            buttons = view.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is string id && expected.Contains(id)).ToArray();
                            if (buttons.Length == 5 && (language != "fr" || !buttons.Any(button => Equals(button.Content, "Open user guide")))) break;
                        }
                        if (buttons.Length != 5) throw new InvalidOperationException($"Classic help has {buttons.Length}/5 entry points in {language}.");
                        if (language == "fr" && buttons.Any(button => Equals(button.Content, "Open user guide")))
                            throw new InvalidOperationException("The Classic help launch surface was not translated.");
                        foreach (var button in buttons) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (urls.Count != 5 || urls.Any(url => !new Uri(url).IsFile || !url.Contains("#/" + language + "/")))
                            throw new InvalidOperationException("Classic help did not produce correctly localized file URLs.");
                        using var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                        image.Render(window); image.Save(Path.Combine(output, $"classic-cydoc-{language}.png"), new PngBitmapEncoderOptions());
                    }
                    Console.WriteLine("PASS: Classic CyDoc entry points, locales and file URLs (no browser launched).");
                    desktop.Shutdown(0);
                }
                catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
            };
            window.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public sealed class HelpState
    {
        public bool IsDocumentationAvailable => true;
        public IReadOnlyList<LanguageOption> Languages { get; } = [new("en", "English"), new("fr", "Français")];
        public LanguageOption? SelectedLanguage { get; set; }
    }
}
