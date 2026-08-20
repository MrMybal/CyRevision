using System.Collections.ObjectModel;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _projectNotificationSearch = string.Empty;
    private string _projectNotificationStateFilter = "All";

    public ObservableCollection<OperationTaskViewModel> ProjectNotifications { get; } = [];
    public IReadOnlyList<string> ProjectNotificationStateFilters { get; } = ["All", "New", "Read", "Alerts"];

    public bool ProjectNotificationsEnabled => SelectedProject?.Definition.ProjectNotificationsEnabled == true;

    public string ProjectNotificationSearch
    {
        get => _projectNotificationSearch;
        set
        {
            if (SetProperty(ref _projectNotificationSearch, value)) RefreshProjectNotifications();
        }
    }

    public string ProjectNotificationStateFilter
    {
        get => _projectNotificationStateFilter;
        set
        {
            if (SetProperty(ref _projectNotificationStateFilter, value)) RefreshProjectNotifications();
        }
    }

    public string ProjectNotificationSummary
    {
        get
        {
            int total = ProjectNotifications.Count;
            int unread = ProjectNotifications.Count(item => !item.IsRead);
            return ProjectNotificationsEnabled
                ? $"{total:N0} visible notification(s) · {unread:N0} unread"
                : "Notifications are disabled for this project. Tasks remain available in the activity center.";
        }
    }

    public async Task SetProjectNotificationsEnabledAsync(bool enabled)
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null || project.Definition.ProjectNotificationsEnabled == enabled) return;
        CyRevision.Core.Projects.ProjectDefinition updated = project.Definition with { ProjectNotificationsEnabled = enabled };
        updated.Validate();
        await _projectCatalog.UpsertAsync(updated);
        project.Update(updated);
        OnPropertyChanged(nameof(ProjectNotificationsEnabled));
        RefreshProjectNotifications();
        _applicationLogService.Information("notifications", $"project notifications enabled={enabled}", project.RootPath);
    }

    public void MarkProjectNotificationsRead()
    {
        string? projectName = SelectedProject?.Name;
        if (projectName is null) return;
        foreach (OperationTaskViewModel task in RecentOperations.Where(item =>
                     item.IsNotification && string.Equals(item.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)))
            task.MarkRead();
        RefreshProjectNotifications();
    }

    private void PromoteCompletedOperationToNotification(OperationTaskViewModel task)
    {
        ProjectItemViewModel? project = Projects.FirstOrDefault(item =>
            string.Equals(item.Name, task.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (project?.Definition.ProjectNotificationsEnabled != true) return;
        if (task.IsAttention || IsImportantProjectOperation(task.Title)) task.PromoteToNotification();
    }

    private static bool IsImportantProjectOperation(string title)
    {
        string value = title.ToLowerInvariant();
        string[] important =
        [
            "commit", "push", "pull", "fetch", "merge", "branch", "conflict", "restore", "rollback",
            "backup", "snapshot", "archive", "prune", "git lfs", "sync commit", "vpn", "build",
            "workflow", "pull request", "install", "update"
        ];
        return important.Any(value.Contains);
    }

    private void RefreshProjectNotifications()
    {
        string? projectName = SelectedProject?.Name;
        IEnumerable<OperationTaskViewModel> notifications = projectName is null
            ? []
            : RecentOperations.Where(item => item.IsNotification &&
                                              string.Equals(item.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
        string search = ProjectNotificationSearch.Trim();
        if (search.Length > 0)
            notifications = notifications.Where(item =>
                $"{item.Title} {item.Detail} {item.State}".Contains(search, StringComparison.CurrentCultureIgnoreCase));
        notifications = ProjectNotificationStateFilter switch
        {
            "New" => notifications.Where(item => !item.IsRead),
            "Read" => notifications.Where(item => item.IsRead),
            "Alerts" => notifications.Where(item => item.IsAttention),
            _ => notifications
        };
        ReplaceCollection(ProjectNotifications, notifications);
        foreach (ProjectItemViewModel project in Projects)
        {
            int unread = RecentOperations.Count(item => item.IsNotification && !item.IsRead &&
                                                      string.Equals(item.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase));
            project.SetUnreadNotifications(unread);
        }
        OnPropertyChanged(nameof(ProjectNotificationSummary));
    }
}
