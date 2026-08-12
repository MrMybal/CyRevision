namespace CyRevision.Git;

internal sealed record LfsStoragePaths(string GitCommonDirectory, string StorageDirectory)
{
    public string ObjectsDirectory => Path.Combine(StorageDirectory, "objects");
    public string GetObjectPath(string oid) => Path.Combine(ObjectsDirectory, oid[..2], oid.Substring(2, 2), oid);
}

internal static class LfsStoragePathResolver
{
    public static async Task<LfsStoragePaths> ResolveAsync(
        ProcessRunner runner,
        string gitExecutable,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        string repository = Path.GetFullPath(repositoryPath);
        ProcessResult gitDirectoryResult = await runner.RunAsync(
            gitExecutable, ["rev-parse", "--git-common-dir"], repository, cancellationToken);
        EnsureSuccess(gitDirectoryResult, "Unable to locate the Git common directory.");
        string gitDirectoryValue = gitDirectoryResult.StandardOutput.Trim();
        string gitDirectory = Path.GetFullPath(Path.IsPathRooted(gitDirectoryValue)
            ? gitDirectoryValue
            : Path.Combine(repository, gitDirectoryValue));

        ProcessResult storageResult = await runner.RunAsync(
            gitExecutable, ["config", "--local", "--get", "lfs.storage"], repository, cancellationToken);
        string storage = storageResult.Succeeded && !string.IsNullOrWhiteSpace(storageResult.StandardOutput)
            ? storageResult.StandardOutput.Trim()
            : "lfs";
        string storageDirectory = Path.GetFullPath(Path.IsPathRooted(storage)
            ? storage
            : Path.Combine(gitDirectory, storage));
        return new LfsStoragePaths(gitDirectory, storageDirectory);
    }

    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.Succeeded)
            return;
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Exit code {result.ExitCode}."
            : result.StandardError.Trim();
        throw new GitOperationException($"{message} {detail}");
    }
}
