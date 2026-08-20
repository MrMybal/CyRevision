using CyRevision.Plugin.Abstractions;

namespace CyRevision.Desktop.Plugins;

/// <summary>
/// Host-owned, project-contained file broker for cooperative plugins and external app
/// adapters. It rejects rooted paths, traversal, disallowed roots and existing reparse
/// points which resolve outside the project. Writes are atomic.
/// </summary>
public sealed class ProjectPluginFileSandbox : IPluginProjectFileSandbox
{
    private readonly string _projectRoot;
    private readonly string[] _allowedRoots;

    public ProjectPluginFileSandbox(PluginProjectSandboxPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(policy.PluginId))
            throw new ArgumentException("A plugin identifier is required.", nameof(policy));
        _projectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(policy.ProjectRoot));
        if (!Directory.Exists(_projectRoot)) throw new DirectoryNotFoundException(_projectRoot);
        if (policy.MaximumReadBytes <= 0 || policy.MaximumWriteBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "File limits must be positive.");

        Policy = policy with { ProjectRoot = _projectRoot };
        _allowedRoots = (policy.AllowedRelativeRoots.Count == 0 ? [string.Empty] : policy.AllowedRelativeRoots)
            .Select(root => NormalizeRelativePath(root, allowEmpty: true))
            .Distinct(PathComparer)
            .ToArray();
        foreach (string root in _allowedRoots) ResolveContainedPath(root, requireExisting: false);
    }

    public PluginProjectSandboxPolicy Policy { get; }

    public Task<IReadOnlyList<string>> EnumerateFilesAsync(
        string relativeDirectory = "",
        string searchPattern = "*",
        int maximumResults = 10_000,
        CancellationToken cancellationToken = default)
    {
        Demand(PluginProjectPermission.EnumerateProjectFiles);
        if (maximumResults is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        if (string.IsNullOrWhiteSpace(searchPattern) || searchPattern.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("The search pattern must be a file-name pattern.", nameof(searchPattern));

        string directory = ResolveContainedPath(relativeDirectory, requireExisting: true);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        IReadOnlyList<string> files = Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories)
            .Take(maximumResults)
            .Select(path => Path.GetRelativePath(_projectRoot, path))
            .Where(IsAllowedRelativePath)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(files);
    }

    public async Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        Demand(PluginProjectPermission.ReadProjectFiles);
        string path = ResolveContainedPath(relativePath, requireExisting: true);
        FileInfo file = new(path);
        if (!file.Exists) throw new FileNotFoundException("The project file does not exist.", path);
        if (file.Length > Policy.MaximumReadBytes)
            throw new IOException($"The file exceeds the plugin read limit of {Policy.MaximumReadBytes:N0} bytes.");
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAllBytesAsync(
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        Demand(PluginProjectPermission.WriteProjectFiles);
        if (content.Length > Policy.MaximumWriteBytes)
            throw new IOException($"The content exceeds the plugin write limit of {Policy.MaximumWriteBytes:N0} bytes.");
        string path = ResolveContainedPath(relativePath, requireExisting: false);
        string parent = Path.GetDirectoryName(path) ?? _projectRoot;
        EnsureExistingPathDoesNotEscape(parent);
        Directory.CreateDirectory(parent);
        string temporary = path + ".cyrevision-plugin-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void Demand(PluginProjectPermission permission)
    {
        if (!Policy.Permissions.HasFlag(permission))
            throw new UnauthorizedAccessException($"Plugin '{Policy.PluginId}' does not have {permission} permission for this project.");
    }

    private string ResolveContainedPath(string relativePath, bool requireExisting)
    {
        string normalized = NormalizeRelativePath(relativePath, allowEmpty: true);
        if (!IsAllowedRelativePath(normalized))
            throw new UnauthorizedAccessException("The requested path is outside the roots granted to this plugin.");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(_projectRoot, normalized)));
        if (!IsInsideOrEqual(full, _projectRoot))
            throw new UnauthorizedAccessException("The requested path escapes the selected project.");
        if (requireExisting && !File.Exists(full) && !Directory.Exists(full))
            throw new FileNotFoundException("The requested project path does not exist.", full);
        EnsureExistingPathDoesNotEscape(full);
        return full;
    }

    private void EnsureExistingPathDoesNotEscape(string path)
    {
        string? current = path;
        while (!string.IsNullOrWhiteSpace(current) && IsInsideOrEqual(current, _projectRoot))
        {
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null)
            {
                FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !IsInsideOrEqual(target.FullName, _projectRoot))
                    throw new UnauthorizedAccessException("A symbolic link escapes the selected project.");
            }
            if (PathComparer.Equals(Path.TrimEndingDirectorySeparator(current), _projectRoot)) break;
            current = Path.GetDirectoryName(current);
        }
    }

    private bool IsAllowedRelativePath(string relativePath) => _allowedRoots.Any(root =>
        string.IsNullOrEmpty(root) ||
        PathComparer.Equals(relativePath, root) ||
        relativePath.StartsWith(root + Path.DirectorySeparatorChar, PathComparison));

    private static string NormalizeRelativePath(string path, bool allowEmpty)
    {
        string value = (path ?? string.Empty).Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(value) && allowEmpty) return string.Empty;
        if (Path.IsPathRooted(value)) throw new UnauthorizedAccessException("Rooted plugin paths are not allowed.");
        string[] parts = value.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new UnauthorizedAccessException("Relative traversal is not allowed in plugin paths.");
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private static bool IsInsideOrEqual(string candidate, string root)
    {
        string fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return PathComparer.Equals(fullCandidate, fullRoot) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
