using System.Collections.ObjectModel;
using CyRevision.Core.Projects;
using CyRevision.Desktop.Workspace;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly AiConversationStore _aiConversationStore = new();
    private AiConversationViewModel? _selectedAiConversation;
    private string _newAiConversationTitle = string.Empty;
    private bool _suppressAiConversationSelection;

    public ObservableCollection<AiConversationViewModel> AiConversations { get; } = [];

    public AiConversationViewModel? SelectedAiConversation
    {
        get => _selectedAiConversation;
        set
        {
            AiConversationViewModel? previous = _selectedAiConversation;
            if (!SetProperty(ref _selectedAiConversation, value)) return;
            if (!_suppressAiConversationSelection)
            {
                if (previous is not null) CopyVisibleMessagesTo(previous);
                AiChatMessages.Clear();
                if (value is not null)
                {
                    foreach (AiChatMessageViewModel message in value.Messages) AiChatMessages.Add(message);
                    CodexChatThreadId = value.ThreadId;
                }
            }
            NotifyAiConversationProperties();
            if (!_suppressAiConversationSelection)
                _ = SwitchAiConversationAsync(previous, value);
        }
    }

    public string NewAiConversationTitle
    {
        get => _newAiConversationTitle;
        set => SetProperty(ref _newAiConversationTitle, value);
    }

    public bool AiRenderMarkdown
    {
        get => SelectedAiConversation?.RenderMarkdown ?? true;
        set
        {
            if (SelectedAiConversation is null || SelectedAiConversation.RenderMarkdown == value) return;
            SelectedAiConversation.RenderMarkdown = value;
            OnPropertyChanged();
            _ = SaveAiConversationsAsync();
        }
    }

    public string AiConversationPrePrompt
    {
        get => SelectedAiConversation?.PrePrompt ?? string.Empty;
        set
        {
            if (SelectedAiConversation is null || SelectedAiConversation.PrePrompt == value) return;
            SelectedAiConversation.PrePrompt = value;
            OnPropertyChanged();
        }
    }

    public bool AiConversationUseWorktree
    {
        get => SelectedAiConversation?.UseWorktree == true;
        set
        {
            if (SelectedAiConversation is null || SelectedAiConversation.UseWorktree == value) return;
            SelectedAiConversation.UseWorktree = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AiConversationWorkspace));
        }
    }

    public string AiConversationWorkspace => SelectedAiConversation?.WorkspaceLabel
                                             ?? SelectedProject?.RootPath
                                             ?? string.Empty;

    public bool HasAiConversation => SelectedAiConversation is not null;

    public async Task CreateAiConversationAsync()
    {
        ProjectItemViewModel? project = SelectedProject;
        if (project is null) return;
        await SaveCurrentAiConversationAsync().ConfigureAwait(true);

        string title = string.IsNullOrWhiteSpace(NewAiConversationTitle)
            ? $"Conversation {AiConversations.Count + 1}"
            : NewAiConversationTitle.Trim();
        AiConversationViewModel conversation = CreateConversation(project, title);
        AiConversations.Insert(0, conversation);
        NewAiConversationTitle = string.Empty;
        SelectedAiConversation = conversation;
        await SaveAiConversationsAsync().ConfigureAwait(true);
    }

    public async Task DeleteSelectedAiConversationAsync()
    {
        AiConversationViewModel? conversation = SelectedAiConversation;
        if (conversation is null) return;
        if (IsCodexChatConnected) await DisconnectCodexChatAsync(clearMessages: false).ConfigureAwait(true);

        int index = AiConversations.IndexOf(conversation);
        AiConversations.Remove(conversation);
        AiConversationViewModel? next = AiConversations.Count == 0
            ? SelectedProject is null ? null : CreateConversation(SelectedProject, "Project conversation")
            : AiConversations[Math.Clamp(index, 0, AiConversations.Count - 1)];
        if (next is not null && !AiConversations.Contains(next)) AiConversations.Add(next);
        SelectedAiConversation = next;
        await SaveAiConversationsAsync().ConfigureAwait(true);
    }

    public async Task SaveAiConversationSettingsAsync()
    {
        if (SelectedAiConversation is null) return;
        SelectedAiConversation.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveCurrentAiConversationAsync().ConfigureAwait(true);
        CodexConnectionStatus = "Conversation settings saved.";
    }

    private async Task SwitchAiConversationAsync(
        AiConversationViewModel? previous,
        AiConversationViewModel? conversation)
    {
        if (previous is not null) await SaveAiConversationsAsync().ConfigureAwait(true);
        if (IsCodexChatConnected) await DisconnectCodexChatAsync(clearMessages: false).ConfigureAwait(true);
        if (ReferenceEquals(SelectedAiConversation, conversation) && conversation is not null)
        {
            CodexChatThreadId = conversation.ThreadId;
            CodexConnectionStatus = string.IsNullOrWhiteSpace(conversation.ThreadId)
                ? "New conversation. Connect Codex when ready."
                : "Saved conversation loaded. Connect to resume its Codex thread.";
        }
        NotifyAiConversationProperties();
    }

    private async Task LoadAiConversationsForProjectAsync(ProjectItemViewModel? project)
    {
        await SaveCurrentAiConversationAsync().ConfigureAwait(true);
        if (project is null)
        {
            AiConversations.Clear();
            SetSelectedAiConversationWithoutSwitch(null);
            AiChatMessages.Clear();
            return;
        }

        IReadOnlyList<AiConversationViewModel> loaded = await _aiConversationStore.LoadAsync(
            project.Id, project.Name, project.RootPath).ConfigureAwait(true);
        AiConversations.Clear();
        foreach (AiConversationViewModel conversation in loaded) AiConversations.Add(conversation);
        if (AiConversations.Count == 0) AiConversations.Add(CreateConversation(project, "Project conversation"));

        AiConversationViewModel selected = AiConversations[0];
        SetSelectedAiConversationWithoutSwitch(selected);
        AiChatMessages.Clear();
        foreach (AiChatMessageViewModel message in selected.Messages) AiChatMessages.Add(message);
        CodexChatThreadId = selected.ThreadId;
        await SaveAiConversationsAsync().ConfigureAwait(true);
    }

    private async Task<string> ResolveAiConversationWorkspaceAsync(
        ProjectItemViewModel project,
        CancellationToken cancellationToken)
    {
        AiConversationViewModel? conversation = SelectedAiConversation;
        if (conversation?.UseWorktree != true) return project.RootPath;
        if (!string.IsNullOrWhiteSpace(conversation.WorktreePath) && Directory.Exists(conversation.WorktreePath))
            return conversation.WorktreePath;

        string shortId = conversation.Id.ToString("N")[..8];
        string branch = $"codex/ai-{shortId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        GitHistoricalWorktreeResult result = await _gitService.CreateHistoricalWorktreeAsync(
            project.RootPath, "HEAD", branch, cancellationToken).ConfigureAwait(true);
        conversation.WorktreePath = result.WorktreePath;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(AiConversationWorkspace));
        await SaveAiConversationsAsync(cancellationToken).ConfigureAwait(true);
        return result.WorktreePath;
    }

    private async Task SaveCurrentAiConversationAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAiConversation is null) return;
        CopyVisibleMessagesTo(SelectedAiConversation);
        SelectedAiConversation.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAiConversationsAsync(cancellationToken).ConfigureAwait(true);
    }

    private Task SaveAiConversationsAsync(CancellationToken cancellationToken = default)
    {
        string? root = SelectedAiConversation?.ProjectRoot ?? AiConversations.FirstOrDefault()?.ProjectRoot;
        return string.IsNullOrWhiteSpace(root)
            ? Task.CompletedTask
            : _aiConversationStore.SaveAsync(root, AiConversations, cancellationToken);
    }

    private void CopyVisibleMessagesTo(AiConversationViewModel conversation)
    {
        conversation.Messages.Clear();
        foreach (AiChatMessageViewModel message in AiChatMessages) conversation.Messages.Add(message);
    }

    private static AiConversationViewModel CreateConversation(ProjectItemViewModel project, string title) => new(
        Guid.NewGuid(), project.Id, project.Name, project.RootPath, title);

    private void SetSelectedAiConversationWithoutSwitch(AiConversationViewModel? conversation)
    {
        _suppressAiConversationSelection = true;
        try { SelectedAiConversation = conversation; }
        finally { _suppressAiConversationSelection = false; }
    }

    private void NotifyAiConversationProperties()
    {
        OnPropertyChanged(nameof(AiRenderMarkdown));
        OnPropertyChanged(nameof(AiConversationPrePrompt));
        OnPropertyChanged(nameof(AiConversationUseWorktree));
        OnPropertyChanged(nameof(AiConversationWorkspace));
        OnPropertyChanged(nameof(HasAiConversation));
    }
}
