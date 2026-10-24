namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _nativeNetworkCancellation;

    public Task<IReadOnlyList<CyRevision.Code.CodeTreeNode>> LoadNativeCodeDirectoryAsync(string relativePath) =>
        SelectedProject is null
            ? Task.FromResult<IReadOnlyList<CyRevision.Code.CodeTreeNode>>([])
            : _codeWorkspaceService.LoadDirectoryAsync(SelectedProject.RootPath, relativePath, CodeIncludeHidden);

    public void CancelNativeOperation()
    {
        foreach (CancellationTokenSource? source in new[] {
            _nativeNetworkCancellation, _projectLoadCancellation, _codeWorkspaceCancellation,
            _codeSearchCancellation, _codePreviewCancellation, _lfsAnalysisCancellation,
            _aiAgentCancellation, _unrealBuildCancellation, _remoteBuildCancellation,
            _repositoryConsoleCancellation, _workingTreeDiffCancellation,
            _explorerRevisionCancellation, _explorerFileCancellation })
        {
            try { source?.Cancel(); }
            catch(ObjectDisposedException) { }
        }
    }
}
