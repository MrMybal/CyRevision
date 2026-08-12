using System.Text.Json;
using CyRevision.RemoteBuild;

namespace CyRevision.Build.Agent;

public static class BuildAgentConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<(RemoteBuildAgentConfiguration Configuration, bool Created)> LoadOrCreateAsync(
        string path,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RemoteBuildAgentConfiguration empty = new(
                Path.Combine(dataDirectory, "jobs"), 1, 72, []);
            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(empty, JsonOptions), cancellationToken);
            return (empty, true);
        }

        RemoteBuildAgentConfiguration configuration = JsonSerializer.Deserialize<RemoteBuildAgentConfiguration>(
            await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("Build agent configuration is empty.");
        configuration.Validate();
        return (configuration with { JobsRoot = Path.GetFullPath(configuration.JobsRoot) }, false);
    }
}
