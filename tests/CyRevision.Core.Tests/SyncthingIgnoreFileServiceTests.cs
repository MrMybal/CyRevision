using CyRevision.Sync;

namespace CyRevision.Core.Tests;

public sealed class SyncthingIgnoreFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyRevisionStignore", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritesAndReadsUtf8IgnoreRulesAtFolderRoot()
    {
        SyncthingIgnoreFileService service = new();
        string contents = "// test\r\n/Binaries\r\n/échanges";

        await service.WriteAsync(_root, contents);
        string loaded = await service.ReadAsync(_root);

        Assert.Equal(SyncthingIgnoreFileService.Normalize(contents), loaded);
        Assert.True(File.Exists(Path.Combine(_root, ".stignore")));
    }

    [Fact]
    public void FolderModesMapToStableApiValues()
    {
        Assert.Equal("sendreceive", SyncthingFolderMode.SendReceive.ToApiValue());
        Assert.Equal("sendonly", SyncthingFolderMode.SendOnly.ToApiValue());
        Assert.Equal("receiveonly", SyncthingFolderMode.ReceiveOnly.ToApiValue());
        Assert.Equal(SyncthingFolderMode.ReceiveOnly, SyncthingFolderModeExtensions.ParseApiValue("receiveonly"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
