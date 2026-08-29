using System.Text.Json;

namespace CyRevision.Desktop.Workspace;

internal sealed record FileHistoryColumnPreference(
    string Key,
    int DisplayIndex,
    double Width,
    bool IsVisible);

internal sealed class FileHistoryColumnPreferencesStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<Guid, FileHistoryColumnPreference[]> _preferences;

    public FileHistoryColumnPreferencesStore(string configurationDirectory)
    {
        Directory.CreateDirectory(configurationDirectory);
        _path = Path.Combine(configurationDirectory, "file-history-columns.json");
        _preferences = LoadAll();
    }

    public IReadOnlyList<FileHistoryColumnPreference> Load(Guid projectId)
    {
        lock (_gate)
        {
            return _preferences.TryGetValue(projectId, out FileHistoryColumnPreference[]? preferences)
                ? preferences
                : [];
        }
    }

    public void Save(Guid projectId, IReadOnlyList<FileHistoryColumnPreference> preferences)
    {
        lock (_gate)
        {
            _preferences[projectId] = preferences.ToArray();
            string temporaryPath = _path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
                    _preferences.Select(item => new ProjectFileHistoryColumns(item.Key, item.Value))
                        .OrderBy(item => item.ProjectId),
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, _path, true);
            }
            catch (IOException)
            {
                TryDelete(temporaryPath);
            }
            catch (UnauthorizedAccessException)
            {
                TryDelete(temporaryPath);
            }
        }
    }

    private Dictionary<Guid, FileHistoryColumnPreference[]> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            ProjectFileHistoryColumns[] saved = JsonSerializer.Deserialize<ProjectFileHistoryColumns[]>(
                File.ReadAllText(_path)) ?? [];
            return saved
                .Where(item => item.ProjectId != Guid.Empty)
                .GroupBy(item => item.ProjectId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Columns ?? []);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ProjectFileHistoryColumns(
        Guid ProjectId,
        FileHistoryColumnPreference[]? Columns);
}