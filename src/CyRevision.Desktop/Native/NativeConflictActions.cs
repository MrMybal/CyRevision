using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CyRevision.Desktop.ViewModels;
using CyRevision.Git;

namespace CyRevision.Desktop.Native;

/// <summary>Preserves the existing conflict resolver, including backup and explicit write/stage approval.</summary>
public static class NativeConflictActions
{
    private sealed class Session { public Guid? ProjectId; public GitConflictResolverViewModel? Resolver; }
    private static readonly ConditionalWeakTable<MainWindowViewModel, Session> Sessions = new();
    private const int MaximumEditableBytes = 750_000;
    private static Session Get(MainWindowViewModel model)
    {
        var session = Sessions.GetOrCreateValue(model);
        if (session.ProjectId != model.SelectedProject?.Id) { session.Resolver = null; session.ProjectId = model.SelectedProject?.Id; }
        return session;
    }
    public static void Dispose(MainWindowViewModel model) { if (Sessions.TryGetValue(model, out var session)) session.Resolver = null; }
    public static IList? GetList(MainWindowViewModel model, string name) => name == "NativeGitConflicts"
        ? (IList?)Get(model).Resolver?.Conflicts ?? Array.Empty<object>() : null;

    public static Dictionary<string, object?> Fields(MainWindowViewModel model)
    {
        var resolver = Get(model).Resolver;
        return new()
        {
            ["GitConflictEditorOpen"] = resolver is not null,
            ["GitConflictEditorTitle"] = resolver?.OperationTitle ?? "Git conflict resolver",
            ["GitConflictEditorSummary"] = resolver?.OperationSummary ?? "",
            ["GitConflictEditorStatus"] = resolver?.Status ?? "",
            ["GitConflictEditorSelectedKey"] = NativeTable.Key(resolver?.SelectedConflict),
            ["GitConflictEditorPath"] = resolver?.SelectedConflict?.Path ?? "",
            ["GitConflictEditorSelectedSummary"] = resolver?.SelectedConflictSummary ?? "",
            ["GitConflictEditorBase"] = resolver?.BaseText ?? "",
            ["GitConflictEditorOurs"] = resolver?.OursText ?? "",
            ["GitConflictEditorTheirs"] = resolver?.TheirsText ?? "",
            ["GitConflictEditorResult"] = resolver?.ResultText ?? "",
            ["GitConflictEditorReadOnly"] = resolver is null || resolver.ResultIsReadOnly || Encoding.UTF8.GetByteCount(resolver.ResultText) > MaximumEditableBytes,
            ["GitConflictEditorLarge"] = resolver is not null && Encoding.UTF8.GetByteCount(resolver.ResultText) > MaximumEditableBytes,
            ["GitConflictEditorBusy"] = resolver?.IsBusy ?? false,
            ["GitConflictEditorCanResolve"] = resolver?.CanResolveSelected ?? false,
            ["GitConflictEditorCanContinue"] = resolver?.CanContinue ?? false,
            ["GitConflictEditorCanAbort"] = resolver?.CanAbort ?? false,
            ["GitConflictEditorCanUseAi"] = resolver?.CanUseAi ?? false,
            ["GitConflictEditorCanPreviewAi"] = resolver?.CanPreviewAiProposal ?? false,
            ["GitConflictEditorBackup"] = resolver?.BackupBeforeResolve ?? true,
            ["GitConflictEditorRetention"] = resolver?.BackupRetentionDays ?? "30",
            ["GitConflictEditorBlocks"] = resolver?.ConflictBlockPosition ?? "",
            ["GitConflictEditorResolutionHint"] = resolver?.ResolutionHint ?? "",
            ["GitConflictEditorSource"] = resolver?.ResolutionSource ?? "",
            ["GitConflictEditorAiAdvice"] = resolver?.AiAdvice ?? "",
            ["GitConflictEditorAiProposal"] = resolver?.AiProposedResult ?? ""
        };
    }

    public static async Task<object?> ExecuteAsync(MainWindowViewModel model, JsonElement request)
    {
        var session = Get(model);
        string operation = Text(request, "operation");
        if (operation == "open")
        {
            var project = model.SelectedProject ?? throw new InvalidOperationException("Select a Git project first.");
            session.Resolver = new GitConflictResolverViewModel(new GitCliRepositoryService(), project.RootPath, project.Name,
                new GitConflictResolutionBackupService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyRevision", "conflict-resolutions")),
                model.CanUseAiConflictResolver ? model.GenerateAiConflictAssistanceAsync : null);
            await session.Resolver.InitializeAsync();
            return new { message = session.Resolver.Status };
        }
        GitConflictResolverViewModel resolver = session.Resolver ?? throw new InvalidOperationException("Open the conflict resolver first.");
        if (resolver.IsBusy) throw new InvalidOperationException("A conflict operation is already running.");
        if (operation == "close") { session.Resolver = null; await model.RefreshAsync(); return new { message = "Conflict resolver closed" }; }
        if (operation == "refresh") { await resolver.RefreshAsync(); return new { message = resolver.Status }; }
        if (operation == "select")
        {
            string key = Text(request, "key");
            resolver.SelectedConflict = resolver.Conflicts.FirstOrDefault(conflict => NativeTable.Key(conflict) == key)
                ?? throw new InvalidOperationException("The conflict list changed. Select the file again.");
            return new { message = resolver.SelectedConflictSummary };
        }
        if (operation == "backup")
        {
            string retention = Text(request, "retention", resolver.BackupRetentionDays);
            if (!int.TryParse(retention, out int days) || days is < 1 or > 3650) throw new ArgumentException("Backup retention must be between 1 and 3650 days.");
            resolver.BackupBeforeResolve = Bool(request, "enabled"); resolver.BackupRetentionDays = retention;
            return new { message = "Conflict backup preference updated" };
        }
        if (operation is "continue" or "abort")
        {
            RequireConfirmed(request);
            if (operation == "continue")
            {
                if (!resolver.CanContinue) throw new InvalidOperationException("Resolve every file before continuing the Git operation.");
                await resolver.ContinueAsync();
            }
            else
            {
                if (!resolver.CanAbort) throw new InvalidOperationException("No active Git operation can be aborted.");
                await resolver.AbortAsync();
            }
            await model.RefreshAsync();
            await resolver.RefreshAsync();
            return new { message = resolver.Status };
        }
        ValidateSelection(resolver.SelectedConflict, Text(request, "key"), Text(request, "path"));
        switch (operation)
        {
            case "setResult":
                string result = Text(request, "result");
                ValidateManualResult(resolver.SelectedConflict!, resolver.ResultText, result);
                resolver.ResultText = result; break;
            case "base": resolver.UseBase(); break;
            case "ours": resolver.UseOurs(); break;
            case "theirs": resolver.UseTheirs(); break;
            case "previousBlock": resolver.SelectPreviousConflictBlock(); break;
            case "nextBlock": resolver.SelectNextConflictBlock(); break;
            case "blockOurs": resolver.AcceptCurrentBlockOurs(); break;
            case "blockIncoming": resolver.AcceptCurrentBlockIncoming(); break;
            case "aiExplain": await resolver.AskAiAsync(false); break;
            case "aiPropose": await resolver.AskAiAsync(true); break;
            case "aiPreview": resolver.PreviewAiProposal(); break;
            case "resolve":
                RequireConfirmed(request);
                if (!resolver.CanResolveSelected) throw new InvalidOperationException("Remove conflict markers or choose a complete file version before resolving.");
                await resolver.ResolveSelectedAsync();
                await model.RefreshAsync();
                break;
            default: throw new ArgumentException("Unknown conflict resolver operation.");
        }
        return new { message = resolver.Status };
    }

    public static void ValidateSelection(GitConflictFile? selected, string key, string path)
    {
        if (selected is null || key != NativeTable.Key(selected) || path != selected.Path)
            throw new InvalidOperationException("The selected conflict changed. Reload it before editing or resolving it.");
    }
    public static void ValidateManualResult(GitConflictFile selected, string currentResult, string result)
    {
        if (!selected.CanEditManually || Encoding.UTF8.GetByteCount(currentResult) > MaximumEditableBytes)
            throw new InvalidOperationException("This file requires whole-file resolution or an external merge editor.");
        if (Encoding.UTF8.GetByteCount(result) > MaximumEditableBytes) throw new ArgumentException("The result is too large for the embedded editor.");
    }

    private static bool TryValue(JsonElement request, string name, out JsonElement value) => request.TryGetProperty(name, out value) ||
        request.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out value);
    private static string Text(JsonElement request, string name, string fallback = "") => TryValue(request, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static bool Bool(JsonElement request, string name) => TryValue(request, name, out var value) && value.ValueKind == JsonValueKind.True;
    private static void RequireConfirmed(JsonElement request) { if (!Bool(request, "confirmed")) throw new InvalidOperationException("Confirm this operation first."); }
}
