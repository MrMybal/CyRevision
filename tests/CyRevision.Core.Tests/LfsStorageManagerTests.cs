using System.Diagnostics;
using System.Security.Cryptography;
using CyRevision.Git;

namespace CyRevision.Core.Tests;

public sealed class LfsStorageManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionLfsManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OrphanedObjectRequiresArchiveProofWhileLocalCommitStaysProtected()
    {
        if (!await ToolWorksAsync("git", "--version") || !await ToolWorksAsync("git", "lfs", "version"))
            return;
        string repository = Path.Combine(_root, "repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.name", "CyRevision Test");
        await GitAsync(repository, "config", "user.email", "test@cyrevision.local");
        await GitAsync(repository, "lfs", "install", "--local");
        await GitAsync(repository, "lfs", "track", "*.bin");
        await GitAsync(repository, "add", ".gitattributes");
        await GitAsync(repository, "commit", "-m", "Track LFS");

        byte[] protectedBytes = "protected-current-lfs"u8.ToArray();
        string asset = Path.Combine(repository, "asset.bin");
        await File.WriteAllBytesAsync(asset, protectedBytes);
        await GitAsync(repository, "add", "asset.bin");
        await GitAsync(repository, "commit", "-m", "Current asset");
        string protectedOid = Convert.ToHexString(SHA256.HashData(protectedBytes)).ToLowerInvariant();

        await GitAsync(repository, "switch", "-c", "temporary-pr");
        byte[] orphanBytes = "orphaned-after-pr-delete"u8.ToArray();
        await File.WriteAllBytesAsync(asset, orphanBytes);
        await GitAsync(repository, "add", "asset.bin");
        await GitAsync(repository, "commit", "-m", "Temporary PR asset");
        string orphanOid = Convert.ToHexString(SHA256.HashData(orphanBytes)).ToLowerInvariant();
        await GitAsync(repository, "switch", "main");
        await GitAsync(repository, "branch", "-D", "temporary-pr");

        Guid projectId = Guid.NewGuid();
        string archive = Path.Combine(_root, "archive");
        LfsManagementProfile profile = LfsManagementProfile.CreateDefault(projectId) with
        {
            VerifyRemote = false,
            ArchivePath = archive,
            GracePeriodDays = 0
        };
        LfsStorageManager manager = new();
        LfsCleanupPlan first = await manager.AnalyzeAsync(repository, profile);
        LfsCleanupItem protectedItem = Assert.Single(first.Objects.Where(item => item.OidSha256 == protectedOid));
        Assert.True(protectedItem.IsReferencedLocally);
        Assert.False(protectedItem.CanDelete);
        LfsCleanupItem orphan = Assert.Single(first.Objects.Where(item => item.OidSha256 == orphanOid));
        Assert.False(orphan.IsReferencedLocally);
        Assert.False(orphan.CanDelete);

        LfsArchiveResult archived = await manager.ArchiveUnreferencedAsync(first, archive);
        Assert.Equal(1, archived.ArchivedObjects);
        LfsCleanupPlan second = await manager.AnalyzeAsync(repository, profile);
        LfsCleanupItem eligible = Assert.Single(second.Objects.Where(item => item.OidSha256 == orphanOid));
        Assert.True(eligible.CanDelete);
        Assert.Contains(eligible.Evidence, evidence => evidence.Kind == LfsRetentionEvidenceKind.Archive);

        LfsCleanupResult result = await manager.ExecuteAsync(second);
        Assert.Equal(1, result.DeletedObjects);
        Assert.False(File.Exists(eligible.LocalPath));
        Assert.True(File.Exists(protectedItem.LocalPath));
        Assert.True(File.Exists(Path.Combine(archive, "objects", orphanOid[..2], orphanOid.Substring(2, 2), orphanOid)));
        Assert.True(File.Exists(result.AuditPath));
    }

    [Fact]
    public async Task RelocationActivatesDedicatedStorageAndKeepsVerifiedCopy()
    {
        if (!await ToolWorksAsync("git", "--version") || !await ToolWorksAsync("git", "lfs", "version"))
            return;
        string repository = Path.Combine(_root, "relocate-repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.name", "CyRevision Test");
        await GitAsync(repository, "config", "user.email", "test@cyrevision.local");
        await GitAsync(repository, "lfs", "install", "--local");
        await GitAsync(repository, "lfs", "track", "*.uasset");
        byte[] content = "relocated-lfs-object"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(repository, "Hero.uasset"), content);
        await GitAsync(repository, "add", ".gitattributes", "Hero.uasset");
        await GitAsync(repository, "commit", "-m", "Add asset");
        string destination = Path.Combine(_root, "dedicated-lfs");

        LfsRelocationResult result = await new LfsStorageManager().RelocateAsync(
            repository, destination, removeOriginalObjectsAfterVerification: false);
        Assert.Equal(Path.GetFullPath(destination), result.ActiveStoragePath);
        Assert.Equal(1, result.CopiedObjects);
        Assert.True(File.Exists(Path.Combine(destination, ".cyrevision-lfs-owner.json")));
        string configured = (await GitAsync(repository, "config", "--local", "--get", "lfs.storage")).Trim();
        Assert.Equal(Path.GetFullPath(destination), configured);
        LfsTrackedFile tracked = Assert.Single(await new GitCliRepositoryService().GetLfsTrackedFilesAsync(repository));
        Assert.StartsWith(Path.GetFullPath(destination), tracked.LocalObjectPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteTrackingRefDoesNotForceVerifiedObjectToStayInHotStorage()
    {
        if (!await ToolWorksAsync("git", "--version") || !await ToolWorksAsync("git", "lfs", "version"))
            return;
        string repository = Path.Combine(_root, "remote-only-repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.name", "CyRevision Test");
        await GitAsync(repository, "config", "user.email", "test@cyrevision.local");
        await GitAsync(repository, "lfs", "install", "--local");
        await GitAsync(repository, "lfs", "track", "*.bin");
        await GitAsync(repository, "add", ".gitattributes");
        await GitAsync(repository, "commit", "-m", "Track LFS");
        await GitAsync(repository, "switch", "-c", "old-remote-branch");
        byte[] content = "remote-only-lfs-object"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(repository, "old.bin"), content);
        await GitAsync(repository, "add", "old.bin");
        await GitAsync(repository, "commit", "-m", "Old remote asset");
        string tip = (await GitAsync(repository, "rev-parse", "HEAD")).Trim();
        string oid = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await GitAsync(repository, "update-ref", "refs/remotes/origin/old-remote-branch", tip);
        await GitAsync(repository, "switch", "main");
        await GitAsync(repository, "branch", "-D", "old-remote-branch");

        LfsManagementProfile profile = LfsManagementProfile.CreateDefault(Guid.NewGuid()) with
        {
            VerifyRemote = false,
            GracePeriodDays = 0
        };
        LfsCleanupPlan plan = await new LfsStorageManager().AnalyzeAsync(repository, profile);
        LfsCleanupItem remoteOnly = Assert.Single(plan.Objects.Where(item => item.OidSha256 == oid));

        Assert.False(remoteOnly.IsReferencedLocally);
        Assert.False(remoteOnly.CanDelete);
        Assert.Contains("verified copy", remoteOnly.Decision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteBackedHistoryPolicyKeepsRecentUassetAndArchivesOlderVersion()
    {
        if (!await ToolWorksAsync("git", "--version") || !await ToolWorksAsync("git", "lfs", "version"))
            return;
        string repository = Path.Combine(_root, "uasset-history-repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.name", "CyRevision Test");
        await GitAsync(repository, "config", "user.email", "test@cyrevision.local");
        await GitAsync(repository, "lfs", "install", "--local");
        await GitAsync(repository, "lfs", "track", "*.uasset");
        string asset = Path.Combine(repository, "Content", "Hero.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        byte[] oldBytes = "hero-uasset-version-one"u8.ToArray();
        byte[] currentBytes = "hero-uasset-version-two"u8.ToArray();
        await File.WriteAllBytesAsync(asset, oldBytes);
        await GitAsync(repository, "add", ".gitattributes", "Content/Hero.uasset");
        await GitAsync(repository, "commit", "-m", "Old Hero asset");
        await File.WriteAllBytesAsync(asset, currentBytes);
        await GitAsync(repository, "add", "Content/Hero.uasset");
        await GitAsync(repository, "commit", "-m", "Current Hero asset");
        await GitAsync(repository, "switch", "-c", "kept-art-branch");
        byte[] branchBytes = "branch-tip-uasset-must-stay-hot"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(repository, "Content", "BranchOnly.uasset"), branchBytes);
        await GitAsync(repository, "add", "Content/BranchOnly.uasset");
        await GitAsync(repository, "commit", "-m", "Branch-only asset");
        await GitAsync(repository, "switch", "main");

        string oldOid = Convert.ToHexString(SHA256.HashData(oldBytes)).ToLowerInvariant();
        string currentOid = Convert.ToHexString(SHA256.HashData(currentBytes)).ToLowerInvariant();
        string branchOid = Convert.ToHexString(SHA256.HashData(branchBytes)).ToLowerInvariant();
        string objects = Path.Combine(repository, ".git", "lfs", "objects");
        File.SetLastWriteTimeUtc(Path.Combine(objects, oldOid[..2], oldOid.Substring(2, 2), oldOid), DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(Path.Combine(objects, currentOid[..2], currentOid.Substring(2, 2), currentOid), DateTime.UtcNow);
        string archive = Path.Combine(_root, "uasset-archive");
        LfsManagementProfile profile = LfsManagementProfile.CreateDefault(Guid.NewGuid()) with
        {
            VerifyRemote = false,
            ArchivePath = archive,
            GracePeriodDays = 0,
            TrimRemoteBackedHistory = true,
            RecentVersionsPerFile = 1,
            RecentVersionExtensions = ".uasset"
        };
        LfsStorageManager manager = new();

        LfsCleanupPlan first = await manager.AnalyzeAsync(repository, profile);
        LfsCleanupItem current = Assert.Single(first.Objects.Where(item => item.OidSha256 == currentOid));
        LfsCleanupItem old = Assert.Single(first.Objects.Where(item => item.OidSha256 == oldOid));
        LfsCleanupItem keptBranch = Assert.Single(first.Objects.Where(item => item.OidSha256 == branchOid));
        Assert.True(current.IsReferencedLocally);
        Assert.True(current.IsKeptAsRecentVersion);
        Assert.True(keptBranch.IsReferencedLocally);
        Assert.False(keptBranch.CanDelete);
        Assert.Equal("Content/Hero.uasset", old.DisplayPath);
        Assert.False(old.IsReferencedLocally);
        Assert.False(old.CanDelete);

        LfsArchiveResult archived = await manager.ArchiveUnreferencedAsync(first, archive);
        Assert.Equal(1, archived.ArchivedObjects);
        LfsCleanupPlan verified = await manager.AnalyzeAsync(repository, profile);
        LfsCleanupItem eligible = Assert.Single(verified.Objects.Where(item => item.OidSha256 == oldOid));
        Assert.True(eligible.CanDelete);
        Assert.Contains(eligible.Evidence, evidence => evidence.Kind == LfsRetentionEvidenceKind.Archive);
    }

    [Fact]
    public async Task RepositoryStorageReportSeparatesWorkingTreeGitAndLfsCache()
    {
        if (!await ToolWorksAsync("git", "--version") || !await ToolWorksAsync("git", "lfs", "version"))
            return;
        string repository = Path.Combine(_root, "storage-report-repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.name", "CyRevision Test");
        await GitAsync(repository, "config", "user.email", "test@cyrevision.local");
        await GitAsync(repository, "lfs", "install", "--local");
        await GitAsync(repository, "lfs", "track", "*.uasset");
        byte[] content = new byte[128 * 1024];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(Path.Combine(repository, "Large.uasset"), content);
        await GitAsync(repository, "add", ".gitattributes", "Large.uasset");
        await GitAsync(repository, "commit", "-m", "Large asset");

        RepositoryStorageReport report = await new LfsStorageManager().AnalyzeRepositoryStorageAsync(repository);

        Assert.Contains(report.Areas, area => area.Name == "Working tree" && area.Size >= content.Length);
        Assert.Contains(report.Areas, area => area.Name == "Git LFS cache" && area.Size >= content.Length);
        Assert.Contains(report.LargestFiles, file => file.RelativePath == "Large.uasset" && file.Size == content.Length);
    }

    private static async Task<bool> ToolWorksAsync(string executable, params string[] arguments)
    {
        try
        {
            ProcessStartInfo start = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    public void Dispose()
    {
        DeleteTestTree(_root);
    }

    private static void DeleteTestTree(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
