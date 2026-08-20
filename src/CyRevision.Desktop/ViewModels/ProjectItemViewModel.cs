using CyRevision.Core.Projects;

namespace CyRevision.Desktop.ViewModels;

public sealed class ProjectItemViewModel : ObservableObject
{
    public const string DefaultAccentColor = "#4E9F8A";

    private ProjectDefinition _definition;
    private string _branchName = "Not loaded";
    private string _loadDetail = "Select to load";
    private string _statusColor = "#7E8799";
    private bool _isLoading;
    private double _loadProgress;
    private bool _isSyncRunning;
    private bool _isSyncPaused;
    private bool _isVpnRunning;
    private int _unreadNotificationCount;

    public ProjectItemViewModel(ProjectDefinition definition)
    {
        _definition = definition;
    }

    public ProjectDefinition Definition => _definition;

    public Guid Id => Definition.Id;

    public string Name => Definition.Name;

    public string RootPath => Definition.RootPath;

    public string AccentColor => Definition.AccentColor ?? DefaultAccentColor;

    public string SidebarGroup => string.IsNullOrWhiteSpace(Definition.SidebarGroup)
        ? "General"
        : Definition.SidebarGroup.Trim();

    public string BranchName
    {
        get => _branchName;
        private set => SetProperty(ref _branchName, value);
    }

    public string LoadDetail
    {
        get => _loadDetail;
        private set => SetProperty(ref _loadDetail, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        private set => SetProperty(ref _statusColor, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public double LoadProgress
    {
        get => _loadProgress;
        private set => SetProperty(ref _loadProgress, value);
    }

    public string Mode
    {
        get
        {
            if (Definition.Features.GitEnabled && Definition.Features.PeerSyncEnabled)
            {
                return "Git + Sync";
            }

            if (Definition.Features.GitEnabled)
            {
                return "Git";
            }

            if (Definition.Features.PeerSyncEnabled && Definition.Features.BackupEnabled)
            {
                return "Sync + versions";
            }

            if (Definition.Features.PeerSyncEnabled)
            {
                return "Sync";
            }

            return "Backup";
        }
    }

    public string ModeAndServices => Mode +
        (IsSyncRunning ? IsSyncPaused ? " · Sync paused" : " · Sync" : string.Empty) +
        (IsVpnRunning ? " · VPN" : string.Empty);

    public int UnreadNotificationCount
    {
        get => _unreadNotificationCount;
        private set
        {
            if (SetProperty(ref _unreadNotificationCount, value))
            {
                OnPropertyChanged(nameof(HasUnreadNotifications));
                OnPropertyChanged(nameof(NotificationBadgeText));
            }
        }
    }

    public bool HasUnreadNotifications => UnreadNotificationCount > 0;
    public string NotificationBadgeText => UnreadNotificationCount > 99 ? "99+" : UnreadNotificationCount.ToString();

    public bool IsSyncRunning
    {
        get => _isSyncRunning;
        private set
        {
            if (SetProperty(ref _isSyncRunning, value)) OnPropertyChanged(nameof(ModeAndServices));
        }
    }

    public bool IsSyncPaused
    {
        get => _isSyncPaused;
        private set
        {
            if (SetProperty(ref _isSyncPaused, value)) OnPropertyChanged(nameof(ModeAndServices));
        }
    }

    public bool IsVpnRunning
    {
        get => _isVpnRunning;
        private set
        {
            if (SetProperty(ref _isVpnRunning, value)) OnPropertyChanged(nameof(ModeAndServices));
        }
    }

    public void SetSyncRuntime(bool running, bool paused)
    {
        IsSyncRunning = running;
        IsSyncPaused = running && paused;
    }

    public void SetVpnRuntime(bool running) => IsVpnRunning = running;

    public void SetUnreadNotifications(int count) => UnreadNotificationCount = Math.Max(0, count);

    public void Update(ProjectDefinition definition)
    {
        _definition = definition;
        OnPropertyChanged(nameof(Definition));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(RootPath));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ModeAndServices));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(SidebarGroup));
    }

    public void SetLoading(string detail, double progress)
    {
        IsLoading = true;
        LoadProgress = Math.Clamp(progress, 0, 100);
        LoadDetail = detail;
        StatusColor = "#61AFEF";
        if (Definition.Features.GitEnabled &&
            (BranchName == "Not loaded" || BranchName == "No Git"))
        {
            BranchName = "Loading branch…";
        }
    }

    public void SetGitState(string branchName, string detail)
    {
        BranchName = branchName;
        LoadDetail = detail;
        StatusColor = "#78D7B7";
    }

    public void SetLoaded(string detail = "Ready", string? branchName = null)
    {
        IsLoading = false;
        LoadProgress = 100;
        LoadDetail = detail;
        StatusColor = "#78D7B7";
        if (Definition.Features.GitEnabled && !string.IsNullOrWhiteSpace(branchName))
        {
            BranchName = branchName;
        }
        else if (!Definition.Features.GitEnabled)
        {
            BranchName = "No Git";
        }
    }

    public void SetLoadError(string detail)
    {
        IsLoading = false;
        LoadDetail = detail;
        StatusColor = "#E06C75";
    }

    public void SetLoadCancelled()
    {
        IsLoading = false;
        LoadDetail = "Load cancelled";
        StatusColor = "#7E8799";
        if (BranchName == "Loading branch…")
        {
            BranchName = "Not loaded";
        }
    }
}
