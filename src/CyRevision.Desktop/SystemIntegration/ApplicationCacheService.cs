namespace CyRevision.Desktop.SystemIntegration;

internal sealed record ApplicationCacheUsage(long Bytes, int Files, int Skipped)
{
    public string Summary => $"{FormatBytes(Bytes)} in {Files:N0} file(s)" +
                             (Skipped > 0 ? $" · {Skipped:N0} inaccessible item(s)" : string.Empty);

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}

internal sealed record ApplicationCacheOperationResult(
    bool Succeeded,
    long Bytes,
    int Files,
    int Skipped,
    string Message);

internal static class ApplicationCacheService
{
    public static Task<ApplicationCacheUsage> MeasureAsync(string cacheDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(() => Measure(cacheDirectory, cancellationToken), cancellationToken);

    public static Task<ApplicationCacheOperationResult> PurgeAsync(
        string cacheDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Purge(cacheDirectory, cancellationToken), cancellationToken);

    public static ApplicationPreferences CompletePendingMove(
        ApplicationPreferences preferences,
        string defaultCacheDirectory,
        out ApplicationCacheOperationResult result)
    {
        ApplicationPreferences normalized = preferences.Normalize();
        if (string.IsNullOrWhiteSpace(normalized.PendingCacheMoveSource))
        {
            result = new ApplicationCacheOperationResult(true, 0, 0, 0, "No cache migration is pending.");
            return normalized;
        }

        try
        {
            string source = NormalizeSafeRoot(normalized.PendingCacheMoveSource);
            string destination = NormalizeSafeRoot(
                ApplicationPreferencesStore.ResolveCacheDirectory(normalized, defaultCacheDirectory));
            EnsureSeparateRoots(source, destination);

            if (!Directory.Exists(source))
            {
                result = new ApplicationCacheOperationResult(
                    true, 0, 0, 0, "The previous cache was already empty; the new location is active.");
                return normalized with { PendingCacheMoveSource = string.Empty };
            }

            List<string> files = EnumerateCacheFiles(source, CancellationToken.None, out int skipped);
            if (skipped > 0)
                throw new IOException($"The cache contains {skipped:N0} inaccessible or linked item(s); the source was left untouched.");

            long bytes = 0;
            foreach (string sourceFile in files)
            {
                string relative = Path.GetRelativePath(source, sourceFile);
                string destinationFile = Path.GetFullPath(Path.Combine(destination, relative));
                EnsureContained(destination, destinationFile);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.Copy(sourceFile, destinationFile, overwrite: true);
                FileInfo sourceInfo = new(sourceFile);
                FileInfo destinationInfo = new(destinationFile);
                if (!destinationInfo.Exists || destinationInfo.Length != sourceInfo.Length)
                    throw new IOException($"Cache verification failed for {relative}.");
                bytes += sourceInfo.Length;
            }

            foreach (string sourceFile in files)
                File.Delete(sourceFile);
            RemoveEmptySubdirectories(source);

            result = new ApplicationCacheOperationResult(
                true,
                bytes,
                files.Count,
                skipped,
                $"Moved {ApplicationCacheUsage.FormatBytes(bytes)} to the new cache folder.");
            return normalized with { PendingCacheMoveSource = string.Empty };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            result = new ApplicationCacheOperationResult(
                false, 0, 0, 0, $"Cache migration will be retried next launch: {exception.Message}");
            return normalized;
        }
    }

    public static string NormalizeSafeRoot(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(cacheDirectory.Trim())));
        string? driveRoot = Path.GetPathRoot(root);
        if (string.IsNullOrWhiteSpace(driveRoot) ||
            string.Equals(root, Path.TrimEndingDirectorySeparator(driveRoot), PathComparison()))
            throw new IOException("CyRevision refuses to manage a drive root as a cache.");

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile) &&
            string.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile)), PathComparison()))
            throw new IOException("CyRevision refuses to manage the user profile as a cache.");

        return root;
    }

    private static ApplicationCacheUsage Measure(string cacheDirectory, CancellationToken cancellationToken)
    {
        string root = NormalizeSafeRoot(cacheDirectory);
        if (!Directory.Exists(root)) return new ApplicationCacheUsage(0, 0, 0);

        List<string> files = EnumerateCacheFiles(root, cancellationToken, out int skipped);
        long bytes = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { bytes += new FileInfo(file).Length; }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return new ApplicationCacheUsage(bytes, files.Count, skipped);
    }

    private static ApplicationCacheOperationResult Purge(string cacheDirectory, CancellationToken cancellationToken)
    {
        string root = NormalizeSafeRoot(cacheDirectory);
        if (!Directory.Exists(root))
            return new ApplicationCacheOperationResult(true, 0, 0, 0, "The cache is already empty.");

        List<string> files = EnumerateCacheFiles(root, cancellationToken, out int skipped);
        long bytes = 0;
        int deleted = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long length = new FileInfo(file).Length;
                File.Delete(file);
                bytes += length;
                deleted++;
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        RemoveEmptySubdirectories(root);
        bool succeeded = skipped == 0;
        return new ApplicationCacheOperationResult(
            succeeded,
            bytes,
            deleted,
            skipped,
            $"{(succeeded ? "Purged" : "Partially purged")} {ApplicationCacheUsage.FormatBytes(bytes)} from {deleted:N0} file(s)." +
            (skipped > 0 ? $" {skipped:N0} item(s) could not be removed." : string.Empty));
    }

    private static List<string> EnumerateCacheFiles(
        string root,
        CancellationToken cancellationToken,
        out int skipped)
    {
        List<string> files = [];
        Stack<string> directories = new();
        directories.Push(root);
        skipped = 0;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = directories.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    FileAttributes attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }
                    EnsureContained(root, file);
                    files.Add(file);
                }

                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    FileAttributes attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }
                    EnsureContained(root, child);
                    directories.Push(child);
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return files;
    }

    private static void RemoveEmptySubdirectories(string root)
    {
        List<string> directories = [];
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    EnsureContained(root, child);
                    directories.Add(child);
                    pending.Push(child);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        foreach (string directory in directories.OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void ValidateMove(string source, string destination) =>
        EnsureSeparateRoots(NormalizeSafeRoot(source), NormalizeSafeRoot(destination));

    private static void EnsureSeparateRoots(string source, string destination)
    {
        if (string.Equals(source, destination, PathComparison()))
            throw new IOException("The source and destination cache folders are identical.");
        string sourcePrefix = source + Path.DirectorySeparatorChar;
        string destinationPrefix = destination + Path.DirectorySeparatorChar;
        if (destination.StartsWith(sourcePrefix, PathComparison()) ||
            source.StartsWith(destinationPrefix, PathComparison()))
            throw new IOException("A cache cannot be moved into itself or one of its parent folders.");
    }

    private static void EnsureContained(string root, string path)
    {
        string normalizedRoot = NormalizeSafeRoot(root);
        string normalizedPath = Path.GetFullPath(path);
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefix, PathComparison()))
            throw new IOException("CyRevision refused to access an item outside the selected cache folder.");
    }

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}