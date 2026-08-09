using System.Text.Json;

namespace CyRevision.Core.Projects;

public sealed class JsonProjectCatalog : IProjectCatalog, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _catalogPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonProjectCatalog(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        _catalogPath = Path.GetFullPath(catalogPath);
    }

    public async Task<IReadOnlyList<ProjectDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProjectDefinition?> FindByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProjectDefinition> projects = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return projects.FirstOrDefault(project => project.Id == projectId);
    }

    public async Task UpsertAsync(ProjectDefinition project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProjectDefinition> projects = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            int index = projects.FindIndex(existing => existing.Id == project.Id);
            if (index >= 0)
            {
                projects[index] = project;
            }
            else
            {
                projects.Add(project);
            }

            await WriteUnsafeAsync(projects, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProjectDefinition> projects = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (projects.RemoveAll(project => project.Id == projectId) > 0)
            {
                await WriteUnsafeAsync(projects, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private async Task<List<ProjectDefinition>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_catalogPath))
        {
            return [];
        }

        await using FileStream stream = new(
            _catalogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        try
        {
            return await JsonSerializer.DeserializeAsync<List<ProjectDefinition>>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   .ConfigureAwait(false) ?? [];
        }
        catch (JsonException exception)
        {
            throw new ProjectCatalogException($"The project catalog is invalid: {_catalogPath}", exception);
        }
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyCollection<ProjectDefinition> projects,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_catalogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _catalogPath + ".tmp";
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, projects, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _catalogPath, overwrite: true);
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

public sealed class ProjectCatalogException : Exception
{
    public ProjectCatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

