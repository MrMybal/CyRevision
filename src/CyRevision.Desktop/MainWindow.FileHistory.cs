using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CyRevision.Code;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private void OnSolutionFileExplorerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control owner ||
            !e.GetCurrentPoint(owner).Properties.IsRightButtonPressed ||
            e.Source is not Control source)
        {
            return;
        }

        switch (sender)
        {
            case TreeView tree when source.FindAncestorOfType<TreeViewItem>() is
                { DataContext: CodeTreeNode node } treeItem:
                treeItem.IsSelected = true;
                _viewModel.SelectedCodeNode = node;
                break;
            case ListBox list when source.FindAncestorOfType<ListBoxItem>() is
                { DataContext: CodeFileEntry entry }:
                list.SelectedItem = entry;
                _viewModel.SelectedCodeFileSearchResult = entry;
                break;
            case DataGrid grid when source.FindAncestorOfType<DataGridRow>() is
                { DataContext: CodeTreeNode node }:
                grid.SelectedItem = node;
                _viewModel.SelectedCodeNode = node;
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
        if (_viewModel.SelectedCodeNode is not { IsDirectory: false, IsPlaceholder: false } node)
        {
            return;
        }

        ShowGitFileHistory(node.RelativePath);
    }

    private void OnShowSelectedAssetGitHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAssetExplorerFile is not { } file)
        {
            return;
        }

        ShowGitFileHistory(file.RelativePath);
    }

    private void ShowGitFileHistory(string relativePath)
    {
        ProjectItemViewModel? project = _viewModel.SelectedProject;
        if (project is null || !project.Definition.Features.GitEnabled)
        {
            return;
        }

        FileHistoryWindow window = new(_viewModel, project, relativePath);
        window.Show(this);
    }
}