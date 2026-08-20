using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool HasWorkItemPlugins => GetActiveWorkItemPlugins().Count > 0;

    public IReadOnlyList<IWorkItemIntegrationPlugin> GetActiveWorkItemPlugins() =>
        _pluginManager.GetExtensions<IWorkItemIntegrationPlugin>();

    public void AddWorkItemReferencesToCommit(IEnumerable<WorkItemReference> references)
    {
        WorkItemReference[] selected = UniqueWorkItems(references, CommitMessage);
        if (selected.Length == 0) return;
        string line = "Tasks: " + string.Join(", ", selected.Select(item => item.CommitReference));
        CommitMessage = AppendParagraph(CommitMessage, line);
        StatusMessage = $"Added {selected.Length:N0} task link(s) to the commit message.";
    }

    public void AddWorkItemReferencesToPullRequest(
        IEnumerable<WorkItemReference> references,
        bool prefixTitleWithFirstKey)
    {
        WorkItemReference[] selected = UniqueWorkItems(references, NewPullRequestBody);
        if (selected.Length == 0) return;
        string lines = string.Join(Environment.NewLine, selected.Select(item => $"- {item.MarkdownReference}"));
        if (NewPullRequestBody.Contains("## Related tasks", StringComparison.OrdinalIgnoreCase))
            NewPullRequestBody = NewPullRequestBody.TrimEnd() + Environment.NewLine + lines;
        else
            NewPullRequestBody = AppendParagraph(NewPullRequestBody, "## Related tasks" + Environment.NewLine + lines);

        if (prefixTitleWithFirstKey && selected.Length > 0)
        {
            string keyPrefix = $"[{selected[0].DisplayKey}]";
            if (!NewPullRequestTitle.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
                NewPullRequestTitle = string.IsNullOrWhiteSpace(NewPullRequestTitle)
                    ? keyPrefix + " " + selected[0].Title
                    : keyPrefix + " " + NewPullRequestTitle.TrimStart();
        }
        StatusMessage = $"Added {selected.Length:N0} task link(s) to the pull request draft.";
    }

    private static WorkItemReference[] UniqueWorkItems(
        IEnumerable<WorkItemReference> references,
        string existingText) =>
        references
            .Where(item => !string.IsNullOrWhiteSpace(item.Url) &&
                           !existingText.Contains(item.Url, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private static string AppendParagraph(string current, string paragraph) =>
        string.IsNullOrWhiteSpace(current)
            ? paragraph
            : current.TrimEnd() + Environment.NewLine + Environment.NewLine + paragraph;
}
