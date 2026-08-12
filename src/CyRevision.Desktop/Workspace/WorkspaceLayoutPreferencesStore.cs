using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyRevision.Desktop.Workspace;

internal enum HistoryLayoutMode
{
    Columns,
    Review,
    DiffFocus
}

internal enum ChangesLayoutMode
{
    Balanced,
    DiffFocus
}

internal enum CodeLayoutMode
{
    Balanced,
    EditorFocus,
    SearchFocus
}

internal sealed record WorkspaceLayoutPreferences(
    HistoryLayoutMode HistoryLayout,
    bool ShowTimeline,
    bool ShowFiles,
    bool ShowDiff,
    ChangesLayoutMode ChangesLayout,
    CodeLayoutMode CodeLayout,
    bool ShowCodeExplorer,
    bool ShowCodeSymbols,
    bool ShowCodeResults,
    double ChangesListWeight,
    double ChangesDiffWeight,
    double CodeExplorerWeight,
    double CodeEditorWeight,
    double CodeResultsWeight,
    double CodeEditorHeightWeight,
    double CodeSymbolsHeightWeight,
    double HistoryFirstWeight,
    double HistorySecondWeight,
    double HistoryThirdWeight,
    double HistoryTopWeight,
    double HistoryBottomWeight,
    int SchemaVersion = 2)
{
    public static WorkspaceLayoutPreferences Default { get; } =
        new(
            HistoryLayoutMode.Columns,
            true,
            true,
            true,
            ChangesLayoutMode.Balanced,
            CodeLayoutMode.Balanced,
            true,
            true,
            true,
            1.05,
            1.35,
            0.8,
            1.45,
            1.0,
            1.0,
            0.32,
            0.9,
            1.25,
            1.15,
            1.0,
            1.0);
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

            if (preferences.SchemaVersion < 2)
            {
                return WorkspaceLayoutPreferences.Default with
                {
                    HistoryLayout = preferences.HistoryLayout,
                    ShowTimeline = preferences.ShowTimeline,
                    ShowFiles = preferences.ShowFiles,
                    ShowDiff = preferences.ShowDiff
                };
            }

            return preferences with
            {
                ChangesListWeight = PositiveOrDefault(preferences.ChangesListWeight, 1.05),
                ChangesDiffWeight = PositiveOrDefault(preferences.ChangesDiffWeight, 1.35),
                CodeExplorerWeight = PositiveOrDefault(preferences.CodeExplorerWeight, 0.8),
                CodeEditorWeight = PositiveOrDefault(preferences.CodeEditorWeight, 1.45),
                CodeResultsWeight = PositiveOrDefault(preferences.CodeResultsWeight, 1.0),
                CodeEditorHeightWeight = PositiveOrDefault(preferences.CodeEditorHeightWeight, 1.0),
                CodeSymbolsHeightWeight = PositiveOrDefault(preferences.CodeSymbolsHeightWeight, 0.32),
                HistoryFirstWeight = PositiveOrDefault(preferences.HistoryFirstWeight, 0.9),
                HistorySecondWeight = PositiveOrDefault(preferences.HistorySecondWeight, 1.25),
                HistoryThirdWeight = PositiveOrDefault(preferences.HistoryThirdWeight, 1.15),
                HistoryTopWeight = PositiveOrDefault(preferences.HistoryTopWeight, 1.0),
                HistoryBottomWeight = PositiveOrDefault(preferences.HistoryBottomWeight, 1.0)
            };
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

    private static double PositiveOrDefault(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

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
