using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task CreateBranchFromSelectedCommitAsync(string branchName)
    {
        if (SelectedProject is null || SelectedBranchRevision is null || string.IsNullOrWhiteSpace(branchName))
            return;

        string trimmedName = branchName.Trim();
        string startPoint = SelectedBranchRevision.Hash;
        await RunOperationAsync("Creating branch from selected commit...", async () =>
        {
            if (CreateHistoricalBranchInWorktree)
            {
                GitHistoricalWorktreeResult result = await _gitService.CreateHistoricalWorktreeAsync(
                    SelectedProject.RootPath, startPoint, trimmedName);
                HistoricalWorktreeStatus = result.Summary;
                await RefreshHistoricalWorktreesAsync();
                return;
            }

            GitRepositoryStatus status = await _gitService.GetStatusAsync(SelectedProject.RootPath);
            if (status.Changes.Count > 0)
                throw new InvalidOperationException(
                    "The working tree contains changes. Commit, stash or discard them before switching to an older revision.");

            await _gitService.CreateBranchFromAsync(SelectedProject.RootPath, trimmedName, startPoint);
            await RefreshCoreAsync();
        }, $"Branch {trimmedName} created from {SelectedBranchRevision.ShortHash}");
    }

    public async Task RefreshHistoricalWorktreesAsync()
    {
        if (SelectedProject is null) return;
        IReadOnlyList<GitHistoricalWorktree> worktrees =
            await _gitService.GetHistoricalWorktreesAsync(SelectedProject.RootPath);
        GitHistoricalWorktree[] managed = worktrees.Where(item => item.IsManagedByCyRevision).ToArray();
        ReplaceCollection(HistoricalWorktrees, managed);
        SelectedHistoricalWorktree = managed.FirstOrDefault();
        HistoricalWorktreeStatus = managed.Length == 0
            ? "No isolated historical worktree."
            : $"{managed.Length:N0} isolated worktree(s) · main repository remains unchanged.";
    }

    public async Task RemoveSelectedHistoricalWorktreeAsync(bool force)
    {
        if (SelectedProject is null || SelectedHistoricalWorktree is null) return;
        string path = SelectedHistoricalWorktree.Path;
        await RunOperationAsync("Removing historical worktree...", async () =>
        {
            await _gitService.RemoveHistoricalWorktreeAsync(SelectedProject.RootPath, path, force);
            await RefreshHistoricalWorktreesAsync();
        }, "Historical worktree removed");
    }
}
