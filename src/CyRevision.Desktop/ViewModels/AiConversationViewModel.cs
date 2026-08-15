using System.Collections.ObjectModel;

namespace CyRevision.Desktop.ViewModels;

public sealed class AiConversationViewModel : ObservableObject
{
    private string _title;
    private string _threadId;
    private string _prePrompt;
    private bool _renderMarkdown;
    private bool _useWorktree;
    private string _worktreePath;
    private DateTimeOffset _updatedAt;

    public AiConversationViewModel(
        Guid id,
        Guid projectId,
        string projectName,
        string projectRoot,
        string title,
        string threadId = "",
        string prePrompt = "",
        bool renderMarkdown = true,
        bool useWorktree = false,
        string worktreePath = "",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        Id = id;
        ProjectId = projectId;
        ProjectName = projectName;
        ProjectRoot = projectRoot;
        _title = title;
        _threadId = threadId;
        _prePrompt = prePrompt;
        _renderMarkdown = renderMarkdown;
        _useWorktree = useWorktree;
        _worktreePath = worktreePath;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        _updatedAt = updatedAt ?? CreatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public string ProjectRoot { get; }
    public DateTimeOffset CreatedAt { get; }
    public ObservableCollection<AiChatMessageViewModel> Messages { get; } = [];

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, string.IsNullOrWhiteSpace(value) ? "New conversation" : value.Trim());
    }

    public string ThreadId
    {
        get => _threadId;
        set => SetProperty(ref _threadId, value ?? string.Empty);
    }

    public string PrePrompt
    {
        get => _prePrompt;
        set => SetProperty(ref _prePrompt, value ?? string.Empty);
    }

    public bool RenderMarkdown
    {
        get => _renderMarkdown;
        set => SetProperty(ref _renderMarkdown, value);
    }

    public bool UseWorktree
    {
        get => _useWorktree;
        set
        {
            if (!SetProperty(ref _useWorktree, value)) return;
            OnPropertyChanged(nameof(WorkspaceLabel));
        }
    }

    public string WorktreePath
    {
        get => _worktreePath;
        set
        {
            if (!SetProperty(ref _worktreePath, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(WorkspaceLabel));
        }
    }

    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (!SetProperty(ref _updatedAt, value)) return;
            OnPropertyChanged(nameof(UpdatedText));
        }
    }

    public string UpdatedText => UpdatedAt.ToLocalTime().ToString("g");

    public string WorkspaceLabel => UseWorktree
        ? string.IsNullOrWhiteSpace(WorktreePath) ? "Worktree will be created on connect" : WorktreePath
        : ProjectRoot;
}
