using System.Text.Json;

namespace CyRevision.Desktop.Workspace;

public enum GitAnnotationTargetKind
{
    Commit,
    Branch
}

public sealed record GitAnnotation(
    Guid Id,
    Guid ProjectId,
    GitAnnotationTargetKind Kind,
    string Target,
    string Title,
    string Note,
    string Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string KindText => Kind.ToString();
    public string UpdatedText => UpdatedAt.ToLocalTime().ToString("g");
    public string SearchText => $"{Kind} {Target} {Title} {Note} {Tags}";
}

public sealed class GitAnnotationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GitAnnotationStore(string configurationDirectory)
    {
        Directory.CreateDirectory(configurationDirectory);
        _path = Path.Combine(configurationDirectory, "git-annotations.json");
    }

    public async Task<IReadOnlyList<GitAnnotation>> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ProjectId == projectId)
                .OrderByDescending(item => item.UpdatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GitAnnotation> SaveAsync(
        GitAnnotation annotation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<GitAnnotation> items = [.. await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false)];
            int index = items.FindIndex(item => item.Id == annotation.Id);
            if (index >= 0) items[index] = annotation;
            else items.Add(annotation);
            await WriteAllUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
            return annotation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid annotationId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<GitAnnotation> items = [.. await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false)];
            items.RemoveAll(item => item.Id == annotationId);
            await WriteAllUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<GitAnnotation>> ReadAllUnsafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_path)) return [];
            await using FileStream stream = new(
                _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<GitAnnotation[]>(stream, JsonOptions, cancellationToken)
                   .ConfigureAwait(false) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private async Task WriteAllUnsafeAsync(IReadOnlyCollection<GitAnnotation> items, CancellationToken cancellationToken)
    {
        string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
