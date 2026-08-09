namespace CyRevision.Backup;

public sealed record BackupStoreOptions(
    string StorePath,
    IReadOnlySet<string>? ExcludedDirectoryNames = null)
{
    public IReadOnlySet<string> EffectiveExcludedDirectoryNames =>
        ExcludedDirectoryNames ?? DefaultExcludedDirectoryNames;

    public static IReadOnlySet<string> DefaultExcludedDirectoryNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cyrevision",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "Binaries",
            "DerivedDataCache",
            "Intermediate",
            "Saved"
        };
}
