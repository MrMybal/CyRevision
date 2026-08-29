using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CyRevision.Code;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private string? _solutionFileHistoryContextPath;
    private FileHistoryWindow? _fileHistoryWindow;

    private void OnSolutionFileExplorerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control owner ||
            !e.GetCurrentPoint(owner).Properties.IsRightButtonPressed ||
            e.Source is not Control source)
        {
            return;
        }

        _solutionFileHistoryContextPath = null;
        switch (sender)
        {
            case TreeView tree when source.FindAncestorOfType<TreeViewItem>() is
                { DataContext: CodeTreeNode node } treeItem:
                treeItem.IsSelected = true;
                _viewModel.SelectedCodeNode = node;
                _solutionFileHistoryContextPath = node.IsDirectory || node.IsPlaceholder
                    ? null
                    : node.RelativePath;
                break;
            case ListBox list when source.FindAncestorOfType<ListBoxItem>() is
                { DataContext: CodeFileEntry entry }:
                list.SelectedItem = entry;
                _viewModel.SelectedCodeFileSearchResult = entry;
                _solutionFileHistoryContextPath = entry.RelativePath;
                break;
            case DataGrid grid when source.FindAncestorOfType<DataGridRow>() is
                { DataContext: CodeTreeNode node }:
                grid.SelectedItem = node;
                _viewModel.SelectedCodeNode = node;
                _solutionFileHistoryContextPath = node.IsDirectory || node.IsPlaceholder
                    ? null
                    : node.RelativePath;
                break;
        }
    }

    private void OnAssetFileExplorerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list ||
            !e.GetCurrentPoint(list).Properties.IsRightButtonPressed ||
            e.Source is not Control source ||
            source.FindAncestorOfType<ListBoxItem>() is not { DataContext: CodeFileEntry entry })
        {
            return;
        }

        list.SelectedItem = entry;
        _viewModel.SelectedAssetExplorerFile = entry;
    }

    private void OnShowSelectedCodeFileGitHistoryClick(object? sender, RoutedEventArgs e)
    {
        string? relativePath = GetSelectedSolutionFilePath();
        ShowGitFileHistory(relativePath);
    }

    private string? GetSelectedSolutionFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_solutionFileHistoryContextPath))
        {
            return _solutionFileHistoryContextPath;
        }

        if (_viewModel.HasCodeFileSearchResults &&
            _viewModel.SelectedCodeFileSearchResult is { } searchResult)
        {
            return searchResult.RelativePath;
        }

        if (_viewModel.SelectedCodeNode is { IsDirectory: false, IsPlaceholder: false } node)
        {
            return node.RelativePath;
        }

        return string.IsNullOrWhiteSpace(_viewModel.CodePreviewPath)
            ? null
            : _viewModel.CodePreviewPath;
    }

    private void OnShowSelectedAssetGitHistoryClick(object? sender, RoutedEventArgs e)
    {
        ShowGitFileHistory(_viewModel.SelectedAssetExplorerFile?.RelativePath);
    }

    private void ShowGitFileHistory(string? relativePath)
    {
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        if (project is null)
        {
            _viewModel.ReportFileHistoryIssue("Select a project before opening file history.");
            return;
        }

        if (!project.Definition.Features.GitEnabled)
        {
            _viewModel.ReportFileHistoryIssue("Git is not active for this project.");
            return;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            _viewModel.ReportFileHistoryIssue("Select a file before opening Git / LFS history.");
            return;
        }

        if (_fileHistoryWindow is not null)
        {
            if (_fileHistoryWindow.IsShowing(project, relativePath))
            {
                if (_fileHistoryWindow.WindowState == WindowState.Minimized)
                {
                    _fileHistoryWindow.WindowState = WindowState.Normal;
                }

                _fileHistoryWindow.Activate();
                return;
            }

            _fileHistoryWindow.Close();
        }

        try
        {
            FileHistoryWindow window = new(_viewModel, project, relativePath, _configurationDirectory);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_fileHistoryWindow, window))
                {
                    _fileHistoryWindow = null;
                }
            };
            _fileHistoryWindow = window;
            window.Show(this);
        }
        catch (Exception exception)
        {
            _fileHistoryWindow = null;
            _viewModel.ReportFileHistoryIssue($"Unable to open Git / LFS history: {exception.Message}");
        }
    }
}
