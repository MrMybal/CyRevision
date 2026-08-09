namespace CyRevision.Core.Configuration;

public enum RetentionMode
{
    CurrentStateOnly,
    DeletedFiles,
    LimitedVersions,
    Timeline,
    Permanent
}

public sealed record RetentionPolicy(
    RetentionMode Mode,
    int? MaxVersionsPerFile = null,
    TimeSpan? MaximumAge = null,
    long? StorageBudgetBytes = null)
{
    public static RetentionPolicy CurrentStateOnly { get; } = new(RetentionMode.CurrentStateOnly);

    public static RetentionPolicy KeepForever { get; } = new(RetentionMode.Permanent);

    public void Validate()
    {
        if (MaxVersionsPerFile is <= 0)
        {
            throw new InvalidOperationException("The maximum version count must be greater than zero.");
        }

        if (MaximumAge is { } maximumAge && maximumAge <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The maximum age must be greater than zero.");
        }

        if (StorageBudgetBytes is <= 0)
        {
            throw new InvalidOperationException("The storage budget must be greater than zero.");
        }
    }
}

