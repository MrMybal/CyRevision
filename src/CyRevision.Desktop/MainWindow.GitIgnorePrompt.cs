using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private async Task<GitIgnorePromptChoice> ShowMissingGitIgnoreRecommendationAsync(string projectRoot)
    {
        GitIgnoreRecommendation recommendation = GitIgnoreRecommendationService.Build(projectRoot);
        TextBox editor = new()
        {
            Text = recommendation.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 12,
            Height = 310,
            Background = new SolidColorBrush(Color.Parse("#090F1E")),
            Foreground = new SolidColorBrush(Color.Parse("#DDE3F0"))
        };
        editor.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        editor.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        Button cancel = new()
        {
            Content = Translate("Cancel"),
            Padding = new Thickness(15, 8),
            Background = new SolidColorBrush(Color.Parse("#202941"))
        };
        Button continueWithout = new()
        {
            Content = Translate("Continue without .gitignore"),
            Padding = new Thickness(15, 8),
            Background = new SolidColorBrush(Color.Parse("#2C3547"))
        };
        Button create = new()
        {
            Content = Translate("Create recommended .gitignore"),
            Padding = new Thickness(15, 8),
            Background = new SolidColorBrush(Color.Parse("#3468E9")),
            Foreground = Brushes.White
        };

        Window dialog = new()
        {
            Title = Translate("No .gitignore found"),
            Width = 760,
            Height = 650,
            MinWidth = 620,
            MinHeight = 520,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#10172A")),
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = Translate("No .gitignore found"),
                        FontSize = 19,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#F1C36A"))
                    },
                    new TextBlock
                    {
                        Text = Translate("This project does not contain a .gitignore. Git can accidentally include generated files, caches, IDE state, and machine-local data."),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#DDE3F0"))
                    },
                    new TextBlock
                    {
                        Text = projectRoot,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.Parse("#9B9DA3"))
                    },
                    new Border
                    {
                        Padding = new Thickness(11, 8),
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(Color.Parse("#17223A")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#2D3A4E")),
                        BorderThickness = new Thickness(1),
                        Child = new TextBlock
                        {
                            Text = $"{Translate("Detected project type:")} {Translate(recommendation.DetectionSummary)}",
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Color.Parse("#78D7B7"))
                        }
                    },
                    new TextBlock
                    {
                        Text = Translate("Review and edit the proposal before creating it. Nothing is written until you confirm."),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#AAB5C8"))
                    },
                    new TextBlock
                    {
                        Text = Translate("Recommended .gitignore"),
                        FontWeight = FontWeight.SemiBold
                    },
                    editor,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 9,
                        Children = { cancel, continueWithout, create }
                    }
                }
            }
        };

        cancel.Click += (_, _) => dialog.Close(new GitIgnorePromptChoice(
            GitIgnorePromptAction.Cancel,
            string.Empty));
        continueWithout.Click += (_, _) => dialog.Close(new GitIgnorePromptChoice(
            GitIgnorePromptAction.ContinueWithout,
            string.Empty));
        create.Click += (_, _) => dialog.Close(new GitIgnorePromptChoice(
            GitIgnorePromptAction.CreateRecommended,
            editor.Text ?? string.Empty));

        GitIgnorePromptChoice? result = await dialog.ShowDialog<GitIgnorePromptChoice?>(this);
        return result ?? new GitIgnorePromptChoice(GitIgnorePromptAction.Cancel, string.Empty);
    }

    private enum GitIgnorePromptAction
    {
        Cancel,
        ContinueWithout,
        CreateRecommended
    }

    private sealed record GitIgnorePromptChoice(GitIgnorePromptAction Action, string Content);
}
