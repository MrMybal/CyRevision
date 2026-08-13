using System.Text.Json;

namespace CyRevision.Desktop.Workspace;

internal sealed record ProjectWorkspaceState(
    Guid ProjectId,
    string ActiveTab,
    string CodeRefreshFrequency,
    int ConsoleSection = 0);

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
        _states.TryGetValue(projectId, out ProjectWorkspaceState? state)
            ? state
            : new ProjectWorkspaceState(projectId, "ProjectWorkspaceTab", "Low · 5 min");

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
