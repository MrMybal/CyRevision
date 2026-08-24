using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyRevision.Plugin.Abstractions;

namespace CyRevision.Plugin.CyStore;

public sealed class CyStorePlugin : ICyStorePlugin, IProjectModeProvider
{
    public const string PluginId = "cyrevision.cystore";
    public const string ModeId = "git-cystore-alpha";
    public const string AlgorithmName = "cyrevision-gear-cdc-v1";

    private const int MinimumChunkBytes = 1 * 1024 * 1024;
    private const int AverageChunkBytes = 4 * 1024 * 1024;
    private const int MaximumChunkBytes = 8 * 1024 * 1024;
    private const ulong BoundaryMask = AverageChunkBytes - 1UL;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly ulong[] GearTable = BuildGearTable();
    private readonly SemaphoreSlim _storeGate = new(1, 1);

    public CyRevisionPluginDescriptor Descriptor { get; } = new(
        PluginId,
        "CyStore Alpha",
        "0.1.0",
        "Alpha content-defined chunk storage for hydrated Git LFS files. Git stays fully compatible and authoritative.",
        "Storage");

    public IReadOnlyList<PluginProjectModeDescriptor> ProjectModes { get; } =
    [
        new(
            ModeId,
            "Git + CyStore — ALPHA",
            "Experimental Git-compatible mode. Commits, branches, remotes and standard Git LFS pointers remain unchanged; hydrated LFS files can additionally be captured as verified, deduplicated chunks.",
            new PluginProjectModeFeatures(true, true, false, true, true),
            new PluginProjectModeRetention(PluginProjectModeRetentionKind.Timeline, 20, 180),
            ["CyStoreWorkspaceTab", "ChangesWorkspaceTab", "HistoryWorkspaceTab", "BranchesWorkspaceTab", "GitLfsWorkspaceTab", "BackupsWorkspaceTab"],
            "CyStore — ALPHA")
    ];

    public PluginProjectModeAvailability EvaluateProjectMode(
        string modeId,
        PluginProjectModeContext context)
    {
        if (!string.Equals(modeId, ModeId, StringComparison.OrdinalIgnoreCase))
        {
            return new PluginProjectModeAvailability(false, $"Unknown CyStore mode '{modeId}'.");
        }

        bool isGitRepository = Directory.Exists(Path.Combine(context.ProjectRoot, ".git")) ||
                               File.Exists(Path.Combine(context.ProjectRoot, ".git"));
        return isGitRepository
            ? new PluginProjectModeAvailability(
                true,
                "Git repository detected. CyStore is opt-in and will not initialize or capture files until explicitly requested.")
            : new PluginProjectModeAvailability(
                false,
                "Git + CyStore — ALPHA requires an existing Git repository. CyRevision never initializes it implicitly.");
    }

    public Task InitializeAsync(
        CyRevisionPluginContext context,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public CyStoreStatus InspectStore(string projectRoot)
    {
        StorePaths paths = StorePaths.For(projectRoot);
        if (!File.Exists(paths.SettingsPath) || !File.Exists(paths.IndexPath))
        {
            return new CyStoreStatus(
                false,
                paths.Root,
                AlgorithmName,
                0,
                0,
                0,
                0,
                null,
                "CyStore has not been initialized for this project.");
        }

        try
        {
            StoreIndex index = ReadJson<StoreIndex>(paths.IndexPath) ?? new StoreIndex();
            DirectoryInfo chunkDirectory = new(paths.Chunks);
            FileInfo[] chunkFiles = chunkDirectory.Exists
                ? chunkDirectory.EnumerateFiles("*.chunk", SearchOption.AllDirectories).ToArray()
                : [];
            long storedBytes = chunkFiles.Sum(file => file.Length);
            long logicalBytes = index.Versions.Sum(version => version.Size);
            DateTimeOffset? updated = index.Versions.Count == 0
                ? File.GetLastWriteTimeUtc(paths.IndexPath)
                : index.Versions.Max(version => version.CapturedAt);

            return new CyStoreStatus(
                true,
                paths.Root,
                AlgorithmName,
                index.Versions.Count,
                chunkFiles.Length,
                logicalBytes,
                storedBytes,
                updated,
                $"{index.Versions.Count} version(s), {chunkFiles.Length} unique chunk(s), {FormatBytes(storedBytes)} stored.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CyStoreStatus(
                true,
                paths.Root,
                AlgorithmName,
                0,
                0,
                0,
                0,
                null,
                $"CyStore metadata could not be read: {exception.Message}");
        }
    }

    public async Task<CyStoreStatus> InitializeStoreAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        string root = NormalizeProjectRoot(projectRoot);
        EnsureGitRepository(root);
        StorePaths paths = StorePaths.For(root);

        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(paths.Root);
            Directory.CreateDirectory(paths.Chunks);
            Directory.CreateDirectory(paths.Manifests);
            Directory.CreateDirectory(paths.Restored);

            if (!File.Exists(paths.SettingsPath))
            {
                await WriteJsonAtomicAsync(
                    paths.SettingsPath,
                    new StoreSettings(
                        1,
                        AlgorithmName,
                        MinimumChunkBytes,
                        AverageChunkBytes,
                        MaximumChunkBytes,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            if (!File.Exists(paths.IndexPath))
            {
                await WriteJsonAtomicAsync(paths.IndexPath, new StoreIndex(), cancellationToken);
            }

            EnsureLocalGitExclusion(root);
        }
        finally
        {
            _storeGate.Release();
        }

        return InspectStore(root);
    }

    public async Task<CyStoreCaptureResult> CaptureFileAsync(
        string projectRoot,
        string filePath,
        bool isGitLfsTracked,
        IProgress<CyStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string root = NormalizeProjectRoot(projectRoot);
        StorePaths paths = RequireInitialized(root);
        string fullPath = Path.GetFullPath(Path.IsPathRooted(filePath) ? filePath : Path.Combine(root, filePath));
        EnsureInsideRoot(root, fullPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file to capture does not exist.", fullPath);
        if (IsLfsPointerFile(fullPath))
            throw new InvalidOperationException("This file is only a Git LFS pointer. Hydrate it before capturing it in CyStore.");

        string relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            StoreIndex index = await ReadIndexAsync(paths, cancellationToken);
            CaptureArtifacts artifacts = await ChunkFileAsync(paths, relativePath, fullPath, progress, cancellationToken);
            VersionEntry? existing = index.Versions.FirstOrDefault(version =>
                string.Equals(version.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(version.FileSha256, artifacts.Manifest.FileSha256, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return new CyStoreCaptureResult(
                    ToContract(existing),
                    0,
                    artifacts.Manifest.Chunks.Count,
                    0,
                    artifacts.Manifest.Size,
                    $"The identical version of {relativePath} is already captured.");
            }

            await WriteJsonAtomicAsync(
                ManifestPath(paths, artifacts.Manifest.FileSha256),
                artifacts.Manifest,
                cancellationToken);

            VersionEntry entry = new(
                Guid.NewGuid().ToString("N"),
                relativePath,
                artifacts.Manifest.FileSha256,
                artifacts.Manifest.Size,
                artifacts.Manifest.Chunks.Count,
                DateTimeOffset.UtcNow,
                isGitLfsTracked,
                "Captured");
            index.Versions.Add(entry);
            await WriteJsonAtomicAsync(paths.IndexPath, index, cancellationToken);

            return new CyStoreCaptureResult(
                ToContract(entry),
                artifacts.NewChunks,
                artifacts.ReusedChunks,
                artifacts.WrittenBytes,
                artifacts.ReusedBytes,
                $"Captured {relativePath}: {artifacts.NewChunks} new and {artifacts.ReusedChunks} reused chunk(s).");
        }
        finally
        {
            _storeGate.Release();
        }
    }

    public async Task<CyStoreBatchCaptureResult> CaptureTrackedGitLfsFilesAsync(
        string projectRoot,
        IProgress<CyStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string root = NormalizeProjectRoot(projectRoot);
        RequireInitialized(root);
        IReadOnlyList<string> trackedFiles = await ListTrackedLfsFilesAsync(root, cancellationToken);
        List<string> warnings = [];
        int captured = 0;
        int skipped = 0;
        int newChunks = 0;
        long writtenBytes = 0;

        for (int index = 0; index < trackedFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = trackedFiles[index].Replace('\\', '/');
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            progress?.Report(new CyStoreProgress(
                "Scanning Git LFS",
                relativePath,
                index,
                trackedFiles.Count,
                index,
                trackedFiles.Count));

            if (!File.Exists(fullPath))
            {
                skipped++;
                warnings.Add($"{relativePath}: not present in the working tree.");
                continue;
            }

            if (IsLfsPointerFile(fullPath))
            {
                skipped++;
                warnings.Add($"{relativePath}: pointer only; hydrate it before capture.");
                continue;
            }

            try
            {
                CyStoreCaptureResult result = await CaptureFileAsync(
                    root,
                    fullPath,
                    true,
                    progress,
                    cancellationToken);
                captured++;
                newChunks += result.NewChunks;
                writtenBytes += result.WrittenBytes;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                skipped++;
                warnings.Add($"{relativePath}: {exception.Message}");
            }
        }

        progress?.Report(new CyStoreProgress(
            "Git LFS capture complete",
            "",
            trackedFiles.Count,
            trackedFiles.Count,
            trackedFiles.Count,
            trackedFiles.Count));

        return new CyStoreBatchCaptureResult(
            captured,
            skipped,
            newChunks,
            writtenBytes,
            warnings,
            $"Captured {captured} hydrated Git LFS file(s); skipped {skipped}; wrote {FormatBytes(writtenBytes)}.");
    }

    public async Task<IReadOnlyList<CyStoreVersion>> ListVersionsAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        StorePaths paths = RequireInitialized(NormalizeProjectRoot(projectRoot));
        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            StoreIndex index = await ReadIndexAsync(paths, cancellationToken);
            return index.Versions
                .OrderByDescending(version => version.CapturedAt)
                .Select(ToContract)
                .ToArray();
        }
        finally
        {
            _storeGate.Release();
        }
    }

    public async Task<CyStoreVerificationResult> VerifyVersionAsync(
        string projectRoot,
        string versionId,
        IProgress<CyStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string root = NormalizeProjectRoot(projectRoot);
        StorePaths paths = RequireInitialized(root);
        (VersionEntry entry, FileManifest manifest) = await LoadVersionAsync(paths, versionId, cancellationToken);
        using IncrementalHash fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long verifiedBytes = 0;

        for (int index = 0; index < manifest.Chunks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChunkReference chunk = manifest.Chunks[index];
            string chunkPath = ChunkPath(paths, chunk.Sha256);
            if (!File.Exists(chunkPath))
            {
                return new CyStoreVerificationResult(
                    false,
                    index,
                    verifiedBytes,
                    $"Missing chunk {chunk.Sha256} for {entry.RelativePath}.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(chunkPath, cancellationToken);
            string chunkHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(chunkHash, chunk.Sha256, StringComparison.OrdinalIgnoreCase) ||
                bytes.LongLength != chunk.Size)
            {
                return new CyStoreVerificationResult(
                    false,
                    index,
                    verifiedBytes,
                    $"Chunk verification failed for {chunk.Sha256}.");
            }

            fileHash.AppendData(bytes);
            verifiedBytes += bytes.LongLength;
            progress?.Report(new CyStoreProgress(
                "Verifying",
                entry.RelativePath,
                verifiedBytes,
                manifest.Size,
                index + 1,
                manifest.Chunks.Count));
        }

        string finalHash = Convert.ToHexString(fileHash.GetHashAndReset()).ToLowerInvariant();
        bool succeeded = verifiedBytes == manifest.Size &&
                         string.Equals(finalHash, manifest.FileSha256, StringComparison.OrdinalIgnoreCase);
        return new CyStoreVerificationResult(
            succeeded,
            manifest.Chunks.Count,
            verifiedBytes,
            succeeded
                ? $"Verified {entry.RelativePath} ({FormatBytes(verifiedBytes)})."
                : $"Final file hash verification failed for {entry.RelativePath}.");
    }

    public async Task<CyStoreReconstructionResult> ReconstructVersionAsync(
        string projectRoot,
        string versionId,
        IProgress<CyStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string root = NormalizeProjectRoot(projectRoot);
        StorePaths paths = RequireInitialized(root);
        (VersionEntry entry, FileManifest manifest) = await LoadVersionAsync(paths, versionId, cancellationToken);

        string versionDirectory = Path.Combine(paths.Restored, entry.Id[..Math.Min(12, entry.Id.Length)]);
        string destination = Path.GetFullPath(Path.Combine(
            versionDirectory,
            entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideRoot(paths.Restored, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";

        long reconstructed = 0;
        try
        {
            await using FileStream output = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                256 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using IncrementalHash fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            for (int index = 0; index < manifest.Chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChunkReference chunk = manifest.Chunks[index];
                string chunkPath = ChunkPath(paths, chunk.Sha256);
                if (!File.Exists(chunkPath)) throw new InvalidDataException($"Missing CyStore chunk {chunk.Sha256}.");

                byte[] bytes = await File.ReadAllBytesAsync(chunkPath, cancellationToken);
                string chunkHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(chunkHash, chunk.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"CyStore chunk {chunk.Sha256} is corrupted.");

                await output.WriteAsync(bytes, cancellationToken);
                fileHash.AppendData(bytes);
                reconstructed += bytes.LongLength;
                progress?.Report(new CyStoreProgress(
                    "Reconstructing",
                    entry.RelativePath,
                    reconstructed,
                    manifest.Size,
                    index + 1,
                    manifest.Chunks.Count));
            }

            await output.FlushAsync(cancellationToken);
            string finalHash = Convert.ToHexString(fileHash.GetHashAndReset()).ToLowerInvariant();
            if (reconstructed != manifest.Size ||
                !string.Equals(finalHash, manifest.FileSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The reconstructed file did not pass its final SHA-256 verification.");
            }
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        File.Move(temporary, destination, true);
        return new CyStoreReconstructionResult(
            true,
            destination,
            manifest.Chunks.Count,
            reconstructed,
            $"Reconstructed and verified {entry.RelativePath} in CyStore's non-destructive restored folder.");
    }

    public ValueTask DisposeAsync()
    {
        _storeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<CaptureArtifacts> ChunkFileAsync(
        StorePaths paths,
        string relativePath,
        string fullPath,
        IProgress<CyStoreProgress>? progress,
        CancellationToken cancellationToken)
    {
        FileInfo info = new(fullPath);
        await using FileStream input = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            256 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using MemoryStream chunkBuffer = new(MaximumChunkBytes);
        byte[] readBuffer = new byte[256 * 1024];
        List<ChunkReference> chunks = [];
        long processed = 0;
        ulong rolling = 0;
        int newChunks = 0;
        int reusedChunks = 0;
        long writtenBytes = 0;
        long reusedBytes = 0;

        while (true)
        {
            int read = await input.ReadAsync(readBuffer, cancellationToken);
            if (read == 0) break;
            fileHash.AppendData(readBuffer, 0, read);

            for (int index = 0; index < read; index++)
            {
                byte value = readBuffer[index];
                chunkBuffer.WriteByte(value);
                rolling = (rolling << 1) + GearTable[value];
                int length = checked((int)chunkBuffer.Length);
                bool boundary = length >= MinimumChunkBytes &&
                                ((rolling & BoundaryMask) == 0 || length >= MaximumChunkBytes);
                if (!boundary) continue;

                ChunkWriteResult written = await FlushChunkAsync(paths, chunkBuffer, cancellationToken);
                chunks.Add(written.Reference);
                if (written.Created)
                {
                    newChunks++;
                    writtenBytes += written.Reference.Size;
                }
                else
                {
                    reusedChunks++;
                    reusedBytes += written.Reference.Size;
                }
                processed += written.Reference.Size;
                rolling = 0;
                progress?.Report(new CyStoreProgress("Capturing", relativePath, processed, info.Length));
            }
        }

        if (chunkBuffer.Length > 0 || info.Length == 0)
        {
            ChunkWriteResult written = await FlushChunkAsync(paths, chunkBuffer, cancellationToken);
            chunks.Add(written.Reference);
            if (written.Created)
            {
                newChunks++;
                writtenBytes += written.Reference.Size;
            }
            else
            {
                reusedChunks++;
                reusedBytes += written.Reference.Size;
            }
            processed += written.Reference.Size;
        }

        progress?.Report(new CyStoreProgress("Captured", relativePath, info.Length, info.Length));
        string fileSha = Convert.ToHexString(fileHash.GetHashAndReset()).ToLowerInvariant();
        return new CaptureArtifacts(
            new FileManifest(1, AlgorithmName, relativePath, fileSha, info.Length, chunks),
            newChunks,
            reusedChunks,
            writtenBytes,
            reusedBytes);
    }

    private static async Task<ChunkWriteResult> FlushChunkAsync(
        StorePaths paths,
        MemoryStream buffer,
        CancellationToken cancellationToken)
    {
        byte[] bytes = buffer.ToArray();
        buffer.SetLength(0);
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string path = ChunkPath(paths, sha);
        if (File.Exists(path))
        {
            return new ChunkWriteResult(new ChunkReference(sha, bytes.LongLength), false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        try
        {
            File.Move(temporary, path, false);
            return new ChunkWriteResult(new ChunkReference(sha, bytes.LongLength), true);
        }
        catch (IOException) when (File.Exists(path))
        {
            TryDelete(temporary);
            return new ChunkWriteResult(new ChunkReference(sha, bytes.LongLength), false);
        }
    }

    private static async Task<(VersionEntry Entry, FileManifest Manifest)> LoadVersionAsync(
        StorePaths paths,
        string versionId,
        CancellationToken cancellationToken)
    {
        StoreIndex index = await ReadIndexAsync(paths, cancellationToken);
        VersionEntry entry = index.Versions.FirstOrDefault(version =>
            string.Equals(version.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"CyStore version '{versionId}' was not found.");
        FileManifest manifest = await ReadJsonAsync<FileManifest>(
            ManifestPath(paths, entry.FileSha256),
            cancellationToken)
            ?? throw new InvalidDataException($"Manifest for CyStore version '{versionId}' is missing.");
        return (entry, manifest);
    }

    private static async Task<IReadOnlyList<string>> ListTrackedLfsFilesAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("lfs");
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--name-only");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start Git LFS.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git LFS file discovery failed: {error.Trim()}");

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StorePaths RequireInitialized(string projectRoot)
    {
        StorePaths paths = StorePaths.For(projectRoot);
        if (!File.Exists(paths.SettingsPath) || !File.Exists(paths.IndexPath))
            throw new InvalidOperationException("Initialize CyStore explicitly before capturing files.");
        return paths;
    }

    private static void EnsureGitRepository(string projectRoot)
    {
        if (!Directory.Exists(Path.Combine(projectRoot, ".git")) &&
            !File.Exists(Path.Combine(projectRoot, ".git")))
        {
            throw new InvalidOperationException("CyStore's Alpha mode requires an existing Git repository.");
        }
    }

    private static string NormalizeProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("A project root is required.", nameof(projectRoot));
        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root;
    }

    private static void EnsureInsideRoot(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The requested path is outside the project or CyStore root.");
    }

    private static bool IsLfsPointerFile(string path)
    {
        FileInfo info = new(path);
        if (info.Length > 1024) return false;
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[Math.Min(256, checked((int)info.Length))];
        int read = stream.Read(buffer, 0, buffer.Length);
        string prefix = Encoding.UTF8.GetString(buffer, 0, read);
        return prefix.StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal);
    }

    private static void EnsureLocalGitExclusion(string projectRoot)
    {
        string? gitDirectory = ResolveGitDirectory(projectRoot);
        if (gitDirectory is null) return;
        string infoDirectory = Path.Combine(gitDirectory, "info");
        Directory.CreateDirectory(infoDirectory);
        string excludePath = Path.Combine(infoDirectory, "exclude");
        string existing = File.Exists(excludePath) ? File.ReadAllText(excludePath) : "";
        if (existing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), ".cyrevision/", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : Environment.NewLine;
        File.AppendAllText(excludePath, $"{prefix}.cyrevision/{Environment.NewLine}");
    }

    private static string? ResolveGitDirectory(string projectRoot)
    {
        string dotGit = Path.Combine(projectRoot, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (!File.Exists(dotGit)) return null;
        string firstLine = File.ReadLines(dotGit).FirstOrDefault() ?? "";
        const string marker = "gitdir:";
        if (!firstLine.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return null;
        string configured = firstLine[marker.Length..].Trim();
        return Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(projectRoot, configured));
    }

    private static string ChunkPath(StorePaths paths, string sha) =>
        Path.Combine(paths.Chunks, sha[..2], $"{sha}.chunk");

    private static string ManifestPath(StorePaths paths, string sha) =>
        Path.Combine(paths.Manifests, sha[..2], $"{sha}.json");

    private static async Task<StoreIndex> ReadIndexAsync(
        StorePaths paths,
        CancellationToken cancellationToken) =>
        await ReadJsonAsync<StoreIndex>(paths.IndexPath, cancellationToken) ?? new StoreIndex();

    private static T? ReadJson<T>(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }

    private static async Task<T?> ReadJsonAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        await using (FileStream stream = new(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporary, path, true);
    }

    private static CyStoreVersion ToContract(VersionEntry entry) => new(
        entry.Id,
        entry.RelativePath,
        entry.FileSha256,
        entry.Size,
        entry.ChunkCount,
        entry.CapturedAt,
        entry.IsGitLfsTracked,
        entry.State);

    private static ulong[] BuildGearTable()
    {
        ulong[] table = new ulong[256];
        ulong seed = 0x4359524556495349UL;
        for (int index = 0; index < table.Length; index++)
        {
            seed += 0x9E3779B97F4A7C15UL;
            ulong value = seed;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            table[index] = value ^ (value >> 31);
        }
        return table;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup of an incomplete cache artifact.
        }
    }

    private sealed record StoreSettings(
        int SchemaVersion,
        string Algorithm,
        int MinimumChunkBytes,
        int AverageChunkBytes,
        int MaximumChunkBytes,
        DateTimeOffset CreatedAt);

    private sealed class StoreIndex
    {
        public int SchemaVersion { get; init; } = 1;
        public List<VersionEntry> Versions { get; init; } = [];
    }

    private sealed record VersionEntry(
        string Id,
        string RelativePath,
        string FileSha256,
        long Size,
        int ChunkCount,
        DateTimeOffset CapturedAt,
        bool IsGitLfsTracked,
        string State);

    private sealed record FileManifest(
        int SchemaVersion,
        string Algorithm,
        string RelativePath,
        string FileSha256,
        long Size,
        IReadOnlyList<ChunkReference> Chunks);

    private sealed record ChunkReference(string Sha256, long Size);

    private sealed record ChunkWriteResult(ChunkReference Reference, bool Created);

    private sealed record CaptureArtifacts(
        FileManifest Manifest,
        int NewChunks,
        int ReusedChunks,
        long WrittenBytes,
        long ReusedBytes);

    private sealed record StorePaths(
        string Root,
        string SettingsPath,
        string IndexPath,
        string Chunks,
        string Manifests,
        string Restored)
    {
        public static StorePaths For(string projectRoot)
        {
            string root = Path.Combine(Path.GetFullPath(projectRoot), ".cyrevision", "cystore");
            return new StorePaths(
                root,
                Path.Combine(root, "settings.json"),
                Path.Combine(root, "index.json"),
                Path.Combine(root, "chunks"),
                Path.Combine(root, "manifests"),
                Path.Combine(root, "restored"));
        }
    }
}
