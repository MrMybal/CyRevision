using CyRevision.Plugin.Abstractions;
using CyRevision.Plugin.CyStore;

namespace CyRevision.Core.Tests;

public sealed class CyStorePluginTests
{
    [Fact]
    public void AlphaModeIsOnlyAvailableForExistingGitRepositories()
    {
        string root = CreateTemporaryDirectory();
        string plainFolder = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            CyStorePlugin plugin = new();

            PluginProjectModeAvailability available = plugin.EvaluateProjectMode(
                CyStorePlugin.ModeId,
                new PluginProjectModeContext(Guid.NewGuid(), "Git project", root, [CyStorePlugin.PluginId]));
            PluginProjectModeAvailability unavailable = plugin.EvaluateProjectMode(
                CyStorePlugin.ModeId,
                new PluginProjectModeContext(Guid.NewGuid(), "Folder", plainFolder, [CyStorePlugin.PluginId]));

            Assert.True(available.IsAvailable);
            Assert.False(unavailable.IsAvailable);
            Assert.Contains("ALPHA", Assert.Single(plugin.ProjectModes).Name, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(plainFolder, true);
        }
    }

    [Fact]
    public async Task CapturedVersionsReuseChunksAndReconstructWithoutTouchingWorkingFile()
    {
        string root = CreateTemporaryDirectory();
        string relativePath = Path.Combine("Content", "LargeAsset.bin");
        string filePath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        byte[] original = new byte[16 * 1024 * 1024];
        new Random(0xC5_70_2E).NextBytes(original);
        await File.WriteAllBytesAsync(filePath, original);

        try
        {
            CyStorePlugin plugin = new();
            CyStoreStatus initialized = await plugin.InitializeStoreAsync(root);
            Assert.True(initialized.IsInitialized);
            Assert.Contains(".cyrevision/", await File.ReadAllTextAsync(Path.Combine(root, ".git", "info", "exclude")));

            CyStoreCaptureResult first = await plugin.CaptureFileAsync(root, relativePath, true);
            Assert.True(first.NewChunks >= 2);
            Assert.Equal(original.LongLength, first.Version.Size);

            byte[] changed = (byte[])original.Clone();
            Array.Fill(changed, (byte)0xA5, changed.Length - 4096, 4096);
            await File.WriteAllBytesAsync(filePath, changed);
            CyStoreCaptureResult second = await plugin.CaptureFileAsync(root, relativePath, true);

            Assert.True(second.NewChunks > 0);
            Assert.True(second.ReusedChunks > 0);
            Assert.True(second.ReusedBytes > 0);
            Assert.Equal(2, (await plugin.ListVersionsAsync(root)).Count);

            CyStoreVerificationResult verification = await plugin.VerifyVersionAsync(root, first.Version.Id);
            Assert.True(verification.Succeeded, verification.Summary);

            CyStoreReconstructionResult reconstruction = await plugin.ReconstructVersionAsync(root, first.Version.Id);
            Assert.True(reconstruction.Succeeded, reconstruction.Summary);
            Assert.NotEqual(Path.GetFullPath(filePath), Path.GetFullPath(reconstruction.DestinationPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(reconstruction.DestinationPath));
            Assert.Equal(changed, await File.ReadAllBytesAsync(filePath));

            CyStoreStatus status = plugin.InspectStore(root);
            Assert.Equal(2, status.VersionCount);
            Assert.True(status.StoredBytes < status.LogicalBytes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LfsPointerMustBeHydratedBeforeCapture()
    {
        string root = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        string pointer = Path.Combine(root, "Asset.uasset");
        await File.WriteAllTextAsync(
            pointer,
            "version https://git-lfs.github.com/spec/v1\noid sha256:0123456789abcdef\nsize 42\n");

        try
        {
            CyStorePlugin plugin = new();
            await plugin.InitializeStoreAsync(root);
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.CaptureFileAsync(root, pointer, true));
            Assert.Contains("Hydrate", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "cyrevision-cystore-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
