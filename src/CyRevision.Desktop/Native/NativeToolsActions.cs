using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CyRevision.Code;
using CyRevision.Git;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop.Native;

/// <summary>Native-only presentation adapters; project operations remain in the shared engine.</summary>
public static class NativeToolsActions
{
    private sealed class ToolsState
    {
        public Guid? ProjectId;
        public HashSet<string> Expanded { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConditionalWeakTable<CodeTreeNode, NativeSolutionRow> Identities { get; } = new();
        public List<NativeSolutionRow> Rows { get; } = [];
    }

    public sealed class NativeSolutionRow(CodeTreeNode node)
    {
        internal CodeTreeNode Node { get; } = node;
        public string Name => Node.Name;
        public string RelativePath => Node.RelativePath;
        public bool IsDirectory => Node.IsDirectory;
        public string Language => Node.Language;
        public string Detail => Node.Detail;
        public long Size => Node.Size;
        public int Depth { get; internal set; }
        public bool Expanded { get; internal set; }
        public bool Selected { get; internal set; }
    }

    public sealed class FileHistoryRow(GitFileRevision entry)
    {
        internal GitFileRevision Entry { get; } = entry;
        public string Commit => Entry.Revision.ShortHash;
        public string CommitHash => Entry.Revision.Hash;
        public string Author => Entry.Revision.AuthorName;
        public string AuthorEmail => Entry.Revision.AuthorEmail;
        public DateTimeOffset Date => Entry.Revision.AuthoredAt;
        public string Change => Entry.Kind.ToString();
        public string Storage => Entry.LfsPointer is not null ? "Git LFS" : Entry.IsBinary ? "Git binary" : "Git";
        public long? Size => Entry.LfsPointer?.Size;
        public string LfsOid => Entry.LfsPointer?.OidSha256 ?? "";
        public string Message => Entry.Revision.Subject;
        public string Path => Entry.Path;
        public long? AddedLines => Entry.AddedLines;
        public long? DeletedLines => Entry.DeletedLines;
        public bool IsBinary => Entry.IsBinary;
    }

    public static IList ProjectFileHistory(IEnumerable<GitFileRevision> history) => history.Select(entry => new FileHistoryRow(entry)).ToArray();

    private static readonly ConditionalWeakTable<MainWindowViewModel, ToolsState> States = new();
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "tools.tree.toggle", "tools.tree.select", "tools.tree.refresh", "tools.saveSyncVersionSettings",
        "tools.openChatAttachment", "tools.openLogFolder", "tools.openUpdateRelease"
    };

    public static bool Handles(string command) => Commands.Contains(command);
    public static object Snapshot(MainWindowViewModel model) => new
    {
        imagePreviewPath = model.ExportNativeImage(model.AssetDiffPreview, "AssetDiffPreview"),
        codePreviewPath = model.ExportNativeImage(model.CodePreviewImage, "CodePreviewImage")
    };

    public static IList? GetList(MainWindowViewModel model, string name)
    {
        if (name != "NativeSolutionTreeRows") return null;
        ToolsState state = State(model);
        state.Rows.Clear();
        foreach (CodeTreeNode node in model.CodeTree) Add(node, 0);
        return state.Rows;

        void Add(CodeTreeNode node, int depth)
        {
            // The source itself is lazy. Never traverse placeholders, follow a
            // filesystem link or recursively enumerate unopened directories.
            if (node.IsPlaceholder || depth > 128 || state.Rows.Count >= 200_000) return;
            NativeSolutionRow row = state.Identities.GetValue(node, value => new NativeSolutionRow(value));
            row.Depth = depth;
            row.Expanded = node.IsDirectory && state.Expanded.Contains(node.RelativePath);
            row.Selected = ReferenceEquals(model.SelectedCodeNode, node);
            state.Rows.Add(row);
            if (row.Expanded) foreach (CodeTreeNode child in node.Children) Add(child, depth + 1);
        }
    }

    public static async Task<object?> ExecuteAsync(
        MainWindowViewModel model,
        JsonElement request,
        Func<string, IList?> listResolver)
    {
        _ = listResolver;
        string command = Text(request, "command");
        switch (command)
        {
            case "tools.tree.refresh":
                State(model).Expanded.Clear();
                await model.RefreshCodeWorkspaceAsync(preserveLoadedTree: false);
                break;
            case "tools.tree.toggle":
            case "tools.tree.select":
            {
                ProjectItemViewModel project = model.SelectedProject ?? throw new InvalidOperationException("Select a project first.");
                var rows = (IList<NativeSolutionRow>)(GetList(model, "NativeSolutionTreeRows") ?? throw new InvalidOperationException("The tree is unavailable."));
                string path = Text(request, "path"), key = Text(request, "key");
                NativeSolutionRow row = RequireTreeRow(rows, path, key);
                if (command == "tools.tree.select") model.SelectedCodeNode = row.Node;
                else
                {
                    if (!row.IsDirectory) throw new ArgumentException("Only folders can be expanded.");
                    ToolsState state = State(model);
                    if (!state.Expanded.Remove(path))
                    {
                        await model.LoadCodeDirectoryAsync(project, row.Node);
                        if (model.SelectedProject?.Id != project.Id)
                            throw new InvalidOperationException("The selected project changed.");
                        state.Expanded.Add(path);
                    }
                }
                break;
            }
            case "tools.saveSyncVersionSettings":
                await model.SaveBackupSettingsAsync();
                await model.SaveSyncthingSettingsAsync();
                break;
            case "tools.openChatAttachment":
            {
                string? path = await model.PrepareSelectedTeamChatAttachmentAsync();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new InvalidOperationException("Select a message containing an available attachment.");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                break;
            }
            case "tools.openLogFolder":
            {
                string? directory = Path.GetDirectoryName(model.CurrentApplicationLogPath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    throw new InvalidOperationException("The application log directory is not available yet.");
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
                break;
            }
            case "tools.openUpdateRelease":
            {
                if (!Uri.TryCreate(model.UpdateReleasePageUrl, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("No published release page is available.");
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
                break;
            }
            default: throw new ArgumentException("Unknown tools command.");
        }
        return new { message = model.StatusMessage };
    }

    private static ToolsState State(MainWindowViewModel model)
    {
        ToolsState state = States.GetValue(model, _ => new ToolsState());
        if (state.ProjectId != model.SelectedProject?.Id)
        {
            state.ProjectId = model.SelectedProject?.Id;
            state.Expanded.Clear();
            state.Rows.Clear();
        }
        return state;
    }

    public static NativeSolutionRow RequireTreeRow(IEnumerable<NativeSolutionRow> rows, string path, string key) =>
        rows.FirstOrDefault(item => item.RelativePath == path && NativeTable.Key(item) == key)
        ?? throw new InvalidOperationException("The explorer changed. Select the item again.");

    private static string Text(JsonElement request, string name) => request.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
