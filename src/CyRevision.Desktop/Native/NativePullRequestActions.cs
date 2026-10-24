using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;
using CyRevision.Plugin.Abstractions;
using CyRevision.PullRequests;

namespace CyRevision.Desktop.Native;

/// <summary>Project-scoped native adapters for workflows formerly owned by the Avalonia code-behind.</summary>
public static class NativePullRequestActions
{
    private sealed class ReviewState
    {
        public Guid ProjectId;
        public int BranchNumber;
        public GitLocalBranchRemovalAnalysis? BranchAnalysis;
        public WorkItemConnectionSettings? TaskSettings;
        public WorkItemReference[] TaskResults = [];
        public string TaskStatus = "Select a provider, then configure or search for tasks.";
        public int AskTasksForNumber;
        public CancellationTokenSource? Operation;
    }

    private static readonly ConditionalWeakTable<MainWindowViewModel, ReviewState> States = new();
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "prMerge", "prReview", "prComment", "prToggle", "prCheckout", "prCompleteTasks", "prDismissTaskPrompt",
        "prAnalyzeBranchRemoval", "prRemoveBranch", "prCiCancel", "ciCancel", "prCiRerun", "ciRerun",
        "openCiRun", "openPrCiRun", "taskLoadProvider", "taskSave", "taskSearch", "taskAdd"
    };

    public static bool Handles(string command) => Commands.Contains(command);

    public static void Cancel(MainWindowViewModel model)
    {
        if (!States.TryGetValue(model, out ReviewState? state)) return;
        try { state.Operation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static ReviewState State(MainWindowViewModel model)
    {
        ReviewState state = States.GetValue(model, _ => new ReviewState());
        Guid project = model.SelectedProject?.Id ?? Guid.Empty;
        if (state.ProjectId != project)
        {
            state.ProjectId = project;
            state.BranchAnalysis = null;
            state.TaskSettings = null;
            state.TaskResults = [];
            state.AskTasksForNumber = 0;
            state.TaskStatus = "Select a provider, then configure or search for tasks.";
        }
        return state;
    }

    public static IList? TryList(MainWindowViewModel model, string name) => name == "NativeWorkItemSearch"
        ? State(model).TaskResults : null;

    public static object Snapshot(MainWindowViewModel model)
    {
        ReviewState state = State(model);
        PullRequestSummary? selected = model.SelectedPullRequest;
        CiWorkflowRun? ciRun = model.SelectedCiRun is { } run
            ? CiStatePresentation.ApplyJobState(run, model.CiJobs) : null;
        return new
        {
            selected,
            ciRun,
            prCiRun = model.SelectedPullRequestCiRun,
            canCancelCi = ciRun?.IsRunning == true && !model.IsCiLoading,
            canRerunCi = ciRun?.HasFailed == true && !model.IsCiLoading,
            canDispatchCi = model.SelectedCiWorkflow is not null && !model.IsCiLoading,
            needsTaskConfirmation = selected is { IsMerged: true } && selected.Number == state.AskTasksForNumber,
            branchAnalysis = selected?.Number == state.BranchNumber ? state.BranchAnalysis : null,
            taskProviders = model.GetActiveWorkItemPlugins().Select(plugin => plugin.Provider).ToArray(),
            taskSettings = DescribeTaskSettings(state.TaskSettings),
            taskStatus = state.TaskStatus,
            taskResultCount = state.TaskResults.Length,
            prJobSteps = model.SelectedPullRequestCiJob?.Steps,
        };
    }

    public static object? DescribeTaskSettings(WorkItemConnectionSettings? settings) => settings is null ? null : new
    {
        settings.BaseUrl,
        settings.ScopeId,
        settings.AccountName,
        settings.TokenEnvironmentVariable,
    };

    public static async Task<object?> ExecuteAsync(MainWindowViewModel model, JsonElement request,
        Func<string, IList?> listResolver)
    {
        string command = Text(request, "command");
        if (!Handles(command)) throw new ArgumentException("Unknown review command.");
        ReviewState state = State(model);
        using CancellationTokenSource operation = new();
        state.Operation = operation;
        try
        {
        switch (command)
        {
            case "prMerge":
                Confirm(request);
                RequirePullRequest(model, request);
                if (!model.CanMergeSelectedPullRequest || model.SelectedPullRequest!.IsDraft)
                    throw new InvalidOperationException("Select an open, non-draft pull request before merging.");
                if (await model.MergeSelectedPullRequestAsync())
                {
                    if (model.ShouldAutomaticallyUpdatePullRequestTasksAfterMerge)
                        await model.CompleteLinkedPullRequestTasksAsync();
                    else if (model.ShouldAskToUpdatePullRequestTasksAfterMerge)
                        state.AskTasksForNumber = model.SelectedPullRequest!.Number;
                }
                break;
            case "prCompleteTasks":
                Confirm(request);
                RequirePullRequest(model, request);
                if (model.SelectedPullRequest is not { IsMerged: true })
                    throw new InvalidOperationException("Task completion is available after a successful merge.");
                await model.CompleteLinkedPullRequestTasksAsync();
                state.AskTasksForNumber = 0;
                break;
            case "prDismissTaskPrompt":
                RequirePullRequest(model, request);
                state.AskTasksForNumber = 0;
                break;
            case "prReview":
                Confirm(request);
                RequirePullRequest(model, request);
                if (!model.CanReviewSelectedPullRequest) throw new InvalidOperationException(model.PullRequestReviewAvailability);
                await model.SubmitPullRequestReviewAsync();
                break;
            case "prComment":
                Confirm(request);
                RequirePullRequest(model, request);
                if (string.IsNullOrWhiteSpace(model.PullRequestComment)) throw new ArgumentException("Enter a comment first.");
                await model.AddPullRequestCommentAsync();
                break;
            case "prToggle":
                Confirm(request);
                RequirePullRequest(model, request);
                if (model.SelectedPullRequest!.IsMerged) throw new InvalidOperationException("Merged pull requests cannot be reopened.");
                await model.ToggleSelectedPullRequestStateAsync();
                break;
            case "prCheckout":
                Confirm(request);
                RequirePullRequest(model, request);
                await model.CheckoutSelectedPullRequestAsync();
                break;
            case "prAnalyzeBranchRemoval":
                RequirePullRequest(model, request);
                state.BranchAnalysis = await model.AnalyzeSelectedPullRequestLocalBranchRemovalAsync()
                    ?? throw new InvalidOperationException("The merged local branch cannot be analyzed.");
                state.BranchNumber = model.SelectedPullRequest!.Number;
                break;
            case "prRemoveBranch":
                Confirm(request);
                RequirePullRequest(model, request);
                // Re-check Git immediately before removal. An earlier preview is never authority to delete.
                GitLocalBranchRemovalAnalysis analysis = await model.AnalyzeSelectedPullRequestLocalBranchRemovalAsync()
                    ?? throw new InvalidOperationException("The merged local branch cannot be analyzed.");
                bool force = Flag(request, "force");
                if (force ? !analysis.CanForceRemove : !analysis.CanRemoveSafely)
                    throw new InvalidOperationException(analysis.SafetyMessage);
                await model.RemoveSelectedPullRequestLocalBranchAsync(force);
                state.BranchAnalysis = null;
                break;
            case "prCiCancel":
            case "prCiRerun":
                Confirm(request);
                RequirePullRequest(model, request);
                CiWorkflowRun prRun = RequireRun(model.SelectedPullRequestCiRun, request);
                ValidateJob(request, model.PullRequestCiJobs);
                if (command == "prCiCancel")
                {
                    if (!model.CanCancelSelectedPullRequestCiRun) throw new InvalidOperationException("This workflow run is no longer active.");
                    await model.CancelSelectedPullRequestCiRunAsync();
                }
                else
                {
                    if (!prRun.HasFailed) throw new InvalidOperationException("Select a failed workflow run.");
                    await model.RerunSelectedPullRequestCiAsync();
                }
                break;
            case "ciCancel":
            case "ciRerun":
                Confirm(request);
                CiWorkflowRun selectedRun = RequireRun(model.SelectedCiRun, request);
                ValidateJob(request, model.CiJobs);
                selectedRun = CiStatePresentation.ApplyJobState(selectedRun, model.CiJobs);
                if (command == "ciCancel")
                {
                    if (!selectedRun.IsRunning) throw new InvalidOperationException("This workflow run is no longer active.");
                    await model.CancelSelectedCiRunAsync();
                }
                else
                {
                    if (!selectedRun.HasFailed) throw new InvalidOperationException("Select a failed workflow run.");
                    await model.RerunFailedCiJobsAsync();
                }
                break;
            case "openCiRun":
                OpenWeb(RequireRun(model.SelectedCiRun, request).WebUrl);
                break;
            case "openPrCiRun":
                RequirePullRequest(model, request);
                OpenWeb(RequireRun(model.SelectedPullRequestCiRun, request).WebUrl);
                break;
            case "taskLoadProvider":
            case "taskSave":
            case "taskSearch":
                IWorkItemIntegrationPlugin provider = model.GetActiveWorkItemPlugins().FirstOrDefault(plugin =>
                    plugin.Provider.Id.Equals(Text(request, "provider"), StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Enable this task provider for the selected project first.");
                Guid projectId = model.SelectedProject?.Id ?? throw new InvalidOperationException("Select a project first.");
                WorkItemConnectionSettings settings = await provider.LoadConnectionAsync(projectId,operation.Token);
                if (command != "taskLoadProvider" && request.TryGetProperty("settings", out JsonElement supplied))
                    settings = new(projectId, Text(supplied,"baseUrl"), Text(supplied,"scopeId"),
                        Text(supplied,"accountName"), Text(supplied,"tokenEnvironmentVariable"));
                state.TaskSettings = settings;
                string? token = Text(request, "sessionToken");
                if (command == "taskLoadProvider")
                {
                    state.TaskResults = [];
                    state.TaskStatus = provider.Provider.Description;
                }
                else if (command == "taskSave")
                {
                    await provider.SaveConnectionAsync(settings,operation.Token);
                    WorkItemConnectionTestResult result = await provider.TestConnectionAsync(settings, token,operation.Token);
                    state.TaskStatus = result.Message;
                }
                else
                {
                    state.TaskResults = (await provider.SearchAsync(settings, token, Text(request,"query"), 100,operation.Token)).Take(100).ToArray();
                    state.TaskStatus = $"{state.TaskResults.Length} task(s) found. Select one or several rows to add their links.";
                }
                break;
            case "taskAdd":
                IList results = listResolver("NativeWorkItemSearch") ?? state.TaskResults;
                if (!request.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
                    throw new ArgumentException("Select at least one task.");
                List<WorkItemReference> tasks = [];
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    int index = row.GetProperty("index").GetInt32();
                    if (index < 0 || index >= results.Count || NativeTable.Key(results[index]) != Text(row,"key"))
                        throw new InvalidOperationException("The task results changed. Select the tasks again.");
                    if (results[index] is WorkItemReference task) tasks.Add(task);
                }
                if (tasks.Count == 0) throw new ArgumentException("Select at least one task.");
                if (Flag(request,"forPullRequest",true)) model.AddWorkItemReferencesToPullRequest(tasks, Flag(request,"prefixTitle",true));
                else model.AddWorkItemReferencesToCommit(tasks);
                break;
        }
        return new { message = model.StatusMessage };
        }
        finally
        {
            if (ReferenceEquals(state.Operation,operation)) state.Operation = null;
        }
    }

    public static void ValidateConfirmation(JsonElement request) => Confirm(request);

    public static PullRequestSummary RequirePullRequest(MainWindowViewModel model, JsonElement request)
    {
        PullRequestSummary selected = model.SelectedPullRequest ?? throw new InvalidOperationException("Select a pull request first.");
        if (!request.TryGetProperty("number", out var number) || !number.TryGetInt32(out int expected) || selected.Number != expected)
            throw new InvalidOperationException("The pull request changed. Review the selected request again.");
        return selected;
    }

    public static CiWorkflowRun RequireRun(CiWorkflowRun? run, JsonElement request)
    {
        if (run is null || !request.TryGetProperty("runId", out var id) || !id.TryGetInt64(out long expected) || run.Id != expected)
            throw new InvalidOperationException("The workflow run changed. Select the run again.");
        return run;
    }

    private static void ValidateJob(JsonElement request, IEnumerable<CiWorkflowJob> jobs)
    {
        if (request.TryGetProperty("jobId", out var id) && (!id.TryGetInt64(out long expected) || !jobs.Any(job => job.Id == expected)))
            throw new InvalidOperationException("The job list changed. Select the job again.");
    }

    private static void OpenWeb(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) throw new ArgumentException("Unsupported web link.");
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }

    private static void Confirm(JsonElement request)
    {
        if (!Flag(request,"confirmed")) throw new InvalidOperationException("Confirm this action before continuing.");
    }
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key,out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static bool Flag(JsonElement value, string key, bool fallback=false) => value.TryGetProperty(key,out JsonElement item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : fallback;
}
