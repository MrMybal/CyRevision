using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;
using CyRevision.Git;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private void OnOpenFocusedDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_focusedDiffWindow is not null)
        {
            if (_focusedDiffWindow.WindowState == WindowState.Minimized)
            {
                _focusedDiffWindow.WindowState = WindowState.Normal;
            }

            _focusedDiffWindow.Activate();
            return;
        }

        if (_localization is null)
        {
            return;
        }

        FocusedDiffWindow window = new(_viewModel, _localization);
        window.Closed += (_, _) => _focusedDiffWindow = null;
        _focusedDiffWindow = window;
        window.Show();
    }

    private void OnOpenChangesDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_localization is null || _viewModel.SelectedChange is null) return;
        if (_changesDiffWindow is not null)
        {
            _changesDiffWindow.Activate();
            return;
        }

        FocusedDiffWindow window = new(_viewModel, DiffWindowSource.WorkingTree, _localization);
        window.Closed += (_, _) => _changesDiffWindow = null;
        _changesDiffWindow = window;
        window.Show();
    }

    private void OnOpenGitConflictResolverClick(object? sender, RoutedEventArgs e) =>
        OpenGitConflictResolver();

    private void OnVersionedChangeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedChange?.Change.Kind == GitChangeKind.Conflicted)
            OpenGitConflictResolver();
    }

    private void OpenGitConflictResolver()
    {
        if (_viewModel.SelectedProject is not { } project ||
            !project.Definition.Features.GitEnabled) return;
        if (_gitConflictResolverWindow is not null)
        {
            if (_gitConflictResolverWindow.WindowState == WindowState.Minimized)
                _gitConflictResolverWindow.WindowState = WindowState.Normal;
            _gitConflictResolverWindow.Activate();
            return;
        }

        GitConflictResolverViewModel resolver = new(
            new GitCliRepositoryService(),
            project.RootPath,
            project.Name,
            new GitConflictResolutionBackupService(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyRevision",
                "conflict-resolutions")),
            _viewModel.CanUseAiConflictResolver
                ? _viewModel.GenerateAiConflictAssistanceAsync
                : null);
        GitConflictResolverWindow window = new(resolver, _localization);
        window.Closed += async (_, _) =>
        {
            _gitConflictResolverWindow = null;
            if (_viewModel.SelectedProject?.Id == project.Id) await _viewModel.RefreshAsync();
        };
        _gitConflictResolverWindow = window;
        window.Show();
    }

    private void OnOpenPullRequestDiffWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_localization is null || _viewModel.SelectedPullRequestFile is not { } file) return;
        if (_pullRequestDiffWindow is not null)
        {
            _pullRequestDiffWindow.Activate();
            return;
        }

        FocusedDiffWindow window = new(_viewModel, DiffWindowSource.PullRequest, _localization);
        window.Closed += (_, _) => _pullRequestDiffWindow = null;
        _pullRequestDiffWindow = window;
        window.Show();
    }

    private void OnOpenCommitExplorerWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_commitExplorerWindow is not null)
        {
            _commitExplorerWindow.ShowRevisions(_viewModel.History, _viewModel.SelectedExplorerRevision);
            if (_commitExplorerWindow.WindowState == WindowState.Minimized)
                _commitExplorerWindow.WindowState = WindowState.Normal;
            _commitExplorerWindow.Activate();
            return;
        }

        CommitExplorerWindow window = new(_viewModel);
        window.Closed += (_, _) => _commitExplorerWindow = null;
        _commitExplorerWindow = window;
        window.Show();
    }

    private void OnOpenSelectedBranchCommitClick(object? sender, RoutedEventArgs e) =>
        OpenSelectedBranchCommitExplorer();

    private void OnOpenSelectedBranchFilesClick(object? sender, RoutedEventArgs e)
    {
        if (_branchFileExplorerWindow is not null)
        {
            _branchFileExplorerWindow.Activate();
            return;
        }

        BranchFileExplorerViewModel? explorer = _viewModel.CreateBranchFileExplorer();
        if (explorer is null) return;
        _branchFileExplorerWindow = new BranchFileExplorerWindow(explorer);
        _branchFileExplorerWindow.Closed += (_, _) => _branchFileExplorerWindow = null;
        _branchFileExplorerWindow.Show();
    }

    private void OnSelectedBranchCommitDoubleTapped(object? sender, TappedEventArgs e) =>
        OpenSelectedBranchCommitExplorer();

    private void OpenSelectedBranchCommitExplorer()
    {
        GitRevision? selectedRevision = _viewModel.SelectedBranchRevision;
        if (selectedRevision is null)
        {
            return;
        }

        if (_commitExplorerWindow is null)
        {
            CommitExplorerWindow window = new(
                _viewModel,
                _viewModel.SelectedBranchHistory,
                selectedRevision);
            window.Closed += (_, _) => _commitExplorerWindow = null;
            _commitExplorerWindow = window;
            window.Show();
            return;
        }

        _commitExplorerWindow.ShowRevisions(_viewModel.SelectedBranchHistory, selectedRevision);
        if (_commitExplorerWindow.WindowState == WindowState.Minimized)
        {
            _commitExplorerWindow.WindowState = WindowState.Normal;
        }
        _commitExplorerWindow.Activate();
    }

    private void OnOpenDetachedHistoryClick(object? sender, RoutedEventArgs e) =>
        OpenDetachedWorkspace(DetachedWorkspaceSection.History);

    private void OnOpenDetachedCodeClick(object? sender, RoutedEventArgs e) =>
        OpenDetachedWorkspace(DetachedWorkspaceSection.Code);

    private void OnOpenDetachedMultiRestoreClick(object? sender, RoutedEventArgs e)
    {
        _ = _viewModel.LoadMultiRestoreCommitAsync(_viewModel.SelectedExplorerRevision);
        OpenDetachedWorkspace(DetachedWorkspaceSection.MultiRestore);
    }

    private void OnOpenDetachedCherryPickClick(object? sender, RoutedEventArgs e) =>
        OpenDetachedWorkspace(DetachedWorkspaceSection.CherryPick);

    private void OnDetachCurrentWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is TabItem tab) DetachWorkspace(tab);
    }

    private void ConfigureWorkspaceDetachGestures()
    {
        foreach (TabItem tab in WorkspaceTabs.Items.OfType<TabItem>())
        {
            tab.DoubleTapped += OnWorkspaceTabDoubleTapped;
            ToolTip.SetTip(tab, "Double-click to open this workspace in a new window");
        }

        foreach (ToggleButton category in new[]
                 {
                     OverviewCategoryToggle,
                     GitCategoryToggle,
                     CodeCategoryToggle,
                     BackupCategoryToggle,
                     SyncCategoryToggle,
                     PluginModeCategoryToggle,
                     NetworkCategoryToggle,
                     ExtensionsCategoryToggle
                 })
        {
            category.DoubleTapped += OnWorkspaceCategoryDoubleTapped;
            ToolTip.SetTip(category, "Double-click to detach the active workspace in this category");
        }
    }

    private void OnWorkspaceTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TabItem tab) return;
        e.Handled = true;
        WorkspaceTabs.SelectedItem = tab;
        Dispatcher.UIThread.Post(() => DetachWorkspace(tab));
    }

    private void OnWorkspaceCategoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab) return;
        e.Handled = true;
        Dispatcher.UIThread.Post(() => DetachWorkspace(tab));
    }

    private void OnDetachWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string key }) return;
        TabItem? tab = key switch
        {
            "Changes" => ChangesWorkspaceTab,
            "History" => HistoryWorkspaceTab,
            "Compose" => CompositionWorkspaceTab,
            "Branches" => BranchesWorkspaceTab,
            "PullRequests" => PullRequestsWorkspaceTab,
            "Ci" => CiWorkspaceTab,
            "GitGraphs" => GitGraphsWorkspaceTab,
            "FileLocks" => LfsLocksWorkspaceTab,
            "GitLfs" => GitLfsWorkspaceTab,
            "CyStore" => CyStoreWorkspaceTab,
            "Backups" => BackupsWorkspaceTab,
            "SolutionExplorer" => SolutionExplorerWorkspaceTab,
            "Code" => CodeWorkspaceTab,
            _ => null
        };
        if (tab is not null) DetachWorkspace(tab);
    }

    private void DetachWorkspace(TabItem tab)
    {
        if (_detachedTabWindows.TryGetValue(tab, out DetachedTabWindow? existing))
        {
            existing.Activate();
            return;
        }
        string title = tab.Header?.ToString() ?? "Workspace";
        DetachedTabWindow window = new(tab, title);
        window.Closed += (_, _) => _detachedTabWindows.Remove(tab);
        _detachedTabWindows[tab] = window;
        window.Show();
    }

    private void OpenDetachedWorkspace(DetachedWorkspaceSection section)
    {
        if (_localization is null)
        {
            return;
        }

        DetachedWorkspaceWindow window = new(_viewModel, _localization, section);
        window.Closed += (_, _) => _detachedWorkspaceWindows.Remove(window);
        _detachedWorkspaceWindows.Add(window);
        window.Show();
    }
}
