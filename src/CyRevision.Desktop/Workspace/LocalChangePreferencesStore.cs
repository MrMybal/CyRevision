using System.Text.Json;

namespace CyRevision.Desktop.Workspace;

public sealed record LocalChangePreferences(
    Guid ProjectId,
    IReadOnlyList<string> LocalOnlyPaths,
    DateTimeOffset UpdatedAt);

public sealed class LocalChangePreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public LocalChangePreferencesStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _directory = Path.Combine(Path.GetFullPath(configurationDirectory), "change-preparation");
    }

    public async Task<LocalChangePreferences> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(projectId);
        if (!File.Exists(path))
        {
            return new LocalChangePreferences(projectId, [], DateTimeOffset.UtcNow);
        }

        await using FileStream stream = File.OpenRead(path);
        LocalChangePreferences? preferences = await JsonSerializer.DeserializeAsync<LocalChangePreferences>(
            stream,
            JsonOptions,
            cancellationToken);
        return preferences is null || preferences.ProjectId != projectId
            ? new LocalChangePreferences(projectId, [], DateTimeOffset.UtcNow)
            : preferences with
            {
                LocalOnlyPaths = preferences.LocalOnlyPaths
                    .Select(NormalizePath)
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
    }

    public async Task SaveAsync(
        Guid projectId,
        IEnumerable<string> localOnlyPaths,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        LocalChangePreferences preferences = new(
            projectId,
            localOnlyPaths
                .Select(NormalizePath)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DateTimeOffset.UtcNow);

        string path = GetPath(projectId);
        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, path, true);
    }

    private string GetPath(Guid projectId) => Path.Combine(_directory, $"{projectId:N}.json");

    private static string NormalizePath(string path) => path.Trim().Replace('\\', '/').TrimStart('/');
}
