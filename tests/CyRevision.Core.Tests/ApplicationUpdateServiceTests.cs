using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CyRevision.Core.Updates;

namespace CyRevision.Core.Tests;

public sealed class ApplicationUpdateServiceTests
{
    [Fact]
    public void ReleaseVersion_OrdersStableAndPrereleaseVersions()
    {
        ReleaseVersion current = ReleaseVersion.Parse("v0.1.3");
        ReleaseVersion newer = ReleaseVersion.Parse("0.2.0");
        ReleaseVersion preview = ReleaseVersion.Parse("0.2.0-beta.2");

        Assert.True(newer.CompareTo(current) > 0);
        Assert.True(preview.CompareTo(current) > 0);
        Assert.True(newer.CompareTo(preview) > 0);
        Assert.Equal("0.1.3", current.ToString());
    }

    [Theory]
    [InlineData(UpdatePlatform.Windows, Architecture.X64, "CyRevision-Setup-0.2.0-win-x64.exe")]
    [InlineData(UpdatePlatform.Linux, Architecture.X64, "CyRevision-0.2.0-linux-x64.deb")]
    [InlineData(UpdatePlatform.Linux, Architecture.Arm64, "CyRevision-0.2.0-linux-arm64.deb")]
    [InlineData(UpdatePlatform.MacOs, Architecture.Arm64, "CyRevision-0.2.0-osx-arm64.dmg")]
    public void SelectPackage_PrefersNativeInstaller(
        UpdatePlatform platform,
        Architecture architecture,
        string expectedName)
    {
        ReleaseAsset[] assets = CreateAssets(
            "CyRevision-Setup-0.2.0-win-x64.exe",
            "CyRevision-0.2.0-win-x64-portable.zip",
            "CyRevision-0.2.0-linux-x64.deb",
            "CyRevision-0.2.0-linux-x64-portable.tar.gz",
            "CyRevision-0.2.0-linux-arm64.deb",
            "CyRevision-0.2.0-osx-arm64.dmg",
            "CyRevision-0.2.0-osx-arm64-portable.zip",
            "SHA256SUMS.txt");

        ReleaseAsset? selected = ApplicationUpdateService.SelectPackage(assets, platform, architecture);

        Assert.NotNull(selected);
        Assert.Equal(expectedName, selected.Name);
    }

    [Fact]
    public async Task CheckAsync_UsesOnlyLatestPublishedReleaseResponse()
    {
        const string response = """
            {
              "tag_name": "v0.2.0",
              "draft": false,
              "prerelease": false,
              "html_url": "https://github.com/MrMybal/CyRevision/releases/tag/v0.2.0",
              "published_at": "2026-08-10T12:00:00Z",
              "body": "Release notes",
              "assets": [
                {
                  "name": "CyRevision-Setup-0.2.0-win-x64.exe",
                  "browser_download_url": "https://github.com/MrMybal/CyRevision/releases/download/v0.2.0/CyRevision-Setup-0.2.0-win-x64.exe",
                  "size": 1234
                },
                {
                  "name": "SHA256SUMS.txt",
                  "browser_download_url": "https://github.com/MrMybal/CyRevision/releases/download/v0.2.0/SHA256SUMS.txt",
                  "size": 200
                }
              ]
            }
            """;
        RecordingHandler handler = new(_ => JsonResponse(response));
        using HttpClient client = new(handler);
        using ApplicationUpdateService service = new(
            new Uri("https://github.com/MrMybal/CyRevision"),
            ReleaseVersion.Parse("0.1.3"),
            client);

        ApplicationUpdateInfo update = await service.CheckAsync();

        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("0.2.0", update.LatestVersion.ToString());
        Assert.EndsWith("/releases/latest", Assert.Single(handler.Requests).AbsoluteUri, StringComparison.Ordinal);
        Assert.NotNull(update.Checksums);
    }

    [Fact]
    public async Task CheckAsync_RejectsPrereleaseResponses()
    {
        const string response = """
            {
              "tag_name": "v0.2.0-beta.1",
              "draft": false,
              "prerelease": true,
              "html_url": "https://github.com/MrMybal/CyRevision/releases/tag/v0.2.0-beta.1",
              "assets": []
            }
            """;
        RecordingHandler handler = new(_ => JsonResponse(response));
        using HttpClient client = new(handler);
        using ApplicationUpdateService service = new(
            new Uri("https://github.com/MrMybal/CyRevision"),
            ReleaseVersion.Parse("0.1.3"),
            client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task DownloadAsync_VerifiesPublishedSha256()
    {
        byte[] packageBytes = Encoding.UTF8.GetBytes("signed release payload");
        string hash = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        const string packageName = "CyRevision-Setup-0.2.0-win-x64.exe";
        RecordingHandler handler = new(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{hash}  {packageName}\n")
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes)
            });
        using HttpClient client = new(handler);
        using ApplicationUpdateService service = new(
            new Uri("https://github.com/MrMybal/CyRevision"),
            ReleaseVersion.Parse("0.1.3"),
            client);
        ApplicationUpdateInfo update = new(
            ReleaseVersion.Parse("0.1.3"),
            ReleaseVersion.Parse("0.2.0"),
            new Uri("https://github.com/MrMybal/CyRevision/releases/tag/v0.2.0"),
            DateTimeOffset.UtcNow,
            string.Empty,
            new ReleaseAsset(packageName, new Uri($"https://github.com/download/{packageName}"), packageBytes.Length),
            new ReleaseAsset("SHA256SUMS.txt", new Uri("https://github.com/download/SHA256SUMS.txt"), 100));
        string root = Path.Combine(Path.GetTempPath(), "CyRevisionTests", Guid.NewGuid().ToString("N"));

        try
        {
            string path = await service.DownloadAsync(update, root);

            Assert.Equal(packageBytes, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".download"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ReleaseAsset[] CreateAssets(params string[] names) => names
        .Select(name => new ReleaseAsset(name, new Uri($"https://github.com/download/{name}"), 1))
        .ToArray();

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }
    }
}
