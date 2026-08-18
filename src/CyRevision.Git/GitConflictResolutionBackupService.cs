using System.IO.Compression;
using System.Text.Json;

namespace CyRevision.Git;

public sealed record GitConflictResolutionBackup(
    Guid Id,
    string ProjectName,
    string RepositoryPath,
    string FilePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string ResolutionSource,
    string ArchivePath);

public sealed class GitConflictResolutionBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;

    public GitConflictResolutionBackupService(string root) => _root = Path.GetFullPath(root);

    public async Task<GitConflictResolutionBackup> CreateAsync(
        string projectName,
        string repositoryPath,
        GitConflictFile conflict,
        string resultText,
        string resolutionSource,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        retentionDays = Math.Clamp(retentionDays, 1, 3650);
        Guid id = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        string projectKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(repositoryPath))))[..16].ToLowerInvariant();
        string directory = Path.Combine(_root, projectKey);
        Directory.CreateDirectory(directory);
        string archivePath = Path.Combine(directory, $"{createdAt:yyyyMMdd-HHmmss}-{id:N}.cyconflict.zip");
        string temporaryPath = archivePath + ".tmp";
        GitConflictResolutionBackup manifest = new(
            id, projectName, Path.GetFullPath(repositoryPath), conflict.Path, createdAt,
            createdAt.AddDays(retentionDays), resolutionSource, archivePath);
        try
        {
            await Task.Run(() =>
            {
                using (ZipArchive archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
                {
                    WriteText(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
                    WriteText(archive, "base.txt", conflict.Base.Text ?? conflict.Base.DisplayText);
                    WriteText(archive, "ours.txt", conflict.Ours.Text ?? conflict.Ours.DisplayText);
                    WriteText(archive, "incoming.txt", conflict.Theirs.Text ?? conflict.Theirs.DisplayText);
                    WriteText(archive, "working-before.txt", conflict.WorkingText ?? string.Empty);
                    WriteText(archive, "result.txt", resultText);
                }
                VerifyArchive(temporaryPath);
                File.Move(temporaryPath, archivePath);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
        await PruneExpiredAsync(cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    public Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (!Directory.Exists(_root)) return 0;
        int removed = 0;
        foreach (string archivePath in Directory.EnumerateFiles(_root, "*.cyconflict.zip", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                VerifyArchive(archivePath);
                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                ZipArchiveEntry? entry = archive.GetEntry("manifest.json");
                if (entry is null) continue;
                using Stream stream = entry.Open();
                GitConflictResolutionBackup? manifest = JsonSerializer.Deserialize<GitConflictResolutionBackup>(stream, JsonOptions);
                if (manifest?.ExpiresAt > DateTimeOffset.UtcNow) continue;
                File.Delete(archivePath);
                removed++;
            }
            catch (InvalidDataException)
            {
                // Never delete an archive that cannot be verified.
            }
        }
        return removed;
    }, cancellationToken);

    private static void WriteText(ZipArchive archive, string name, string value)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open());
        writer.Write(value);
    }

    private static void VerifyArchive(string archivePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        string[] required = ["manifest.json", "base.txt", "ours.txt", "incoming.txt", "working-before.txt", "result.txt"];
        if (required.Any(name => archive.GetEntry(name) is null))
            throw new InvalidDataException("The conflict recovery archive is incomplete.");
        using Stream stream = archive.GetEntry("manifest.json")!.Open();
        if (JsonSerializer.Deserialize<GitConflictResolutionBackup>(stream, JsonOptions) is null)
            throw new InvalidDataException("The conflict recovery manifest is invalid.");
    }
}
