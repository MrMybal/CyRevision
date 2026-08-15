using System.Text.Json;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Workspace;

public sealed class AiConversationStore
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<AiConversationViewModel>> LoadAsync(
        Guid projectId,
        string projectName,
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectRoot);
        if (!File.Exists(path)) return [];

        try
        {
            await using FileStream stream = File.OpenRead(path);
            AiConversationDocument? document = await JsonSerializer.DeserializeAsync<AiConversationDocument>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (document is null) return [];

            return document.Conversations
                .Where(item => item.ProjectId == projectId)
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => ToViewModel(item, projectName, projectRoot))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public async Task SaveAsync(
        string projectRoot,
        IEnumerable<AiConversationViewModel> conversations,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = GetPath(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AiConversationDocument document = new(
                1,
                conversations.Select(ToState).OrderByDescending(item => item.UpdatedAt).ToArray());
            string temporaryPath = path + ".tmp";
            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static string GetPath(string projectRoot) =>
        Path.Combine(Path.GetFullPath(projectRoot), ".cyrevision", "ai", "conversations.json");

    private static AiConversationViewModel ToViewModel(
        AiConversationState state,
        string projectName,
        string projectRoot)
    {
        AiConversationViewModel conversation = new(
            state.Id,
            state.ProjectId,
            projectName,
            projectRoot,
            state.Title,
            state.ThreadId,
            state.PrePrompt,
            state.RenderMarkdown,
            state.UseWorktree,
            state.WorktreePath,
            state.CreatedAt,
            state.UpdatedAt);
        foreach (AiConversationMessageState message in state.Messages)
            conversation.Messages.Add(new AiChatMessageViewModel(message.Role, message.Text, message.Timestamp));
        return conversation;
    }

    private static AiConversationState ToState(AiConversationViewModel conversation) => new(
        conversation.Id,
        conversation.ProjectId,
        conversation.Title,
        conversation.ThreadId,
        conversation.PrePrompt,
        conversation.RenderMarkdown,
        conversation.UseWorktree,
        conversation.WorktreePath,
        conversation.CreatedAt,
        conversation.UpdatedAt,
        conversation.Messages
            .Select(message => new AiConversationMessageState(message.Role, message.Text, message.Timestamp))
            .ToArray());

    private sealed record AiConversationDocument(int SchemaVersion, IReadOnlyList<AiConversationState> Conversations);

    private sealed record AiConversationState(
        Guid Id,
        Guid ProjectId,
        string Title,
        string ThreadId,
        string PrePrompt,
        bool RenderMarkdown,
        bool UseWorktree,
        string WorktreePath,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<AiConversationMessageState> Messages);

    private sealed record AiConversationMessageState(string Role, string Text, DateTimeOffset Timestamp);
}
