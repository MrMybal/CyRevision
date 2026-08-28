using System.Text.Json;

namespace CyRevision.Desktop.SystemIntegration;

internal sealed class ApplicationPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ApplicationPreferencesStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _path = Path.Combine(configurationDirectory, "application-preferences.json");
    }

    public ApplicationPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return ApplicationPreferences.Default;
            return (JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(_path))
                    ?? ApplicationPreferences.Default).Normalize();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return ApplicationPreferences.Default;
        }
    }

    public void Save(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        string? directory = Path.GetDirectoryName(_path);
        if (directory is null) throw new InvalidOperationException("The application preference path has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences.Normalize(), JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static string ResolveCacheDirectory(ApplicationPreferences preferences, string defaultCacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(preferences.CacheDirectory)) return defaultCacheDirectory;
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(preferences.CacheDirectory.Trim());
            return Path.GetFullPath(expanded);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return defaultCacheDirectory;
        }
    }
}