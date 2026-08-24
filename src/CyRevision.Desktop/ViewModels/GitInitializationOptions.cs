namespace CyRevision.Desktop.ViewModels;

public sealed record GitInitializationOptions(
    bool CreateGitIgnore,
    string GitIgnoreContent,
    IReadOnlyList<string> LfsPatterns,
    string UserName,
    string UserEmail,
    string RemoteUrl,
    bool CreateInitialCommit,
    string InitialCommitMessage)
{
    public static GitInitializationOptions Default { get; } = new(
        false, string.Empty, [], string.Empty, string.Empty, string.Empty, false, "Initial commit");
}

public sealed record GitInitializationFilePreview(
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> SamplePaths,
    bool WasLimited);
