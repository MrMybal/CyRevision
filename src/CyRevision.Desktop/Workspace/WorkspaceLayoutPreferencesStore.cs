using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyRevision.Desktop.Workspace;

internal enum HistoryLayoutMode
{
    Columns,
    Review,
    DiffFocus
}

internal sealed record WorkspaceLayoutPreferences(
    HistoryLayoutMode HistoryLayout,
    bool ShowTimeline,
    bool ShowFiles,
    bool ShowDiff)
{
    public static WorkspaceLayoutPreferences Default { get; } =
        new(HistoryLayoutMode.Columns, true, true, true);
}

internal sealed class WorkspaceLayoutPreferencesStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorkspaceLayoutPreferencesStore(string configurationDirectory)
    {
        _path = Path.Combine(configurationDirectory, "workspace-layout.json");
    }

    public WorkspaceLayoutPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return WorkspaceLayoutPreferences.Default;
            }

            WorkspaceLayoutPreferences? preferences = JsonSerializer.Deserialize<WorkspaceLayoutPreferences>(
                File.ReadAllText(_path),
                _jsonOptions);
            if (preferences is null ||
                (!preferences.ShowTimeline && !preferences.ShowFiles && !preferences.ShowDiff))
            {
                return WorkspaceLayoutPreferences.Default;
            }

            return preferences;
        }
        catch (JsonException)
        {
            return WorkspaceLayoutPreferences.Default;
        }
        catch (IOException)
        {
            return WorkspaceLayoutPreferences.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return WorkspaceLayoutPreferences.Default;
        }
    }

    public void Save(WorkspaceLayoutPreferences preferences)
    {
        string temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, _jsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        catch (IOException)
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
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
}
