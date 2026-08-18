using System.Collections.ObjectModel;
using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitConflictResolverViewModel : ObservableObject
{
    private readonly IGitRepositoryService _gitService;
    private readonly GitConflictResolutionBackupService? _backupService;
    private readonly Func<GitConflictFile, bool, CancellationToken, Task<string?>>? _aiAssistant;
    private GitConflictState _state = new(GitConflictOperation.None, []);
    private GitConflictFile? _selectedConflict;
    private GitConflictResolutionChoice _pendingChoice = GitConflictResolutionChoice.Manual;
    private string _resultText = string.Empty;
    private string _status = "Loading Git conflicts…";
    private bool _isBusy;
    private bool _backupBeforeResolve = true;
    private string _backupRetentionDays = "30";
    private string _aiAdvice = "AI advice is optional. Nothing proposed by AI is applied automatically.";
    private string _aiProposedResult = string.Empty;
    private int _activeConflictBlock;

    public GitConflictResolverViewModel(
        IGitRepositoryService gitService,
        string repositoryPath,
        string projectName,
        GitConflictResolutionBackupService? backupService = null,
        Func<GitConflictFile, bool, CancellationToken, Task<string?>>? aiAssistant = null)
    {
        _gitService = gitService;
        RepositoryPath = Path.GetFullPath(repositoryPath);
        ProjectName = projectName;
        _backupService = backupService;
        _aiAssistant = aiAssistant;
    }

    public event EventHandler? RepositoryChanged;
    public event EventHandler? OperationCompleted;

    public ObservableCollection<GitConflictFile> Conflicts { get; } = [];

    public string RepositoryPath { get; }
    public string ProjectName { get; }

    public bool CanUseAi => _aiAssistant is not null && SelectedConflict is not null && !IsBusy;

    public bool BackupBeforeResolve
    {
        get => _backupBeforeResolve;
        set => SetProperty(ref _backupBeforeResolve, value);
    }

    public string BackupRetentionDays
    {
        get => _backupRetentionDays;
        set => SetProperty(ref _backupRetentionDays, value);
    }

    public string AiAdvice
    {
        get => _aiAdvice;
        private set => SetProperty(ref _aiAdvice, value);
    }

    public string AiProposedResult
    {
        get => _aiProposedResult;
        private set
        {
            if (SetProperty(ref _aiProposedResult, value)) OnPropertyChanged(nameof(CanPreviewAiProposal));
        }
    }

    public bool CanPreviewAiProposal => !string.IsNullOrWhiteSpace(AiProposedResult) && !ResultIsReadOnly && !IsBusy;

    public int ConflictBlockCount => ParseConflictBlocks(ResultText).Count;
    public bool HasConflictBlocks => ConflictBlockCount > 0 && !ResultIsReadOnly;
    public string ConflictBlockPosition => HasConflictBlocks
        ? $"Block {Math.Clamp(_activeConflictBlock + 1, 1, ConflictBlockCount)} / {ConflictBlockCount}"
        : "No marker block";

    public GitConflictFile? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (!SetProperty(ref _selectedConflict, value)) return;
            ResetResultFromWorkingTree();
            NotifySelectionState();
        }
    }

    public string ResultText
    {
        get => _resultText;
        set
        {
            if (!SetProperty(ref _resultText, value)) return;
            if (!_isApplyingChoice) _pendingChoice = GitConflictResolutionChoice.Manual;
            NotifyResolutionState();
        }
    }

    public string BaseText => SelectedConflict?.Base.DisplayText ?? "Select a conflicted file.";
    public string OursText => SelectedConflict?.Ours.DisplayText ?? "Select a conflicted file.";
    public string TheirsText => SelectedConflict?.Theirs.DisplayText ?? "Select a conflicted file.";

    public string BaseSummary => VersionSummary("Common base", SelectedConflict?.Base);
    public string OursSummary => VersionSummary("Our branch", SelectedConflict?.Ours);
    public string TheirsSummary => VersionSummary("Incoming branch", SelectedConflict?.Theirs);

    public string OperationTitle => $"{_state.OperationText} conflict resolver";

    public string OperationSummary => _state.Operation == GitConflictOperation.None
        ? "No merge, cherry-pick, rebase, or revert is active."
        : $"{_state.OperationText} in progress · {Conflicts.Count:N0} unresolved file(s)";

    public string SelectedConflictSummary => SelectedConflict is null
        ? "Select a conflict to inspect Base, Result, and Incoming."
        : $"{SelectedConflict.ConflictType} · {(SelectedConflict.CanEditManually ? "editable text" : "whole-file resolution")}";

    public string ResolutionSource => _pendingChoice switch
    {
        GitConflictResolutionChoice.Base => "BASE",
        GitConflictResolutionChoice.Ours => "OURS",
        GitConflictResolutionChoice.Theirs => "THEIRS",
        _ => HasConflictMarkers ? "UNRESOLVED MARKERS" : "MANUAL RESULT"
    };

    public string ResolutionColor => HasConflictMarkers ? "#E06C75" : "#78D7B7";

    public string ResolutionHint => SelectedConflict is null
        ? "Select a file."
        : ResultIsReadOnly
            ? "This file cannot be edited as text. Choose Base, Ours, or Theirs, or use an external merge tool."
            : HasConflictMarkers
                ? "Remove every <<<<<<<, =======, and >>>>>>> marker before marking the file resolved."
                : "The center panel is the exact file CyRevision will write and stage.";

    public bool ResultIsReadOnly => SelectedConflict is null || !SelectedConflict.CanEditManually;

    public bool HasConflictMarkers =>
        _pendingChoice == GitConflictResolutionChoice.Manual && ContainsConflictMarkers(ResultText);

    public bool CanResolveSelected =>
        SelectedConflict is not null && !IsBusy &&
        (_pendingChoice != GitConflictResolutionChoice.Manual ||
         SelectedConflict.CanEditManually && !HasConflictMarkers);

    public bool CanContinue => !IsBusy && Conflicts.Count == 0 && _state.Operation is not GitConflictOperation.None;
    public bool CanAbort => !IsBusy && _state.Operation is not GitConflictOperation.None;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanResolveSelected));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanAbort));
            OnPropertyChanged(nameof(CanUseAi));
            OnPropertyChanged(nameof(CanPreviewAiProposal));
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private bool _isApplyingChoice;

    public Task InitializeAsync() => RefreshAsync();

    public void UseBase() => ApplyChoice(GitConflictResolutionChoice.Base, SelectedConflict?.Base);
    public void UseOurs() => ApplyChoice(GitConflictResolutionChoice.Ours, SelectedConflict?.Ours);
    public void UseTheirs() => ApplyChoice(GitConflictResolutionChoice.Theirs, SelectedConflict?.Theirs);

    public void SelectPreviousConflictBlock()
    {
        int count = ConflictBlockCount;
        if (count == 0) return;
        _activeConflictBlock = (_activeConflictBlock - 1 + count) % count;
        NotifyConflictBlocks();
    }

    public void SelectNextConflictBlock()
    {
        int count = ConflictBlockCount;
        if (count == 0) return;
        _activeConflictBlock = (_activeConflictBlock + 1) % count;
        NotifyConflictBlocks();
    }

    public void AcceptCurrentBlockOurs() => AcceptCurrentBlock(useIncoming: false);
    public void AcceptCurrentBlockIncoming() => AcceptCurrentBlock(useIncoming: true);

    public async Task AskAiAsync(bool proposeResolution)
    {
        if (SelectedConflict is null || _aiAssistant is null) return;
        IsBusy = true;
        Status = proposeResolution ? "Asking AI for a proposed result…" : "Asking AI for conflict advice…";
        try
        {
            string? response = await _aiAssistant(SelectedConflict, proposeResolution, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(response))
            {
                Status = "The connected AI did not return a response.";
                return;
            }
            if (proposeResolution)
            {
                AiProposedResult = ExtractCodeBlock(response);
                AiAdvice = response;
                Status = "AI proposal received. Preview it in Result before marking the file resolved.";
            }
            else
            {
                AiAdvice = response;
                Status = "AI advice received; the result was not modified.";
            }
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void PreviewAiProposal()
    {
        if (!CanPreviewAiProposal) return;
        ResultText = AiProposedResult;
        Status = "AI proposal copied into Result for review. It is not staged until you mark the file resolved.";
    }

    public async Task ResolveSelectedAsync()
    {
        if (SelectedConflict is null || !CanResolveSelected) return;
        string path = SelectedConflict.Path;
        IsBusy = true;
        Status = $"Resolving {path}…";
        try
        {
            if (BackupBeforeResolve && _backupService is not null)
            {
                int retention = int.TryParse(BackupRetentionDays, out int parsed) ? Math.Clamp(parsed, 1, 3650) : 30;
                await _backupService.CreateAsync(
                    ProjectName,
                    RepositoryPath,
                    SelectedConflict,
                    ResultText,
                    ResolutionSource,
                    retention);
            }
            await _gitService.ResolveConflictAsync(
                RepositoryPath,
                path,
                _pendingChoice,
                _pendingChoice == GitConflictResolutionChoice.Manual ? ResultText : null);
            RepositoryChanged?.Invoke(this, EventArgs.Empty);
            await RefreshCoreAsync(path);
            Status = Conflicts.Count == 0
                ? "Every file is resolved. Review the index, then continue the Git operation."
                : $"{path} resolved and staged · {Conflicts.Count:N0} conflict(s) remaining";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        Status = "Reading Base, Ours, and Theirs from the Git index…";
        try
        {
            await RefreshCoreAsync(SelectedConflict?.Path);
            Status = Conflicts.Count == 0
                ? _state.Operation == GitConflictOperation.None
                    ? "No unresolved Git conflict."
                    : "Every file is resolved. Continue or abort the Git operation."
                : $"{Conflicts.Count:N0} unresolved conflict(s) loaded";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ContinueAsync()
    {
        if (!CanContinue) return;
        IsBusy = true;
        Status = $"Continuing {_state.OperationText.ToLowerInvariant()}…";
        try
        {
            await _gitService.ContinueConflictOperationAsync(RepositoryPath);
            RepositoryChanged?.Invoke(this, EventArgs.Empty);
            Status = $"{_state.OperationText} completed locally.";
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AbortAsync()
    {
        if (!CanAbort) return;
        IsBusy = true;
        Status = $"Aborting {_state.OperationText.ToLowerInvariant()}…";
        try
        {
            await _gitService.AbortConflictOperationAsync(RepositoryPath);
            RepositoryChanged?.Invoke(this, EventArgs.Empty);
            Status = $"{_state.OperationText} aborted; the pre-operation state was restored.";
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync(string? previousPath)
    {
        _state = await _gitService.GetConflictStateAsync(RepositoryPath);
        Conflicts.Clear();
        foreach (GitConflictFile conflict in _state.Files) Conflicts.Add(conflict);
        SelectedConflict = Conflicts.FirstOrDefault(item =>
                               item.Path.Equals(previousPath, StringComparison.OrdinalIgnoreCase))
                           ?? Conflicts.FirstOrDefault();
        OnPropertyChanged(nameof(OperationTitle));
        OnPropertyChanged(nameof(OperationSummary));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanAbort));
    }

    private void ApplyChoice(GitConflictResolutionChoice choice, GitConflictVersion? version)
    {
        if (SelectedConflict is null || version is null) return;
        _pendingChoice = choice;
        _isApplyingChoice = true;
        try
        {
            ResultText = version.Exists ? version.DisplayText : "[Resolution will delete this file]";
        }
        finally
        {
            _isApplyingChoice = false;
        }
        NotifyResolutionState();
    }

    private void ResetResultFromWorkingTree()
    {
        _pendingChoice = GitConflictResolutionChoice.Manual;
        _isApplyingChoice = true;
        try
        {
            if (SelectedConflict is null)
            {
                ResultText = string.Empty;
            }
            else if (SelectedConflict.CanEditManually)
            {
                ResultText = SelectedConflict.WorkingText ?? SelectedConflict.Ours.Text ?? SelectedConflict.Theirs.Text ?? string.Empty;
            }
            else
            {
                GitConflictVersion version = SelectedConflict.Ours.Exists
                    ? SelectedConflict.Ours
                    : SelectedConflict.Theirs;
                _pendingChoice = SelectedConflict.Ours.Exists
                    ? GitConflictResolutionChoice.Ours
                    : GitConflictResolutionChoice.Theirs;
                ResultText = version.DisplayText;
            }
        }
        finally
        {
            _isApplyingChoice = false;
        }
        _activeConflictBlock = 0;
        AiProposedResult = string.Empty;
        AiAdvice = "AI advice is optional. Nothing proposed by AI is applied automatically.";
    }

    private void NotifySelectionState()
    {
        OnPropertyChanged(nameof(BaseText));
        OnPropertyChanged(nameof(OursText));
        OnPropertyChanged(nameof(TheirsText));
        OnPropertyChanged(nameof(BaseSummary));
        OnPropertyChanged(nameof(OursSummary));
        OnPropertyChanged(nameof(TheirsSummary));
        OnPropertyChanged(nameof(SelectedConflictSummary));
        OnPropertyChanged(nameof(ResultIsReadOnly));
        OnPropertyChanged(nameof(CanUseAi));
        NotifyResolutionState();
    }

    private void NotifyResolutionState()
    {
        OnPropertyChanged(nameof(ResolutionSource));
        OnPropertyChanged(nameof(ResolutionColor));
        OnPropertyChanged(nameof(ResolutionHint));
        OnPropertyChanged(nameof(HasConflictMarkers));
        OnPropertyChanged(nameof(CanResolveSelected));
        NotifyConflictBlocks();
    }

    private void AcceptCurrentBlock(bool useIncoming)
    {
        IReadOnlyList<ConflictBlock> blocks = ParseConflictBlocks(ResultText);
        if (blocks.Count == 0) return;
        _activeConflictBlock = Math.Clamp(_activeConflictBlock, 0, blocks.Count - 1);
        ConflictBlock block = blocks[_activeConflictBlock];
        string replacement = useIncoming ? block.Incoming : block.Ours;
        ResultText = ResultText[..block.Start] + replacement + ResultText[block.End..];
        _activeConflictBlock = Math.Min(_activeConflictBlock, Math.Max(0, ConflictBlockCount - 1));
        Status = useIncoming ? "Incoming block accepted into Result." : "Our block accepted into Result.";
        NotifyConflictBlocks();
    }

    private void NotifyConflictBlocks()
    {
        OnPropertyChanged(nameof(ConflictBlockCount));
        OnPropertyChanged(nameof(HasConflictBlocks));
        OnPropertyChanged(nameof(ConflictBlockPosition));
    }

    private static IReadOnlyList<ConflictBlock> ParseConflictBlocks(string text)
    {
        List<ConflictBlock> blocks = [];
        int search = 0;
        while (search < text.Length)
        {
            int start = text.IndexOf("<<<<<<<", search, StringComparison.Ordinal);
            if (start < 0) break;
            int oursStart = text.IndexOf('\n', start);
            if (oursStart < 0) break;
            oursStart++;
            int separator = text.IndexOf("=======", oursStart, StringComparison.Ordinal);
            if (separator < 0) break;
            int incomingStart = text.IndexOf('\n', separator);
            if (incomingStart < 0) break;
            incomingStart++;
            int markerEnd = text.IndexOf(">>>>>>>", incomingStart, StringComparison.Ordinal);
            if (markerEnd < 0) break;
            int end = text.IndexOf('\n', markerEnd);
            end = end < 0 ? text.Length : end + 1;
            string ours = text[oursStart..separator].TrimEnd('\r', '\n') + Environment.NewLine;
            string incoming = text[incomingStart..markerEnd].TrimEnd('\r', '\n') + Environment.NewLine;
            blocks.Add(new ConflictBlock(start, end, ours, incoming));
            search = end;
        }
        return blocks;
    }

    private static string ExtractCodeBlock(string response)
    {
        int start = response.IndexOf("```", StringComparison.Ordinal);
        if (start < 0) return response.Trim();
        int contentStart = response.IndexOf('\n', start);
        if (contentStart < 0) return response.Trim();
        int end = response.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
        return end < 0 ? response[(contentStart + 1)..].TrimEnd() : response[(contentStart + 1)..end].TrimEnd();
    }

    private sealed record ConflictBlock(int Start, int End, string Ours, string Incoming);

    private static string VersionSummary(string label, GitConflictVersion? version) => version is null
        ? label
        : !version.Exists
            ? $"{label} · deleted"
            : $"{label} · {version.SizeText} · {version.ShortObjectId}";

    private static bool ContainsConflictMarkers(string text)
    {
        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("<<<<<<< ", StringComparison.Ordinal) ||
                line.Equals("=======", StringComparison.Ordinal) ||
                line.StartsWith(">>>>>>> ", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
