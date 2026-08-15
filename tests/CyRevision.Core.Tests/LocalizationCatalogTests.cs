using System.Text.Json;
using CyRevision.Desktop.Localization;

namespace CyRevision.Core.Tests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void BundledCatalogs_AreValidAndDoNotContainEncodingArtifacts()
    {
        string catalogDirectory = Path.Combine(AppContext.BaseDirectory, "Localization", "Locales");
        string[] catalogs = Directory.GetFiles(catalogDirectory, "*.json", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(catalogs);
        foreach (string catalog in catalogs)
        {
            string content = File.ReadAllText(catalog);
            using JsonDocument _ = JsonDocument.Parse(content);
            Assert.DoesNotContain("Â·", content, StringComparison.Ordinal);
            Assert.DoesNotContain("â€¦", content, StringComparison.Ordinal);
            Assert.DoesNotContain("â€”", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FrenchCatalog_CoversGlobalActivityAndRuntimeStatus()
    {
        string settingsDirectory = Path.Combine(Path.GetTempPath(), "cyrevision-localization-tests", Guid.NewGuid().ToString("N"));
        try
        {
            LocalizationService localization = new();
            localization.Configure(settingsDirectory);
            localization.SetLanguage("fr");

            Assert.Equal("Détails", localization.Translate("Details"));
            Assert.Equal("Opérations récentes", localization.Translate("Recent operations"));
            Assert.Equal("Tâches 12", localization.Translate("Tasks 12"));
            Assert.Equal("3 alertes", localization.Translate("3 alerts"));
            Assert.Equal("Git distant", localization.Translate("Git remote"));
            Assert.Equal("Sauvegarde activée", localization.Translate("Backup enabled"));
            Assert.Equal("Rechercher par message, auteur ou hash…", localization.Translate("Search by message, author, or hash…"));
        }
        finally
        {
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, recursive: true);
            }
        }
    }
}
