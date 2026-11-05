using System.Text.Json;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Native;

namespace CyRevision.Core.Tests;

public sealed class OfflineDocumentationTests
{
    private static string Bundled => Path.Combine(AppContext.BaseDirectory, "Help");

    [Fact]
    public void PackagedCyDocHasGuidesReferencesAndOnlyImageModule()
    {
        var service = new OfflineDocumentationService(Bundled);
        Assert.True(service.IsAvailable);
        Assert.Equal(94, service.Load("en").Count);
        Assert.Equal(service.Load("en").Select(page => page.Id), service.Load("fr").Select(page => page.Id));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Bundled, "manifest.json")));
        Assert.Equal("en", manifest.RootElement.GetProperty("defaultLocale").GetString());
        Assert.Equal(150, manifest.RootElement.GetProperty("pages").GetArrayLength());
        Assert.True(File.Exists(Path.Combine(Bundled, "media-image.js")));
        foreach (string module in new[] { "mesh", "video", "gallery", "graph", "gif" })
            Assert.False(File.Exists(Path.Combine(Bundled, "media-" + module + ".js")));
        Assert.True(File.Exists(Path.Combine(Bundled, "CyDoc-LICENSE.txt")));
        foreach (var workspace in NativeWorkspaceCatalog.All)
            Assert.StartsWith("file:///", service.CreateLink(workspace.Id, "en"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr-FR")]
    [InlineData("FR_ca")]
    [InlineData("unknown")]
    public void RoutesCarryLocaleSharedAnchorsAndPreciseMedia(string language)
    {
        var service = new OfflineDocumentationService(Bundled);
        string locale = language.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
        string url = service.CreateLink("doc:git-history#file-history-including-lfs", language, mediaId: "history-v2");
        Assert.EndsWith("#/" + locale + "/git-history/file-history-including-lfs?media=history-v2", url);
        Assert.True(new Uri(url).IsFile);
        Assert.EndsWith("#/" + locale + "/getting-started", service.CreateLink("quickstart", language));
    }

    [Theory]
    [InlineData("../file")]
    [InlineData("https://example.invalid")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("missing-page")]
    public void UnknownAndExternalTargetsCannotBeOpened(string target)
        => Assert.Throws<ArgumentException>(() => new OfflineDocumentationService(Bundled).CreateLink(target, "en"));

    [Fact]
    public void MissingAnchorAndMediaAreRejected_AndOriginalFrenchReferenceIsPreserved()
    {
        var service = new OfflineDocumentationService(Bundled);
        Assert.Throws<ArgumentException>(() => service.CreateLink("git-history", "en", "missing-section"));
        Assert.Throws<ArgumentException>(() => service.CreateLink("git-history", "en", mediaId: "missing-media"));
        Assert.EndsWith("#/fr/reference-server-api", service.CreateLink("reference-server-api", "en"));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(Bundled, "translation-report.json")));
        Assert.Equal(26, report.RootElement.GetProperty("missing").GetArrayLength());
        Assert.Equal(3, report.RootElement.GetProperty("untracked").GetArrayLength());
        Assert.Equal(0, report.RootElement.GetProperty("outdated").GetArrayLength());
    }

    [Fact]
    public void FileUrlsEncodeSpacesUnicodeAndHashInInstallationPaths()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "CyRevisionGuideTests", "Guide # é " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(Path.Combine(Bundled, "manifest.json"), Path.Combine(temporary, "manifest.json"));
            File.Copy(Path.Combine(Bundled, "index.html"), Path.Combine(temporary, "index.html"));
            var service = new OfflineDocumentationService(temporary);
            string url = service.CreateLink("getting-started", "en");
            Assert.Contains("%23", url); Assert.Contains("%20", url);
            Assert.Equal(Path.Combine(temporary, "index.html"), new Uri(url).LocalPath);
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), "malformed");
            var missing = new OfflineDocumentationService(temporary);
            Assert.False(missing.IsAvailable);
            Assert.NotEmpty(missing.Load("en"));
            Assert.Throws<InvalidOperationException>(() => missing.CreateLink("getting-started", "en"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
