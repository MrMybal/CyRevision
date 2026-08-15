using CyRevision.Desktop.ViewModels;
using CyRevision.Desktop.Workspace;

namespace CyRevision.Core.Tests;

public sealed class AiConversationStoreTests
{
    [Fact]
    public async Task SavesAndRestoresProjectConversationSettingsAndMessages()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "CyRevisionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        try
        {
            Guid projectId = Guid.NewGuid();
            AiConversationViewModel conversation = new(
                Guid.NewGuid(), projectId, "Sample", projectRoot, "Review assets",
                "thread-42", "Answer as a reviewer.", renderMarkdown: true,
                useWorktree: true, worktreePath: Path.Combine(projectRoot, "worktree"));
            conversation.Messages.Add(new AiChatMessageViewModel("user", "Inspect `Content/`."));
            conversation.Messages.Add(new AiChatMessageViewModel("assistant", "## Result\n- Ready"));

            AiConversationStore store = new();
            await store.SaveAsync(projectRoot, [conversation]);
            IReadOnlyList<AiConversationViewModel> restored = await store.LoadAsync(
                projectId, "Sample", projectRoot);

            AiConversationViewModel item = Assert.Single(restored);
            Assert.Equal("Review assets", item.Title);
            Assert.Equal("thread-42", item.ThreadId);
            Assert.Equal("Answer as a reviewer.", item.PrePrompt);
            Assert.True(item.RenderMarkdown);
            Assert.True(item.UseWorktree);
            Assert.Equal(2, item.Messages.Count);
            Assert.Contains("Result", item.Messages[1].Text);
        }
        finally
        {
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }
}
