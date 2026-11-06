using System.Text.Json;
using CyRevision.Desktop.Documentation;
using CyRevision.Desktop.Native;

namespace CyRevision.Core.Tests;

public sealed class OfflineDocumentationTests
{
    private static string Bundled => Path.Combine(AppContext.BaseDirectory, "Documentation");
    private static string DataFile(string file) => Path.Combine(Bundled, "DocData", file);

    [Fact]
    public void PackagedReaderIsBilingualAndHasOnlyRequiredModules()
    {
        var service = new OfflineDocumentationService(Bundled);
        Assert.True(service.IsAvailable);
        Assert.Equal(86, service.PageCount);
        Assert.Equal(new[] { "image", "gallery" }, service.Modules);
        Assert.EndsWith("Documentation.html", service.IndexFile);
        Assert.Equal(service.Load("en").Select(page => page.Id), service.Load("fr").Select(page => page.Id));
        Assert.All(service.Load("en"), page => Assert.Equal("en", page.Language));
        Assert.All(service.Load("fr"), page => Assert.Equal("fr", page.Language));
        using var manifest = JsonDocument.Parse(File.ReadAllText(DataFile("manifest.json")));
        Assert.Equal("en", manifest.RootElement.GetProperty("defaultLocale").GetString());
        Assert.Equal(172, manifest.RootElement.GetProperty("pages").GetArrayLength());
        Assert.True(File.Exists(DataFile("media-image.js")));
        Assert.True(File.Exists(DataFile("media-gallery.js")));
        foreach (string module in new[] { "mesh", "video", "graph", "gif" })
            Assert.False(File.Exists(DataFile("media-" + module + ".js")));
        Assert.True(File.Exists(DataFile("CyDoc-LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(Bundled, "DocAI.md")));
        foreach (var workspace in NativeWorkspaceCatalog.All)
            Assert.Contains("/Documentation/Documentation.html#/", service.CreateLink(workspace.Id, "en"));
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
        Assert.EndsWith("#/" + locale + "/getting-started?media=interface-tour",
            service.CreateLink("getting-started", language, mediaId: "interface-tour"));
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
    public void LegacyReferencesResolveToCompleteTranslations_AndMissingSectionsAreRejected()
    {
        var service = new OfflineDocumentationService(Bundled);
        Assert.Throws<ArgumentException>(() => service.CreateLink("git-history", "en", "missing-section"));
        Assert.Throws<ArgumentException>(() => service.CreateLink("git-history", "en", mediaId: "missing-media"));
        Assert.EndsWith("#/en/server-api", service.CreateLink("reference-server-api", "en"));
        Assert.EndsWith("#/fr/server-api", service.CreateLink("reference-server-api", "fr"));
        Assert.EndsWith("#/en/plugin-api", service.CreateLink("technical-references", "en"));
        using var report = JsonDocument.Parse(File.ReadAllText(DataFile("translation-report.json")));
        foreach (string key in new[] { "missing", "untracked", "outdated" })
            Assert.Equal(0, report.RootElement.GetProperty(key).GetArrayLength());
    }

    [Fact]
    public void HumanManualContainsNoReportsAiGuidesOrSourceListings()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(DataFile("manifest.json")));
        foreach (var page in manifest.RootElement.GetProperty("pages").EnumerateArray())
        {
            string id = page.GetProperty("id").GetString()!;
            Assert.DoesNotContain(id, new[] { "technical-references", "translation-report", "reference-offline-guide" });
            Assert.False(id.StartsWith("source-", StringComparison.Ordinal));
            Assert.False(page.TryGetProperty("kind", out var kind) && kind.GetString() == "source");
            if (page.TryGetProperty("code", out var code))
            {
                Assert.DoesNotContain("class GameEngineIntegrationPluginBase", code.GetString());
                Assert.DoesNotContain("SaveConnectionsCoreAsync", code.GetString());
                if (id.StartsWith("public-api-", StringComparison.Ordinal))
                    Assert.DoesNotContain("{", code.GetString());
            }
        }
        Assert.False(Directory.Exists(Path.Combine(Bundled, ".CyDocBuild")));
        Assert.False(Directory.Exists(Path.Combine(Bundled, "DocData", "source-api")));
    }

    [Fact]
    public void FileUrlsEncodeSpacesUnicodeAndHashInInstallationPaths()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "CyRevisionGuideTests", "Documentation # é " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "DocData"));
        try
        {
            File.Copy(DataFile("manifest.json"), Path.Combine(temporary, "DocData", "manifest.json"));
            File.Copy(Path.Combine(Bundled, "Documentation.html"), Path.Combine(temporary, "Documentation.html"));
            var service = new OfflineDocumentationService(temporary);
            string url = service.CreateLink("getting-started", "en");
            Assert.Contains("%23", url); Assert.Contains("%20", url);
            Assert.Equal(Path.Combine(temporary, "Documentation.html"), new Uri(url).LocalPath);
            File.WriteAllText(Path.Combine(temporary, "DocData", "manifest.json"), "malformed");
            var missing = new OfflineDocumentationService(temporary);
            Assert.False(missing.IsAvailable);
            Assert.NotEmpty(missing.Load("en"));
            Assert.Throws<InvalidOperationException>(() => missing.CreateLink("getting-started", "en"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
