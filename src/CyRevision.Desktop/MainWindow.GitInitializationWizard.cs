using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private async Task<GitInitializationOptions?> ShowGitInitializationWizardAsync(
        string projectRoot,
        bool lfsOnly = false)
    {
        string root = Path.GetFullPath(projectRoot);
        GitIgnoreRecommendation ignoreRecommendation = GitIgnoreRecommendationService.Build(root);
        GitLfsRecommendation lfsRecommendation = GitLfsRecommendationService.Build(root);
        bool hasGitIgnore = File.Exists(Path.Combine(root, ".gitignore"));
        bool hasGitRepository = Directory.Exists(Path.Combine(root, ".git"));
        IReadOnlyList<string> existingLfsPatterns = hasGitRepository || File.Exists(Path.Combine(root, ".gitattributes"))
            ? await _viewModel.GetGitAttributeLfsPatternsAsync(root)
            : [];
        GitToolAvailability? tools = null;
        string? toolError = null;
        try
        {
            tools = await _viewModel.GetGitToolAvailabilityAsync();
        }
        catch (Exception exception)
        {
            toolError = exception.Message;
        }

        SolidColorBrush foreground = Brush("#DDE3F0");
        SolidColorBrush muted = Brush("#9AA6BC");
        SolidColorBrush accent = Brush("#78D7B7");
        SolidColorBrush warning = Brush("#F1C36A");
        TextBlock pageTitle = new() { FontSize = 20, FontWeight = FontWeight.Bold, Foreground = foreground };
        TextBlock pageDescription = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = muted };
        TextBlock stepLabel = new() { FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = accent };
        Border pageHost = new()
        {
            Padding = new Thickness(2, 8, 2, 8),
            Background = Brushes.Transparent
        };
        TextBlock validation = new()
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = warning,
            IsVisible = false
        };

        CheckBox createGitIgnore = new()
        {
            Content = Translate("Create the recommended .gitignore"),
            IsChecked = !hasGitIgnore,
            IsEnabled = !hasGitIgnore
        };
        TextBox gitIgnoreEditor = new()
        {
            Text = hasGitIgnore
                ? await File.ReadAllTextAsync(Path.Combine(root, ".gitignore"))
                : ignoreRecommendation.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 11,
            MinHeight = 330,
            IsReadOnly = hasGitIgnore,
            Background = Brush("#090F1E"),
            Foreground = foreground
        };
        gitIgnoreEditor.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        gitIgnoreEditor.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        List<(GitLfsPatternRecommendation Recommendation, CheckBox CheckBox)> patternChecks = [];
        StackPanel patternRows = new() { Spacing = 2 };
        HashSet<string> existingPatternSet = new(existingLfsPatterns, StringComparer.OrdinalIgnoreCase);
        foreach (GitLfsPatternRecommendation item in lfsRecommendation.Patterns)
        {
            bool exists = existingPatternSet.Contains(item.Pattern);
            CheckBox checkBox = new()
            {
                IsChecked = exists || item.IsRecommended,
                IsEnabled = !exists,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid row = new()
            {
                Height = 31,
                ColumnDefinitions = new ColumnDefinitions("30,120,110,*,95"),
                Background = patternRows.Children.Count % 2 == 0 ? Brush("#171D2A") : Brushes.Transparent
            };
            row.Children.Add(checkBox);
            AddCell(row, 1, item.Pattern, foreground, FontWeight.SemiBold, "Cascadia Mono,Consolas");
            AddCell(row, 2, Translate(item.Category), muted);
            AddCell(row, 3, Translate(item.Reason), muted);
            AddCell(row, 4, exists ? Translate("Already tracked") : item.IsRecommended ? Translate("Recommended") : Translate("Optional"), exists ? accent : muted);
            patternRows.Children.Add(row);
            patternChecks.Add((item, checkBox));
        }
        TextBox customPatterns = new()
        {
            AcceptsReturn = true,
            MinHeight = 75,
            PlaceholderText = "*.bin\nContent/**/SourceArt/*.png",
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 11
        };

        TextBox userName = new() { PlaceholderText = Translate("Leave blank to inherit the existing Git identity") };
        TextBox userEmail = new() { PlaceholderText = "name@example.com" };
        TextBox remoteUrl = new() { PlaceholderText = "https://server/team/repository.git" };
        CheckBox initialCommit = new() { Content = Translate("Create an initial commit after reviewing the eligible files"), IsChecked = false };
        TextBox initialMessage = new() { Text = "Initial commit", IsEnabled = false };
        initialCommit.IsCheckedChanged += (_, _) => initialMessage.IsEnabled = initialCommit.IsChecked == true;
        TextBlock reviewSummary = new() { TextWrapping = TextWrapping.Wrap, Foreground = foreground };
        TextBlock previewSummary = new() { FontSize = 10, Foreground = muted };
        ListBox previewFiles = new()
        {
            Height = 250,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 10,
            Background = Brush("#090F1E")
        };
        ProgressBar previewProgress = new() { IsIndeterminate = true, Height = 3, IsVisible = false };

        Control BuildToolsPage()
        {
            StackPanel body = PageStack();
            body.Children.Add(InfoCard(
                Translate("Project folder"),
                root,
                foreground));
            body.Children.Add(InfoCard(
                Translate("Detected project type"),
                Translate(ignoreRecommendation.DetectionSummary),
                accent));
            string gitStatus = tools?.GitAvailable == true
                ? $"{Translate("Available")} · {tools.GitVersion}"
                : $"{Translate("Unavailable")} · {toolError ?? Translate("Git was not detected")}";
            string lfsStatus = tools?.LfsAvailable == true
                ? $"{Translate("Available")} · {tools.LfsVersion}"
                : $"{Translate("Unavailable")} · {Translate("Git LFS was not detected")}";
            body.Children.Add(InfoCard("Git", gitStatus, tools?.GitAvailable == true ? accent : warning));
            body.Children.Add(InfoCard("Git LFS", lfsStatus, tools?.LfsAvailable == true ? accent : warning));
            if (hasGitRepository && !lfsOnly)
                body.Children.Add(WarningCard(Translate("This folder is already a Git repository. Use Open repository or the Git LFS setup assistant instead.")));
            body.Children.Add(WarningCard(Translate("Nothing is written until the final confirmation. The wizard never pushes automatically."), false));
            return body;
        }

        Control BuildIgnorePage()
        {
            StackPanel body = PageStack();
            body.Children.Add(createGitIgnore);
            body.Children.Add(new TextBlock
            {
                Text = hasGitIgnore
                    ? Translate("An existing .gitignore was detected and will be preserved. Edit it later from Git > Ignore rules.")
                    : Translate("Review the generated rules. They only target generated data, caches, IDE state, and operating-system metadata."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = hasGitIgnore ? accent : muted,
                FontSize = 11
            });
            body.Children.Add(gitIgnoreEditor);
            return body;
        }

        Control BuildLfsPage()
        {
            Grid header = new() { ColumnDefinitions = new ColumnDefinitions("30,120,110,*,95"), Height = 28, Background = Brush("#0A0F1B") };
            AddCell(header, 1, Translate("Pattern"), muted, FontWeight.SemiBold);
            AddCell(header, 2, Translate("Category"), muted, FontWeight.SemiBold);
            AddCell(header, 3, Translate("Reason"), muted, FontWeight.SemiBold);
            AddCell(header, 4, Translate("Status"), muted, FontWeight.SemiBold);
            StackPanel body = PageStack();
            body.Children.Add(new TextBlock
            {
                Text = Translate("Selected patterns are merged into .gitattributes. Existing rules and unrelated attributes are never overwritten."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = accent,
                FontSize = 11
            });
            body.Children.Add(header);
            body.Children.Add(new ScrollViewer { Height = 305, Content = patternRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            body.Children.Add(new TextBlock { Text = Translate("Additional patterns (one per line or separated by semicolons)"), FontWeight = FontWeight.SemiBold });
            body.Children.Add(customPatterns);
            return body;
        }

        Control BuildIdentityPage()
        {
            StackPanel body = PageStack();
            body.Children.Add(new TextBlock
            {
                Text = Translate("Identity is stored only in this repository. Leave both fields blank to inherit the existing Git configuration."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = muted
            });
            body.Children.Add(FormField(Translate("Author name"), userName));
            body.Children.Add(FormField(Translate("Author email"), userEmail));
            body.Children.Add(FormField(Translate("Optional origin remote"), remoteUrl));
            body.Children.Add(WarningCard(Translate("Adding a remote only records its address. No fetch, pull, push, or network operation is performed."), false));
            return body;
        }

        Control BuildReviewPage()
        {
            StackPanel body = PageStack();
            body.Children.Add(reviewSummary);
            if (!lfsOnly)
            {
                body.Children.Add(initialCommit);
                body.Children.Add(initialMessage);
                body.Children.Add(previewProgress);
                body.Children.Add(previewSummary);
                body.Children.Add(previewFiles);
            }
            body.Children.Add(WarningCard(Translate("Finish applies the displayed configuration locally. It never pushes and never overwrites an existing .gitignore."), false));
            return body;
        }

        Control[] pages = [BuildToolsPage(), BuildIgnorePage(), BuildLfsPage(), BuildIdentityPage(), BuildReviewPage()];
        int[] pageOrder = lfsOnly ? [2, 4] : [0, 1, 2, 3, 4];
        string[] titles =
        [
            Translate("Check project and tools"),
            Translate("Prepare .gitignore"),
            Translate("Configure .gitattributes and Git LFS"),
            Translate("Configure identity and remote"),
            Translate("Review and initialize")
        ];
        string[] descriptions =
        [
            Translate("Confirm the target folder and the local tools that will be used."),
            Translate("Prevent generated and machine-local files from entering the repository."),
            Translate("Choose which large binary file types should be stored through Git LFS."),
            Translate("Optionally set a repository-local author identity and an origin remote."),
            Translate("Review every local change before CyRevision writes anything.")
        ];

        Button cancel = SecondaryButton(Translate("Cancel"));
        Button back = SecondaryButton(Translate("Back"));
        Button next = PrimaryButton(Translate("Next"));
        Window dialog = new()
        {
            Title = lfsOnly ? Translate("Git LFS setup assistant") : Translate("Initialize Git"),
            Width = 980,
            Height = 720,
            MinWidth = 760,
            MinHeight = 580,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#10172A")
        };
        Grid shell = new() { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        shell.Children.Add(new StackPanel { Spacing = 3, Children = { stepLabel, pageTitle, pageDescription } });
        Grid.SetRow(pageHost, 1);
        shell.Children.Add(pageHost);
        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { validation, cancel, back, next }
        };
        Grid.SetRow(footer, 2);
        shell.Children.Add(footer);
        dialog.Content = shell;

        int orderIndex = 0;
        CancellationTokenSource previewCancellation = new();
        bool previewLoaded = false;

        async Task UpdateReviewAsync()
        {
            string[] selectedPatterns = CollectPatterns(patternChecks, customPatterns.Text);
            reviewSummary.Text = lfsOnly
                ? $"{selectedPatterns.Length:N0} {Translate("Git LFS pattern(s) selected")} · {Translate("existing .gitattributes rules remain untouched")}."
                : string.Join('\n',
                    $"{Translate("Folder")}: {root}",
                    $".gitignore: {(createGitIgnore.IsChecked == true ? Translate("create recommended file") : Translate("leave unchanged"))}",
                    $"Git LFS: {selectedPatterns.Length:N0} {Translate("pattern(s)")}",
                    $"{Translate("Identity")}: {(string.IsNullOrWhiteSpace(userName.Text) ? Translate("inherit Git configuration") : userName.Text)}",
                    $"origin: {(string.IsNullOrWhiteSpace(remoteUrl.Text) ? Translate("not configured") : remoteUrl.Text)}");
            if (lfsOnly || previewLoaded) return;
            previewLoaded = true;
            previewProgress.IsVisible = true;
            previewSummary.Text = Translate("Estimating eligible files without modifying the folder…");
            try
            {
                GitInitializationFilePreview preview = await BuildGitInitializationPreviewAsync(root, previewCancellation.Token);
                previewFiles.ItemsSource = preview.SamplePaths;
                previewSummary.Text = $"{preview.FileCount:N0} {Translate("file(s)")} · {FormatBytes(preview.TotalBytes)}" +
                                      (preview.WasLimited ? $" · {Translate("estimate limited for responsiveness")}" : string.Empty);
                if (preview.FileCount == 0)
                {
                    initialCommit.IsChecked = false;
                    initialCommit.IsEnabled = false;
                }
            }
            catch (OperationCanceledException)
            {
                previewSummary.Text = Translate("Preview cancelled.");
            }
            finally
            {
                previewProgress.IsVisible = false;
            }
        }

        async Task ShowPageAsync()
        {
            int pageIndex = pageOrder[orderIndex];
            stepLabel.Text = $"{Translate("Step")} {orderIndex + 1} / {pageOrder.Length}";
            pageTitle.Text = titles[pageIndex];
            pageDescription.Text = descriptions[pageIndex];
            pageHost.Child = pages[pageIndex];
            back.IsEnabled = orderIndex > 0;
            next.Content = orderIndex == pageOrder.Length - 1 ? Translate("Finish") : Translate("Next");
            validation.IsVisible = false;
            if (pageIndex == 4) await UpdateReviewAsync();
        }

        bool ValidateCurrentPage()
        {
            validation.IsVisible = false;
            int pageIndex = pageOrder[orderIndex];
            try
            {
                if (pageIndex == 0)
                {
                    if (hasGitRepository)
                        throw new InvalidOperationException(Translate("The selected folder is already a Git repository."));
                    if (tools?.GitAvailable != true || tools.LfsAvailable != true)
                        throw new InvalidOperationException(Translate("Git and Git LFS must both be available before initialization."));
                }
                else if (pageIndex == 2)
                {
                    _ = CollectPatterns(patternChecks, customPatterns.Text);
                }
                else if (pageIndex == 3)
                {
                    bool hasName = !string.IsNullOrWhiteSpace(userName.Text);
                    bool hasEmail = !string.IsNullOrWhiteSpace(userEmail.Text);
                    if (hasName != hasEmail)
                        throw new InvalidOperationException(Translate("Enter both an author name and email, or leave both blank."));
                }
                else if (pageIndex == 4 && initialCommit.IsChecked == true && string.IsNullOrWhiteSpace(initialMessage.Text))
                {
                    throw new InvalidOperationException(Translate("Enter an initial commit message."));
                }
                return true;
            }
            catch (Exception exception)
            {
                validation.Text = exception.Message;
                validation.IsVisible = true;
                return false;
            }
        }

        cancel.Click += (_, _) => dialog.Close(null);
        back.Click += async (_, _) => { if (orderIndex > 0) { orderIndex--; await ShowPageAsync(); } };
        next.Click += async (_, _) =>
        {
            if (!ValidateCurrentPage()) return;
            if (orderIndex < pageOrder.Length - 1)
            {
                orderIndex++;
                await ShowPageAsync();
                return;
            }
            string[] patterns = CollectPatterns(patternChecks, customPatterns.Text);
            dialog.Close(new GitInitializationOptions(
                !lfsOnly && !hasGitIgnore && createGitIgnore.IsChecked == true,
                gitIgnoreEditor.Text ?? string.Empty,
                patterns,
                lfsOnly ? string.Empty : userName.Text?.Trim() ?? string.Empty,
                lfsOnly ? string.Empty : userEmail.Text?.Trim() ?? string.Empty,
                lfsOnly ? string.Empty : remoteUrl.Text?.Trim() ?? string.Empty,
                !lfsOnly && initialCommit.IsChecked == true,
                initialMessage.Text?.Trim() ?? "Initial commit"));
        };
        dialog.Closed += (_, _) => previewCancellation.Cancel();
        await ShowPageAsync();
        GitInitializationOptions? result = await dialog.ShowDialog<GitInitializationOptions?>(this);
        previewCancellation.Dispose();
        return result;
    }

    private static string[] CollectPatterns(
        IEnumerable<(GitLfsPatternRecommendation Recommendation, CheckBox CheckBox)> checks,
        string? customText)
    {
        IEnumerable<string> selected = checks
            .Where(item => item.CheckBox.IsChecked == true)
            .Select(item => item.Recommendation.Pattern);
        IEnumerable<string> custom = (customText ?? string.Empty)
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return selected.Concat(custom)
            .Select(GitAttributesService.NormalizePattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Task<GitInitializationFilePreview> BuildGitInitializationPreviewAsync(
        string root,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        const int maximumFiles = 50_000;
        const int maximumSamples = 300;
        HashSet<string> excludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".cyrevision", ".vs", ".idea", "Binaries", "DerivedDataCache", "Intermediate",
            "Saved", "Library", "Temp", "Obj", "node_modules", "bin"
        };
        Stack<string> pending = new();
        pending.Push(root);
        List<string> sample = [];
        int count = 0;
        long totalBytes = 0;
        bool limited = false;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory))
                    if (!excludedDirectories.Contains(Path.GetFileName(child))) pending.Push(child);
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    count++;
                    try { totalBytes += new FileInfo(file).Length; } catch (IOException) { }
                    if (sample.Count < maximumSamples) sample.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                    if (count < maximumFiles) continue;
                    limited = true;
                    pending.Clear();
                    break;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return new GitInitializationFilePreview(count, totalBytes, sample, limited);
    }, cancellationToken);

    private static StackPanel PageStack() => new() { Spacing = 10 };

    private static Border InfoCard(string title, string detail, IBrush detailBrush) => new()
    {
        Padding = new Thickness(12, 9),
        CornerRadius = new CornerRadius(5),
        Background = Brush("#17223A"),
        Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("175,*"),
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Foreground = detailBrush, [Grid.ColumnProperty] = 1 }
            }
        }
    };

    private static Border WarningCard(string text, bool isWarning = true) => new()
    {
        Padding = new Thickness(11, 8),
        CornerRadius = new CornerRadius(5),
        Background = Brush(isWarning ? "#342817" : "#132A27"),
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(isWarning ? "#F1C36A" : "#78D7B7"),
            FontSize = 11
        }
    };

    private static StackPanel FormField(string label, Control editor) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontWeight = FontWeight.SemiBold }, editor }
    };

    private static void AddCell(
        Grid grid,
        int column,
        string text,
        IBrush brush,
        FontWeight? weight = null,
        string? fontFamily = null)
    {
        TextBlock cell = new()
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = brush,
            FontSize = 10.5,
            Margin = new Thickness(4, 0),
            FontWeight = weight ?? FontWeight.Normal
        };
        if (fontFamily is not null) cell.FontFamily = new FontFamily(fontFamily);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(15, 8),
        Background = Brush("#2C3547")
    };

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(15, 8),
        Background = Brush("#3468E9"),
        Foreground = Brushes.White,
        IsDefault = true
    };

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
