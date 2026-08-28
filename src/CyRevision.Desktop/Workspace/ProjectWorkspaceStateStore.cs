using System.Text.Json;

namespace CyRevision.Desktop.Workspace;

internal sealed record ProjectWorkspaceState(
    Guid ProjectId,
    string ActiveTab,
    string CodeRefreshFrequency,
    int ConsoleSection = 0,
    Dictionary<string, string>? CategoryTabs = null,
    string TabVisibilityPreset = "Git essentials",
    List<string>? HiddenTabs = null,
    List<string>? HiddenChangeColumns = null,
    string ChangeSort = "Name",
    Dictionary<string, double[]>? DataGridColumnWidths = null,
    Dictionary<string, string[]>? HiddenDataGridColumns = null);

internal sealed class ProjectWorkspaceStateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<Guid, ProjectWorkspaceState> _states;

    public ProjectWorkspaceStateStore(string configurationDirectory)
    {
        Directory.CreateDirectory(configurationDirectory);
        _path = Path.Combine(configurationDirectory, "project-workspaces.json");
        _states = Load();
    }

    public ProjectWorkspaceState Get(Guid projectId) =>
        TryGet(projectId, out ProjectWorkspaceState state)
            ? state
            : CreateDefault(projectId);

    public bool TryGet(Guid projectId, out ProjectWorkspaceState state)
    {
        if (_states.TryGetValue(projectId, out ProjectWorkspaceState? saved))
        {
            state = saved;
            return true;
        }

        state = CreateDefault(projectId);
        return false;
    }

    private static ProjectWorkspaceState CreateDefault(Guid projectId) =>
        new(projectId, "ProjectWorkspaceTab", "Low · 5 min");

    public void Save(ProjectWorkspaceState state)
    {
        lock (_gate)
        {
            _states[state.ProjectId] = state;
            string temporary = _path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(
                    _states.Values.OrderBy(item => item.ProjectId),
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, _path, true);
            }
            catch (IOException) { TryDelete(temporary); }
            catch (UnauthorizedAccessException) { TryDelete(temporary); }
        }
    }

    private Dictionary<Guid, ProjectWorkspaceState> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            ProjectWorkspaceState[] states = JsonSerializer.Deserialize<ProjectWorkspaceState[]>(File.ReadAllText(_path)) ?? [];
            return states.GroupBy(state => state.ProjectId).ToDictionary(group => group.Key, group => group.Last());
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
