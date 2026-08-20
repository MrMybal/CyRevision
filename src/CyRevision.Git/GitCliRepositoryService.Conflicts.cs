using System.Text;

namespace CyRevision.Git;

public sealed partial class GitCliRepositoryService
{
    private const long InlineConflictTextLimit = 1024 * 1024;

    private static readonly HashSet<string> KnownBinaryConflictExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".uasset", ".umap", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".exr", ".webp",
        ".fbx", ".obj", ".gltf", ".glb", ".wav", ".mp3", ".ogg", ".flac", ".mp4", ".mov", ".avi",
        ".zip", ".7z", ".rar", ".gz", ".bin", ".dll", ".exe", ".pdb", ".so", ".dylib", ".pdf"
    };

    public async Task<GitConflictState> GetConflictStateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string repository = Path.GetFullPath(repositoryPath);
        ProcessResult unmerged = await RunGitResultAsync(
            repository,
            ["ls-files", "--unmerged", "-z"],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(unmerged, "Unable to inspect Git conflicts.");

        Dictionary<string, Dictionary<int, ConflictStage>> stages = ParseUnmergedEntries(unmerged.StandardOutput);
        await PopulateConflictSizesAsync(repository, stages, cancellationToken).ConfigureAwait(false);
        List<GitConflictFile> files = new(stages.Count);
        foreach ((string path, Dictionary<int, ConflictStage> fileStages) in stages.OrderBy(
                     item => item.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GitConflictVersion @base = await ReadConflictVersionAsync(repository, path, fileStages, 1, cancellationToken)
                .ConfigureAwait(false);
            GitConflictVersion ours = await ReadConflictVersionAsync(repository, path, fileStages, 2, cancellationToken)
                .ConfigureAwait(false);
            GitConflictVersion theirs = await ReadConflictVersionAsync(repository, path, fileStages, 3, cancellationToken)
                .ConfigureAwait(false);
            string fullPath = GetSafeWorkingTreePath(repository, path);
            bool workingTreeExists = File.Exists(fullPath);
            string? workingText = null;
            if (workingTreeExists && !@base.IsBinary && !ours.IsBinary && !theirs.IsBinary &&
                !@base.IsTooLarge && !ours.IsTooLarge && !theirs.IsTooLarge)
            {
                FileInfo workingFile = new(fullPath);
                if (workingFile.Length <= InlineConflictTextLimit)
                {
                    workingText = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                }
            }

            files.Add(new GitConflictFile(path, @base, ours, theirs, workingText, workingTreeExists));
        }

        GitConflictOperation operation = await DetectConflictOperationAsync(repository, cancellationToken)
            .ConfigureAwait(false);
        return new GitConflictState(operation, files);
    }

    public async Task ResolveConflictAsync(
        string repositoryPath,
        string relativePath,
        GitConflictResolutionChoice choice,
        string? manualResult = null,
        CancellationToken cancellationToken = default)
    {
        string repository = Path.GetFullPath(repositoryPath);
        string safePath = NormalizeAndValidateGitPath(relativePath);
        GitConflictState state = await GetConflictStateAsync(repository, cancellationToken).ConfigureAwait(false);
        GitConflictFile conflict = state.Files.FirstOrDefault(item =>
            item.Path.Equals(safePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new GitOperationException($"'{safePath}' is no longer an unresolved Git conflict.");

        if (choice == GitConflictResolutionChoice.Manual)
        {
            if (!conflict.CanEditManually)
                throw new GitOperationException("Binary and oversized conflicts must use Base, Ours, Theirs, or an external merge tool.");
            if (manualResult is null)
                throw new GitOperationException("A manual resolution result is required.");
            if (ContainsConflictMarkers(manualResult))
                throw new GitOperationException("The result still contains Git conflict markers.");

            string target = GetSafeWorkingTreePath(repository, safePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + $".cyrevision-conflict-{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, manualResult, new UTF8Encoding(false), cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            await StageAsync(repository, [safePath], cancellationToken).ConfigureAwait(false);
            return;
        }

        (int stage, GitConflictVersion version) = choice switch
        {
            GitConflictResolutionChoice.Base => (1, conflict.Base),
            GitConflictResolutionChoice.Ours => (2, conflict.Ours),
            GitConflictResolutionChoice.Theirs => (3, conflict.Theirs),
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null)
        };

        if (!version.Exists)
        {
            string target = GetSafeWorkingTreePath(repository, safePath);
            if (File.Exists(target)) File.Delete(target);
            ProcessResult remove = await RunGitResultAsync(
                repository,
                ["rm", "--ignore-unmatch", "--", safePath],
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(remove, $"Unable to resolve '{safePath}' as deleted.");
            return;
        }

        ProcessResult checkout = await RunGitResultAsync(
            repository,
            ["checkout-index", "--force", $"--stage={stage}", "--", safePath],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(checkout, $"Unable to restore the selected version of '{safePath}'.");
        await StageAsync(repository, [safePath], cancellationToken).ConfigureAwait(false);
    }

    public async Task ContinueConflictOperationAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        GitConflictState state = await GetConflictStateAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        if (state.HasConflicts)
            throw new GitOperationException($"Resolve all {state.Files.Count} conflict(s) before continuing.");

        IReadOnlyCollection<string> arguments = state.Operation switch
        {
            GitConflictOperation.Merge => ["commit", "--no-edit"],
            GitConflictOperation.CherryPick => ["-c", "core.editor=true", "cherry-pick", "--continue"],
            GitConflictOperation.Rebase => ["-c", "core.editor=true", "rebase", "--continue"],
            GitConflictOperation.Revert => ["-c", "core.editor=true", "revert", "--continue"],
            GitConflictOperation.None => throw new GitOperationException("There is no Git operation to continue."),
            _ => throw new GitOperationException("CyRevision cannot safely continue this unknown Git operation.")
        };
        await RunGitWithoutOutputAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbortConflictOperationAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        GitConflictOperation operation = await DetectConflictOperationAsync(repositoryPath, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyCollection<string> arguments = operation switch
        {
            GitConflictOperation.Merge => ["merge", "--abort"],
            GitConflictOperation.CherryPick => ["cherry-pick", "--abort"],
            GitConflictOperation.Rebase => ["rebase", "--abort"],
            GitConflictOperation.Revert => ["revert", "--abort"],
            GitConflictOperation.None => throw new GitOperationException("There is no Git operation to abort."),
            _ => throw new GitOperationException("CyRevision cannot safely abort this unknown Git operation.")
        };
        await RunGitWithoutOutputAsync(repositoryPath, arguments, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitConflictVersion> ReadConflictVersionAsync(
        string repository,
        string path,
        IReadOnlyDictionary<int, ConflictStage> stages,
        int stage,
        CancellationToken cancellationToken)
    {
        if (!stages.TryGetValue(stage, out ConflictStage? entry))
            return new GitConflictVersion(false, null, 0, null, false, false);

        bool knownBinary = KnownBinaryConflictExtensions.Contains(Path.GetExtension(path));
        bool tooLarge = entry.Size > InlineConflictTextLimit;
        if (knownBinary || tooLarge)
            return new GitConflictVersion(true, entry.ObjectId, entry.Size, null, knownBinary, tooLarge);

        ProcessResult contents = await RunGitResultAsync(
            repository,
            ["cat-file", "blob", entry.ObjectId],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(contents, $"Unable to read stage {stage} of '{path}'.");
        bool binary = contents.StandardOutput.IndexOf('\0') >= 0;
        return new GitConflictVersion(
            true,
            entry.ObjectId,
            entry.Size,
            binary ? null : contents.StandardOutput,
            binary,
            false);
    }

    private async Task<GitConflictOperation> DetectConflictOperationAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        if (await RefExistsAsync(repositoryPath, "MERGE_HEAD", cancellationToken).ConfigureAwait(false))
            return GitConflictOperation.Merge;
        if (await RefExistsAsync(repositoryPath, "CHERRY_PICK_HEAD", cancellationToken).ConfigureAwait(false))
            return GitConflictOperation.CherryPick;
        if (await RefExistsAsync(repositoryPath, "REVERT_HEAD", cancellationToken).ConfigureAwait(false))
            return GitConflictOperation.Revert;
        if (await GitPathExistsAsync(repositoryPath, "rebase-merge", cancellationToken).ConfigureAwait(false) ||
            await GitPathExistsAsync(repositoryPath, "rebase-apply", cancellationToken).ConfigureAwait(false))
            return GitConflictOperation.Rebase;

        ProcessResult unmerged = await RunGitResultAsync(
            repositoryPath,
            ["ls-files", "--unmerged"],
            cancellationToken).ConfigureAwait(false);
        return unmerged.Succeeded && !string.IsNullOrWhiteSpace(unmerged.StandardOutput)
            ? GitConflictOperation.Unknown
            : GitConflictOperation.None;
    }

    private async Task<bool> RefExistsAsync(string repositoryPath, string reference, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(
            repositoryPath,
            ["rev-parse", "--quiet", "--verify", reference],
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private async Task<bool> GitPathExistsAsync(string repositoryPath, string name, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunGitResultAsync(
            repositoryPath,
            ["rev-parse", "--git-path", name],
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput)) return false;
        string path = result.StandardOutput.Trim();
        string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryPath, path));
        return Directory.Exists(fullPath) || File.Exists(fullPath);
    }

    private static Dictionary<string, Dictionary<int, ConflictStage>> ParseUnmergedEntries(string output)
    {
        Dictionary<string, Dictionary<int, ConflictStage>> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = record.IndexOf('\t');
            if (tab <= 0 || tab == record.Length - 1) continue;
            string[] metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3 || !int.TryParse(metadata[2], out int stage)) continue;
            string path = record[(tab + 1)..].Replace('\\', '/');
            if (!result.TryGetValue(path, out Dictionary<int, ConflictStage>? fileStages))
            {
                fileStages = [];
                result[path] = fileStages;
            }
            fileStages[stage] = new ConflictStage(metadata[1], 0);
        }
        return result;
    }

    private async Task PopulateConflictSizesAsync(
        string repository,
        Dictionary<string, Dictionary<int, ConflictStage>> stages,
        CancellationToken cancellationToken)
    {
        foreach (Dictionary<int, ConflictStage> fileStages in stages.Values)
        foreach ((int stage, ConflictStage entry) in fileStages.ToArray())
        {
            ProcessResult size = await RunGitResultAsync(
                repository,
                ["cat-file", "-s", entry.ObjectId],
                cancellationToken).ConfigureAwait(false);
            if (size.Succeeded && long.TryParse(size.StandardOutput.Trim(), out long length))
                fileStages[stage] = entry with { Size = length };
        }
    }

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

    private sealed record ConflictStage(string ObjectId, long Size);
}
