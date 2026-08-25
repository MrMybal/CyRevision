using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CyRevision.Core.Projects;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;
using CyRevision.PullRequests;

namespace CyRevision.Desktop.ViewModels;

public enum PullRequestTaskUpdateMode
{
    AskAfterMerge,
    AutomaticAfterMerge,
    Disabled
}


public sealed record PullRequestTaskUpdateModeOption(
    PullRequestTaskUpdateMode Mode,
    string DisplayName);
public sealed record PullRequestLinkedWorkItemViewModel(
    string ProviderId,
    string ProviderName,
    string DetectedIdentifier,
    string DetectedUrl,
    WorkItemReference? WorkItem,
    string DetectionStatus)
{
    public string Key => WorkItem?.DisplayKey ?? DetectedIdentifier;
    public string Title => WorkItem?.Title ?? "Detected task reference";
    public string Status => WorkItem?.Status ?? DetectionStatus;
    public string Url => WorkItem?.Url ?? DetectedUrl;
    public string StatusColor => WorkItem is null ? "#E6B85C" : "#66D9A9";
    public bool CanUpdate => WorkItem is not null;
}

public sealed partial class MainWindowViewModel
{
    private CiWorkflow? _selectedPullRequestCiWorkflow;
    private CiWorkflowRun? _selectedPullRequestCiRun;
    private CiWorkflowJob? _selectedPullRequestCiJob;
    private CiLogFilterMode _pullRequestCiLogFilterMode;
    private bool _isPullRequestCiLoading;
    private int _pullRequestCiRunLoadVersion;
    private string _pullRequestCiStatus = "Select a pull request to inspect its CI.";
    private string _pullRequestConflictSummary = "Select a pull request to analyze merge conflicts.";
    private string _pullRequestLinkedTaskStatus = "Task links are detected from the pull-request title and description.";
    private string _pullRequestLocalBranchStatus = "Merged pull requests can remove their matching local branch after confirmation.";
    private bool _canRemovePullRequestLocalBranch;
    private string? _pullRequestCurrentUser;
    private PullRequestTaskUpdateMode _pullRequestTaskUpdateMode = PullRequestTaskUpdateMode.AskAfterMerge;

    public ObservableCollection<CiWorkflow> PullRequestCiWorkflows { get; } = [];
    public ObservableCollection<CiWorkflowRun> PullRequestCiRuns { get; } = [];
    public ObservableCollection<CiWorkflowJob> PullRequestCiJobs { get; } = [];
    public ObservableCollection<CiLogLine> PullRequestCiLogLines { get; } = [];
    public ObservableCollection<CiLogLine> FilteredPullRequestCiLogLines { get; } = [];
    public ObservableCollection<PullRequestConflictFile> PullRequestConflictFiles { get; } = [];
    public ObservableCollection<PullRequestLinkedWorkItemViewModel> PullRequestLinkedWorkItems { get; } = [];

    public IReadOnlyList<CiLogFilterMode> CiLogFilterModes { get; } = Enum.GetValues<CiLogFilterMode>();

    public IReadOnlyList<PullRequestTaskUpdateModeOption> PullRequestTaskUpdateModeOptions { get; } =
    [
        new(PullRequestTaskUpdateMode.AskAfterMerge, "Ask after merge"),
        new(PullRequestTaskUpdateMode.AutomaticAfterMerge, "Automatic after merge"),
        new(PullRequestTaskUpdateMode.Disabled, "Detection only")
    ];

    public PullRequestTaskUpdateModeOption SelectedPullRequestTaskUpdateModeOption
    {
        get => PullRequestTaskUpdateModeOptions.First(option => option.Mode == PullRequestTaskUpdateMode);
        set
        {
            if (value is not null) PullRequestTaskUpdateMode = value.Mode;
        }
    }

    public PullRequestTaskUpdateMode PullRequestTaskUpdateMode
    {
        get => _pullRequestTaskUpdateMode;
        set
        {
            if (!SetProperty(ref _pullRequestTaskUpdateMode, value)) return;
            OnPropertyChanged(nameof(PullRequestTaskUpdateModeDescription));
            OnPropertyChanged(nameof(SelectedPullRequestTaskUpdateModeOption));
            _ = SavePullRequestTaskUpdateModeAsync();
        }
    }

    public string PullRequestTaskUpdateModeDescription => PullRequestTaskUpdateMode switch
    {
        PullRequestTaskUpdateMode.AutomaticAfterMerge =>
            "After a successful merge, detected Jira and ClickUp tasks are moved to their provider completion status automatically.",
        PullRequestTaskUpdateMode.Disabled =>
            "Linked tasks remain unchanged. Detection stays visible for review.",
        _ =>
            "After a successful merge, CyRevision asks before moving detected tasks to their provider completion status."
    };

    private void LoadPullRequestTaskUpdatePreference(ProjectDefinition definition)
    {
        PullRequestTaskUpdateMode parsed = Enum.TryParse(
            definition.PullRequestTaskUpdateMode,
            ignoreCase: true,
            out PullRequestTaskUpdateMode configured)
            ? configured
            : PullRequestTaskUpdateMode.AskAfterMerge;
        if (_pullRequestTaskUpdateMode == parsed) return;
        _pullRequestTaskUpdateMode = parsed;
        OnPropertyChanged(nameof(PullRequestTaskUpdateMode));
        OnPropertyChanged(nameof(PullRequestTaskUpdateModeDescription));
        OnPropertyChanged(nameof(SelectedPullRequestTaskUpdateModeOption));
    }

    private async Task SavePullRequestTaskUpdateModeAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;
        string configured = PullRequestTaskUpdateMode.ToString();
        if (string.Equals(project.Definition.PullRequestTaskUpdateMode, configured, StringComparison.Ordinal)) return;
        try
        {
            ProjectDefinition updated = project.Definition with { PullRequestTaskUpdateMode = configured };
            updated.Validate();
            project.Update(updated);
            await _projectCatalog.UpsertAsync(updated);
        }
        catch (Exception exception)
        {
            _applicationLogService.Warning(
                "pull-requests",
                "Unable to save the pull-request task update policy: " + exception.Message,
                project.RootPath);
        }
    }

    public CiWorkflow? SelectedPullRequestCiWorkflow
    {
        get => _selectedPullRequestCiWorkflow;
        set
        {
            if (!SetProperty(ref _selectedPullRequestCiWorkflow, value)) return;
            OnPropertyChanged(nameof(CanDispatchPullRequestCiWorkflow));
        }
    }

    public CiWorkflowRun? SelectedPullRequestCiRun
    {
        get => _selectedPullRequestCiRun;
        set
        {
            if (!SetProperty(ref _selectedPullRequestCiRun, value)) return;
            OnPropertyChanged(nameof(CanRerunSelectedPullRequestCiRun));
            OnPropertyChanged(nameof(CanCancelSelectedPullRequestCiRun));
            OnPropertyChanged(nameof(SelectedPullRequestCiStateColor));
            SelectedPullRequestCiJob = null;
            _ = LoadSelectedPullRequestCiRunAsync(value);
        }
    }

    public CiWorkflowJob? SelectedPullRequestCiJob
    {
        get => _selectedPullRequestCiJob;
        set
        {
            if (!SetProperty(ref _selectedPullRequestCiJob, value)) return;
            OnPropertyChanged(nameof(CanCancelSelectedPullRequestCiRun));
        }
    }
    public CiLogFilterMode PullRequestCiLogFilterMode
    {
        get => _pullRequestCiLogFilterMode;
        set
        {
            if (!SetProperty(ref _pullRequestCiLogFilterMode, value)) return;
            ApplyPullRequestCiLogFilter();
        }
    }

    public bool IsPullRequestCiLoading
    {
        get => _isPullRequestCiLoading;
        private set
        {
            if (!SetProperty(ref _isPullRequestCiLoading, value)) return;
            OnPropertyChanged(nameof(CanDispatchPullRequestCiWorkflow));
            OnPropertyChanged(nameof(CanRerunSelectedPullRequestCiRun));
            OnPropertyChanged(nameof(CanCancelSelectedPullRequestCiRun));
        }
    }

    public string PullRequestCiStatus
    {
        get => _pullRequestCiStatus;
        private set => SetProperty(ref _pullRequestCiStatus, value);
    }

    public string PullRequestConflictSummary
    {
        get => _pullRequestConflictSummary;
        private set => SetProperty(ref _pullRequestConflictSummary, value);
    }

    public string PullRequestLinkedTaskStatus
    {
        get => _pullRequestLinkedTaskStatus;
        private set => SetProperty(ref _pullRequestLinkedTaskStatus, value);
    }

    public string PullRequestLocalBranchStatus
    {
        get => _pullRequestLocalBranchStatus;
        private set => SetProperty(ref _pullRequestLocalBranchStatus, value);
    }

    public bool CanRemovePullRequestLocalBranch
    {
        get => _canRemovePullRequestLocalBranch;
        private set => SetProperty(ref _canRemovePullRequestLocalBranch, value);
    }

    public bool CanDispatchPullRequestCiWorkflow =>
        !IsPullRequestCiLoading &&
        SelectedPullRequest is { IsMerged: false } &&
        SelectedPullRequestCiWorkflow is not null;

    public bool CanRerunSelectedPullRequestCiRun =>
        !IsPullRequestCiLoading &&
        SelectedPullRequestCiRun is { HasFailed: true };

    public bool CanCancelSelectedPullRequestCiRun =>
        !IsPullRequestCiLoading &&
        SelectedPullRequestCiRun is not null &&
        (SelectedPullRequestCiRun.IsRunning || PullRequestCiJobs.Any(job =>
            job.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase) ||
            job.Status.Equals("queued", StringComparison.OrdinalIgnoreCase) ||
            job.Status.Equals("waiting", StringComparison.OrdinalIgnoreCase)));
    public string SelectedPullRequestCiStateColor =>
        SelectedPullRequestCiRun?.StateColor ?? "#A7ACB7";

    public bool HasPullRequestConflictFiles => PullRequestConflictFiles.Count > 0;
    public bool HasPullRequestLinkedWorkItems => PullRequestLinkedWorkItems.Count > 0;
    public bool ShouldAskToUpdatePullRequestTasksAfterMerge =>
        PullRequestTaskUpdateMode == PullRequestTaskUpdateMode.AskAfterMerge &&
        PullRequestLinkedWorkItems.Any(item => item.CanUpdate);
    public bool ShouldAutomaticallyUpdatePullRequestTasksAfterMerge =>
        PullRequestTaskUpdateMode == PullRequestTaskUpdateMode.AutomaticAfterMerge &&
        PullRequestLinkedWorkItems.Any(item => item.CanUpdate);

    private async Task<IReadOnlyList<PullRequestSummary>> EnrichPullRequestSummariesAsync(
        IReadOnlyList<PullRequestSummary> pulls,
        string? token)
    {
        if (_pullRequestRepository is null || pulls.Count == 0) return pulls;

        IReadOnlyList<CiWorkflowRun> runs = [];
        try
        {
            Task<string?> currentUserTask = _pullRequestService.GetCurrentUserAsync(
                _pullRequestRepository,
                token);
            Task<IReadOnlyList<CiWorkflowRun>> runsTask = _ciWorkflowService.ListRunsAsync(
                _pullRequestRepository,
                token);
            await Task.WhenAll(currentUserTask, runsTask);
            _pullRequestCurrentUser = await currentUserTask;
            runs = await runsTask;
        }
        catch (Exception exception)
        {
            _applicationLogService.Warning(
                "pull-requests",
                "Unable to enrich pull-request ownership or CI state: " + exception.Message,
                SelectedProject?.RootPath);
        }

        return pulls.Select(pull =>
        {
            CiWorkflowRun[] branchRuns = runs
                .Where(candidate => candidate.Branch.Equals(pull.HeadBranch, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            (string status, string conclusion) = CiStatePresentation.AggregateRuns(branchRuns);
            return pull with
            {
                CurrentUser = _pullRequestCurrentUser ?? string.Empty,
                CiStatus = status,
                CiConclusion = conclusion
            };
        }).ToArray();
    }

    private async Task LoadPullRequestOperationalDataAsync(
        PullRequestDetails details,
        string? token,
        int detailsLoadVersion)
    {
        int number = details.Summary.Number;
        ClearPullRequestOperationalData();
        IsPullRequestCiLoading = true;
        try
        {
            Task<IReadOnlyList<CiWorkflow>> workflowsTask = _ciWorkflowService.ListWorkflowsAsync(
                _pullRequestRepository!,
                token);
            Task<IReadOnlyList<CiWorkflowRun>> runsTask = _ciWorkflowService.ListRunsAsync(
                _pullRequestRepository!,
                token);
            await Task.WhenAll(workflowsTask, runsTask);
            if (detailsLoadVersion != _pullRequestDetailsLoadVersion || SelectedPullRequest?.Number != number) return;

            HashSet<string> commitHashes = details.Commits
                .Select(commit => commit.Hash)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            CiWorkflowRun[] matchingRuns = (await runsTask)
                .Where(run => run.Branch.Equals(details.Summary.HeadBranch, StringComparison.OrdinalIgnoreCase) ||
                              commitHashes.Contains(run.CommitSha))
                .OrderByDescending(run => run.Id)
                .ToArray();
            ReplaceCollection(PullRequestCiWorkflows, await workflowsTask);
            ReplaceCollection(PullRequestCiRuns, matchingRuns);
            SelectedPullRequestCiWorkflow = PullRequestCiWorkflows.FirstOrDefault(
                                                  workflow => workflow.Name.Contains("build", StringComparison.OrdinalIgnoreCase) ||
                                                              workflow.Name.Contains("test", StringComparison.OrdinalIgnoreCase))
                                              ?? PullRequestCiWorkflows.FirstOrDefault();
            (string aggregateStatus, string aggregateConclusion) =
                CiStatePresentation.AggregateRuns(matchingRuns);
            UpdateSelectedPullRequestCiPresentation(aggregateStatus, aggregateConclusion);
            SelectedPullRequestCiRun = matchingRuns
                                           .Where(run => run.IsRunning)
                                           .OrderByDescending(run => run.Id)
                                           .FirstOrDefault()
                                       ?? PullRequestCiRuns.FirstOrDefault();
            PullRequestCiStatus = matchingRuns.Length == 0
                ? $"No CI run was found for {details.Summary.HeadBranch} or its {commitHashes.Count:N0} commit(s)."
                : $"{matchingRuns.Length:N0} CI run(s) match pull request #{number}.";
        }
        catch (Exception exception)
        {
            if (detailsLoadVersion == _pullRequestDetailsLoadVersion)
                PullRequestCiStatus = "Unable to load PR CI: " + exception.Message;
        }
        finally
        {
            if (detailsLoadVersion == _pullRequestDetailsLoadVersion)
                IsPullRequestCiLoading = false;
        }

        if (detailsLoadVersion != _pullRequestDetailsLoadVersion || SelectedPullRequest?.Number != number) return;
        await DetectPullRequestWorkItemsAsync(details, detailsLoadVersion);
        await AnalyzePullRequestConflictsAsync(details, detailsLoadVersion);
        await RefreshPullRequestLocalBranchStateAsync(details.Summary, detailsLoadVersion);
    }

    public async Task RefreshSelectedPullRequestCiAsync()
    {
        PullRequestDetails? details = _selectedPullRequestDetails;
        if (_pullRequestRepository is null || details is null) return;
        string? token = await ResolvePullRequestTokenAsync(_pullRequestRepository);
        await LoadPullRequestOperationalDataAsync(details, token, _pullRequestDetailsLoadVersion);
    }

    public async Task DispatchSelectedPullRequestCiWorkflowAsync()
    {
        if (_pullRequestRepository is null ||
            SelectedPullRequest is null ||
            SelectedPullRequestCiWorkflow is null)
            return;
        string? token = await GetPullRequestWriteTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;

        IsPullRequestCiLoading = true;
        try
        {
            await _ciWorkflowService.DispatchAsync(
                _pullRequestRepository,
                SelectedPullRequestCiWorkflow,
                SelectedPullRequest.HeadBranch,
                new Dictionary<string, string>(),
                token);
            PullRequestCiStatus =
                $"{SelectedPullRequestCiWorkflow.Name} queued on {SelectedPullRequest.HeadBranch}.";
            await Task.Delay(1200);
            await RefreshSelectedPullRequestCiAsync();
        }
        catch (Exception exception)
        {
            PullRequestCiStatus = exception.Message;
        }
        finally
        {
            IsPullRequestCiLoading = false;
        }
    }

    public async Task RerunSelectedPullRequestCiAsync()
    {
        if (_pullRequestRepository is null || SelectedPullRequestCiRun is null) return;
        string? token = await GetPullRequestWriteTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;

        IsPullRequestCiLoading = true;
        try
        {
            await _ciWorkflowService.RerunFailedJobsAsync(
                _pullRequestRepository,
                SelectedPullRequestCiRun.Id,
                token);
            PullRequestCiStatus = $"Failed jobs for run #{SelectedPullRequestCiRun.Id} were queued again.";
        }
        catch (Exception exception)
        {
            PullRequestCiStatus = exception.Message;
        }
        finally
        {
            IsPullRequestCiLoading = false;
        }
    }

    public async Task<bool> CancelSelectedPullRequestCiRunAsync()
    {
        if (_pullRequestRepository is null || SelectedPullRequestCiRun is null ||
            !CanCancelSelectedPullRequestCiRun)
            return false;
        string? token = await GetPullRequestWriteTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return false;

        IsPullRequestCiLoading = true;
        try
        {
            await _ciWorkflowService.CancelRunAsync(
                _pullRequestRepository,
                SelectedPullRequestCiRun.Id,
                token);
            PullRequestCiStatus =
                $"Cancellation requested for workflow run #{SelectedPullRequestCiRun.Id}. GitHub cancels the complete run, including its active jobs.";
            return true;
        }
        catch (Exception exception)
        {
            PullRequestCiStatus = exception.Message;
            return false;
        }
        finally
        {
            IsPullRequestCiLoading = false;
        }
    }
    private async Task LoadSelectedPullRequestCiRunAsync(CiWorkflowRun? run)
    {
        int version = Interlocked.Increment(ref _pullRequestCiRunLoadVersion);
        PullRequestCiJobs.Clear();
        PullRequestCiLogLines.Clear();
        FilteredPullRequestCiLogLines.Clear();
        if (_pullRequestRepository is null || run is null) return;

        IsPullRequestCiLoading = true;
        PullRequestCiStatus = $"Loading jobs and logs for {run.Name}…";
        try
        {
            string? token = await ResolvePullRequestTokenAsync(_pullRequestRepository);
            CiWorkflowRunDetails details = await _ciWorkflowService.GetRunDetailsAsync(
                _pullRequestRepository,
                run,
                token);
            if (version != _pullRequestCiRunLoadVersion || SelectedPullRequestCiRun?.Id != run.Id) return;
            ReplaceCollection(PullRequestCiJobs, details.Jobs);
            SelectedPullRequestCiJob = PullRequestCiJobs.FirstOrDefault();
            CiWorkflowRun effectiveRun = CiStatePresentation.ApplyJobState(run, details.Jobs);
            UpdatePullRequestCiRunPresentation(run, effectiveRun);
            (string aggregateStatus, string aggregateConclusion) =
                CiStatePresentation.AggregateRuns(PullRequestCiRuns);
            UpdateSelectedPullRequestCiPresentation(aggregateStatus, aggregateConclusion);

            string logStatus = string.Empty;
            try
            {
                IReadOnlyList<CiLogLine> logs = await _ciWorkflowService.GetRunLogLinesAsync(
                    _pullRequestRepository,
                    run,
                    token);
                if (version != _pullRequestCiRunLoadVersion || SelectedPullRequestCiRun?.Id != run.Id) return;
                ReplaceCollection(PullRequestCiLogLines, logs);
            }
            catch (Exception exception)
            {
                logStatus = " · logs unavailable: " + exception.Message;
            }

            ApplyPullRequestCiLogFilter();
            int errors = PullRequestCiLogLines.Count(line => line.IsError);
            PullRequestCiStatus =
                $"{effectiveRun.Name} · {effectiveRun.StateText} · {details.Jobs.Count:N0} job(s) · {errors:N0} error line(s){logStatus}";
        }
        catch (Exception exception)
        {
            if (version == _pullRequestCiRunLoadVersion)
                PullRequestCiStatus = "Unable to load CI details: " + exception.Message;
        }
        finally
        {
            if (version == _pullRequestCiRunLoadVersion)
                IsPullRequestCiLoading = false;
        }
    }

    private void UpdatePullRequestCiRunPresentation(CiWorkflowRun original, CiWorkflowRun updated)
    {
        if (original.Status == updated.Status && original.Conclusion == updated.Conclusion) return;
        for (int index = 0; index < PullRequestCiRuns.Count; index++)
        {
            if (PullRequestCiRuns[index].Id == original.Id)
            {
                PullRequestCiRuns[index] = updated;
                break;
            }
        }

        if (_selectedPullRequestCiRun?.Id == original.Id)
        {
            _selectedPullRequestCiRun = updated;
            OnPropertyChanged(nameof(SelectedPullRequestCiRun));
            OnPropertyChanged(nameof(CanRerunSelectedPullRequestCiRun));
            OnPropertyChanged(nameof(CanCancelSelectedPullRequestCiRun));
            OnPropertyChanged(nameof(SelectedPullRequestCiStateColor));
        }
    }

    private void UpdateSelectedPullRequestCiPresentation(string status, string conclusion)
    {
        if (_selectedPullRequest is null ||
            (_selectedPullRequest.CiStatus == status && _selectedPullRequest.CiConclusion == conclusion))
            return;
        PullRequestSummary updated = _selectedPullRequest with
        {
            CiStatus = status,
            CiConclusion = conclusion
        };
        for (int index = 0; index < PullRequests.Count; index++)
        {
            if (PullRequests[index].Number == updated.Number) PullRequests[index] = updated;
        }
        for (int index = 0; index < FilteredPullRequests.Count; index++)
        {
            if (FilteredPullRequests[index].Number == updated.Number) FilteredPullRequests[index] = updated;
        }
        _selectedPullRequest = updated;
        if (_selectedPullRequestDetails is { } details)
            _selectedPullRequestDetails = details with { Summary = updated };
        OnPropertyChanged(nameof(SelectedPullRequest));
    }
    private void ApplyPullRequestCiLogFilter()
    {
        IEnumerable<CiLogLine> source = PullRequestCiLogLines
            .Where(line => line.Matches(PullRequestCiLogFilterMode));
        ReplaceCollection(FilteredPullRequestCiLogLines, source);
    }

    private async Task AnalyzePullRequestConflictsAsync(
        PullRequestDetails details,
        int detailsLoadVersion)
    {
        if (SelectedProject is null || details.Summary.IsMerged)
        {
            PullRequestConflictSummary = details.Summary.IsMerged
                ? "The pull request is merged; conflict analysis is no longer required."
                : "Select a local Git project to analyze conflicts.";
            return;
        }

        if (details.Summary.IsMergeable == true &&
            !details.Summary.MergeableState.Equals("dirty", StringComparison.OrdinalIgnoreCase))
        {
            PullRequestConflictSummary = "GitHub reports that this pull request can be merged cleanly.";
            return;
        }

        string? baseReference = null;
        string? headReference = null;
        try
        {
            PullRequestConflictSummary = "Fetching private inspection refs and analyzing merge conflicts…";
            baseReference = await _gitService.FetchRemoteBranchForInspectionAsync(
                SelectedProject.RootPath,
                "origin",
                details.Summary.BaseBranch);
            headReference = $"refs/cyrevision/inspect/pull/{details.Summary.Number}";
            await _gitService.FetchReferenceAsync(
                SelectedProject.RootPath,
                "origin",
                $"+pull/{details.Summary.Number}/head:{headReference}");
            GitMergeConflictAnalysis analysis = await _gitService.AnalyzeMergeConflictsAsync(
                SelectedProject.RootPath,
                baseReference,
                headReference);
            if (detailsLoadVersion != _pullRequestDetailsLoadVersion ||
                SelectedPullRequest?.Number != details.Summary.Number)
                return;
            ReplaceCollection(
                PullRequestConflictFiles,
                analysis.ConflictPaths.Select(path => new PullRequestConflictFile(
                    path,
                    "Conflicting result reported by git merge-tree.")));
            PullRequestConflictSummary = analysis.Summary;
        }
        catch (Exception exception)
        {
            if (detailsLoadVersion == _pullRequestDetailsLoadVersion)
                PullRequestConflictSummary = "Conflict analysis unavailable: " + exception.Message;
        }
        finally
        {
            foreach (string reference in new[] { headReference, baseReference }
                         .Where(reference => !string.IsNullOrWhiteSpace(reference))
                         .Cast<string>())
            {
                try
                {
                    await _gitService.DeleteInspectionReferenceAsync(
                        SelectedProject.RootPath,
                        reference);
                }
                catch
                {
                    // Private inspection refs are disposable and will be removed by the next inspection cleanup.
                }
            }
        }

        OnPropertyChanged(nameof(HasPullRequestConflictFiles));
    }

    private async Task DetectPullRequestWorkItemsAsync(
        PullRequestDetails details,
        int detailsLoadVersion)
    {
        IReadOnlyList<IWorkItemIntegrationPlugin> plugins = GetActiveWorkItemPlugins();
        if (SelectedProject is null || plugins.Count == 0)
        {
            PullRequestLinkedTaskStatus = "Enable Jira Tasks or ClickUp Tasks for this project to resolve detected links.";
            return;
        }

        string content = string.Join(
            Environment.NewLine,
            new[]
            {
                details.Summary.Title,
                details.Body,
                string.Join(Environment.NewLine, details.Commits.Select(commit => commit.Subject)),
                string.Join(Environment.NewLine, details.Comments.Select(comment => comment.Body)),
                string.Join(Environment.NewLine, details.Reviews.Select(review => review.Body))
            });
        IReadOnlyList<DetectedWorkItemCandidate> candidates = PullRequestWorkItemLinkDetector.Detect(content);
        if (candidates.Count == 0)
        {
            PullRequestLinkedTaskStatus = "No Jira or ClickUp task ID/link was detected in this pull request.";
            return;
        }

        List<PullRequestLinkedWorkItemViewModel> resolved = [];
        foreach (IWorkItemIntegrationPlugin plugin in plugins)
        {
            DetectedWorkItemCandidate[] providerCandidates = candidates
                .Where(candidate => candidate.ProviderId.Equals(plugin.Provider.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (providerCandidates.Length == 0) continue;

            WorkItemConnectionSettings settings = await plugin.LoadConnectionAsync(SelectedProject.Id);
            foreach (DetectedWorkItemCandidate candidate in providerCandidates)
            {
                try
                {
                    WorkItemReference? item = await plugin.ResolveAsync(
                        settings,
                        null,
                        candidate.Identifier);
                    resolved.Add(new PullRequestLinkedWorkItemViewModel(
                        plugin.Provider.Id,
                        plugin.Provider.Name,
                        candidate.Identifier,
                        candidate.Url,
                        item,
                        item is null ? "Task not found" : "Resolved"));
                }
                catch (Exception exception)
                {
                    resolved.Add(new PullRequestLinkedWorkItemViewModel(
                        plugin.Provider.Id,
                        plugin.Provider.Name,
                        candidate.Identifier,
                        candidate.Url,
                        null,
                        "Detected · " + exception.Message));
                }
            }
        }

        if (detailsLoadVersion != _pullRequestDetailsLoadVersion ||
            SelectedPullRequest?.Number != details.Summary.Number)
            return;
        ReplaceCollection(
            PullRequestLinkedWorkItems,
            resolved
                .GroupBy(item => $"{item.ProviderId}:{item.Key}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()));
        PullRequestLinkedTaskStatus = PullRequestLinkedWorkItems.Count == 0
            ? $"{candidates.Count:N0} task reference(s) detected, but their project plugin is not active."
            : $"{PullRequestLinkedWorkItems.Count:N0} linked task(s) detected.";
        OnPropertyChanged(nameof(HasPullRequestLinkedWorkItems));
        OnPropertyChanged(nameof(ShouldAskToUpdatePullRequestTasksAfterMerge));
        OnPropertyChanged(nameof(ShouldAutomaticallyUpdatePullRequestTasksAfterMerge));
    }

    public async Task<int> CompleteLinkedPullRequestTasksAsync()
    {
        if (SelectedProject is null) return 0;
        IReadOnlyList<IWorkItemIntegrationPlugin> plugins = GetActiveWorkItemPlugins();
        int updated = 0;
        for (int index = 0; index < PullRequestLinkedWorkItems.Count; index++)
        {
            PullRequestLinkedWorkItemViewModel link = PullRequestLinkedWorkItems[index];
            if (link.WorkItem is null) continue;
            IWorkItemIntegrationPlugin? plugin = plugins.FirstOrDefault(candidate =>
                candidate.Provider.Id.Equals(link.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (plugin is null)
            {
                PullRequestLinkedWorkItems[index] = link with { DetectionStatus = "Provider plugin is no longer active." };
                continue;
            }

            try
            {
                WorkItemConnectionSettings settings = await plugin.LoadConnectionAsync(SelectedProject.Id);
                IReadOnlyList<WorkItemTransitionOption> transitions = await plugin.GetTransitionsAsync(
                    settings,
                    null,
                    link.WorkItem);
                WorkItemTransitionOption? completion = transitions.FirstOrDefault(transition => transition.IsCompletion);
                if (completion is null)
                {
                    PullRequestLinkedWorkItems[index] = link with
                    {
                        DetectionStatus = "No completion transition is available."
                    };
                    continue;
                }

                WorkItemStatusUpdateResult result = await plugin.ApplyTransitionAsync(
                    settings,
                    null,
                    link.WorkItem,
                    completion);
                PullRequestLinkedWorkItems[index] = link with
                {
                    WorkItem = result.WorkItem,
                    DetectionStatus = $"{result.PreviousStatus} → {result.NewStatus}"
                };
                updated++;
            }
            catch (Exception exception)
            {
                PullRequestLinkedWorkItems[index] = link with
                {
                    DetectionStatus = "Update failed · " + exception.Message
                };
            }
        }

        PullRequestLinkedTaskStatus = updated == 0
            ? "No linked task was updated. Check provider permissions, credentials and completion transitions."
            : $"{updated:N0} linked task(s) moved to their completion status.";
        return updated;
    }

    private async Task RefreshPullRequestLocalBranchStateAsync(
        PullRequestSummary pullRequest,
        int detailsLoadVersion)
    {
        CanRemovePullRequestLocalBranch = false;
        if (SelectedProject is null)
        {
            PullRequestLocalBranchStatus = "No local Git project is selected.";
            return;
        }

        IReadOnlyList<GitBranch> branches = await _gitService.GetBranchesAsync(SelectedProject.RootPath);
        if (detailsLoadVersion != _pullRequestDetailsLoadVersion ||
            SelectedPullRequest?.Number != pullRequest.Number)
            return;
        GitBranch? local = branches.FirstOrDefault(branch =>
            !branch.IsRemote &&
            branch.Name.Equals(pullRequest.HeadBranch, StringComparison.Ordinal));
        PullRequestLocalBranchStatus = local is null
            ? $"No local branch named {pullRequest.HeadBranch} exists."
            : !pullRequest.IsMerged
                ? $"Local branch {pullRequest.HeadBranch} exists; removal is available after the PR is merged."
                : local.IsCurrent
                    ? $"Switch away from {pullRequest.HeadBranch} before removing it."
                    : $"Local branch {pullRequest.HeadBranch} can be safety-checked and removed.";
        CanRemovePullRequestLocalBranch = pullRequest.IsMerged && local is { IsCurrent: false };
    }

    public async Task<GitLocalBranchRemovalAnalysis?> AnalyzeSelectedPullRequestLocalBranchRemovalAsync()
    {
        if (SelectedProject is null || SelectedPullRequest is not { IsMerged: true } pullRequest)
            return null;
        try
        {
            return await _gitService.AnalyzeLocalBranchRemovalAsync(
                SelectedProject.RootPath,
                pullRequest.HeadBranch);
        }
        catch (Exception exception)
        {
            PullRequestLocalBranchStatus = exception.Message;
            return null;
        }
    }

    public async Task<bool> RemoveSelectedPullRequestLocalBranchAsync(bool force)
    {
        if (SelectedPullRequest is not { IsMerged: true } pullRequest) return false;
        bool removed = await RemoveLocalBranchAsync(pullRequest.HeadBranch, force);
        if (removed)
        {
            PullRequestLocalBranchStatus =
                $"Local branch {pullRequest.HeadBranch} removed. The remote repository was not changed.";
            CanRemovePullRequestLocalBranch = false;
        }
        return removed;
    }

    private void ClearPullRequestOperationalData()
    {
        Interlocked.Increment(ref _pullRequestCiRunLoadVersion);
        PullRequestCiWorkflows.Clear();
        PullRequestCiRuns.Clear();
        PullRequestCiJobs.Clear();
        PullRequestCiLogLines.Clear();
        FilteredPullRequestCiLogLines.Clear();
        PullRequestConflictFiles.Clear();
        PullRequestLinkedWorkItems.Clear();
        SelectedPullRequestCiWorkflow = null;
        SelectedPullRequestCiRun = null;
        SelectedPullRequestCiJob = null;
        PullRequestCiStatus = "Select a pull request to inspect its CI.";
        PullRequestConflictSummary = "Select a pull request to analyze merge conflicts.";
        PullRequestLinkedTaskStatus = "Task links are detected from the pull-request title and description.";
        PullRequestLocalBranchStatus = "Merged pull requests can remove their matching local branch after confirmation.";
        CanRemovePullRequestLocalBranch = false;
        OnPropertyChanged(nameof(HasPullRequestConflictFiles));
        OnPropertyChanged(nameof(HasPullRequestLinkedWorkItems));
        OnPropertyChanged(nameof(ShouldAskToUpdatePullRequestTasksAfterMerge));
        OnPropertyChanged(nameof(ShouldAutomaticallyUpdatePullRequestTasksAfterMerge));
    }

    private sealed record DetectedWorkItemCandidate(
        string ProviderId,
        string Identifier,
        string Url);

    private static class PullRequestWorkItemLinkDetector
    {
        private static readonly Regex JiraUrl = new(
            @"https?://[^\s)\]]+/browse/(?<id>[A-Z][A-Z0-9]+-\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex JiraKey = new(
            @"(?<![A-Z0-9])(?<id>[A-Z][A-Z0-9]{1,15}-\d+)(?![A-Z0-9])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex ClickUpUrl = new(
            @"https?://app\.clickup\.com/t/(?<id>[A-Za-z0-9_-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex ClickUpId = new(
            @"(?:ClickUp|CU)\s*[:#-]\s*(?<id>[A-Za-z0-9_-]{4,})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IReadOnlyList<DetectedWorkItemCandidate> Detect(string content)
        {
            List<DetectedWorkItemCandidate> result = [];
            foreach (Match match in JiraUrl.Matches(content))
                Add(result, "jira", match.Groups["id"].Value, match.Value);
            foreach (Match match in JiraKey.Matches(content))
                Add(result, "jira", match.Groups["id"].Value, string.Empty);
            foreach (Match match in ClickUpUrl.Matches(content))
                Add(result, "clickup", match.Groups["id"].Value, match.Value);
            foreach (Match match in ClickUpId.Matches(content))
                Add(result, "clickup", match.Groups["id"].Value, string.Empty);
            return result;
        }

        private static void Add(
            ICollection<DetectedWorkItemCandidate> result,
            string provider,
            string identifier,
            string url)
        {
            if (result.Count >= 50 ||
                identifier.Length == 0 ||
                result.Any(item => item.ProviderId.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                                   item.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
                return;
            result.Add(new DetectedWorkItemCandidate(provider, identifier, url));
        }
    }
}