namespace CyRevision.Core.Configuration;

public sealed record ProjectFeatures(
    bool GitEnabled,
    bool LfsEnabled,
    bool PeerSyncEnabled,
    bool BackupEnabled,
    bool StandardGitRemoteEnabled)
{
    public void Validate()
    {
        if (LfsEnabled && !GitEnabled)
        {
            throw new InvalidOperationException("Git LFS requires Git to be enabled.");
        }

        if (StandardGitRemoteEnabled && !GitEnabled)
        {
            throw new InvalidOperationException("A standard Git remote requires Git to be enabled.");
        }
    }
}

