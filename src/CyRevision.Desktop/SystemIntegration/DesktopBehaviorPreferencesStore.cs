using System.Text.Json;

namespace CyRevision.Desktop.SystemIntegration;

internal sealed class DesktopBehaviorPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public DesktopBehaviorPreferencesStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _path = Path.Combine(configurationDirectory, "desktop-behavior.json");
    }

    public DesktopBehaviorPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return DesktopBehaviorPreferences.Default;
            }

            return JsonSerializer.Deserialize<DesktopBehaviorPreferences>(File.ReadAllText(_path))
                   ?? DesktopBehaviorPreferences.Default;
        }
        catch (JsonException)
        {
            return DesktopBehaviorPreferences.Default;
        }
        catch (IOException)
        {
            return DesktopBehaviorPreferences.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return DesktopBehaviorPreferences.Default;
        }
    }

    public void Save(DesktopBehaviorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        string? directory = Path.GetDirectoryName(_path);
        if (directory is null)
        {
            throw new InvalidOperationException("The desktop preference path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
