using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyRevision.Desktop.Localization;

public sealed class LocalizationService
{
    private const string DefaultLanguageCode = "en";
    private readonly Dictionary<string, TranslationCatalog> _catalogs =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _settingsDirectory;
    private string _currentLanguageCode = DefaultLanguageCode;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguageCode => _currentLanguageCode;

    public IReadOnlyList<LanguageOption> Languages { get; private set; } =
        [new(DefaultLanguageCode, "English"), new("fr", "Français")];

    public void Configure(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        _settingsDirectory = settingsDirectory;
        _catalogs.Clear();

        LoadCatalogDirectory(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), replace: false);
        LoadCatalogDirectory(Path.Combine(settingsDirectory, "locales"), replace: true);

        EnsureFallbackCatalogs();
        Languages = _catalogs.Values
            .OrderBy(catalog => catalog.Code.Equals(DefaultLanguageCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(catalog => catalog.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .Select(catalog => new LanguageOption(catalog.Code, catalog.NativeName))
            .ToArray();

        string savedLanguage = ReadSavedLanguage();
        _currentLanguageCode = _catalogs.ContainsKey(savedLanguage) ? savedLanguage : DefaultLanguageCode;
    }

    public void SetLanguage(string languageCode)
    {
        if (!_catalogs.ContainsKey(languageCode) ||
            string.Equals(_currentLanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentLanguageCode = languageCode;
        SaveLanguage(languageCode);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Translate(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !_catalogs.TryGetValue(_currentLanguageCode, out TranslationCatalog? catalog))
        {
            return source ?? string.Empty;
        }

        if (catalog.Translations.TryGetValue(source, out string? translated) && !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        foreach (PatternTranslation pattern in catalog.Patterns.Values)
        {
            try
            {
                if (pattern.Regex.IsMatch(source))
                {
                    return pattern.Regex.Replace(source, pattern.Replacement);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Ignore an unsafe optional translator pattern.
            }
        }

        return source;
    }

    public string Format(string source, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Translate(source), arguments);

    private void LoadCatalogDirectory(string directory, bool replace)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement;
                string code = Path.GetFileNameWithoutExtension(path).Trim().ToLowerInvariant();
                string nativeName = root.TryGetProperty("$name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? code
                    : code;
                Dictionary<string, string> translations = new(StringComparer.Ordinal);
                Dictionary<string, PatternTranslation> patterns = new(StringComparer.Ordinal);

                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name.StartsWith('$') || property.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    translations[property.Name] = property.Value.GetString() ?? property.Name;
                }

                if (root.TryGetProperty("$patterns", out JsonElement patternsElement) &&
                    patternsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in patternsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        try
                        {
                            patterns[property.Name] = new PatternTranslation(
                                new Regex(property.Name, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
                                property.Value.GetString() ?? string.Empty);
                        }
                        catch (ArgumentException)
                        {
                            // Ignore an invalid optional translator pattern.
                        }
                    }
                }

                if (_catalogs.TryGetValue(code, out TranslationCatalog? existing) && replace)
                {
                    foreach ((string key, string value) in translations)
                    {
                        existing.Translations[key] = value;
                    }

                    foreach ((string key, PatternTranslation value) in patterns)
                    {
                        existing.Patterns[key] = value;
                    }

                    _catalogs[code] = existing with { NativeName = nativeName };
                }
                else if (!_catalogs.ContainsKey(code) || replace)
                {
                    _catalogs[code] = new TranslationCatalog(code, nativeName, translations, patterns);
                }
            }
            catch (JsonException)
            {
                // A broken optional catalog must never prevent CyRevision from starting.
            }
            catch (IOException)
            {
                // A catalog may temporarily be locked while a translator edits it.
            }
        }
    }

    private void EnsureFallbackCatalogs()
    {
        if (!_catalogs.ContainsKey(DefaultLanguageCode))
        {
            _catalogs[DefaultLanguageCode] = new TranslationCatalog(
                DefaultLanguageCode,
                "English",
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, PatternTranslation>(StringComparer.Ordinal));
        }

        if (!_catalogs.ContainsKey("fr"))
        {
            _catalogs["fr"] = new TranslationCatalog(
                "fr",
                "Français",
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, PatternTranslation>(StringComparer.Ordinal));
        }
    }

    private string ReadSavedLanguage()
    {
        if (_settingsDirectory is null)
        {
            return DefaultLanguageCode;
        }

        string path = Path.Combine(_settingsDirectory, "ui-language.txt");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim().ToLowerInvariant() : DefaultLanguageCode;
        }
        catch (IOException)
        {
            return DefaultLanguageCode;
        }
    }

    private void SaveLanguage(string languageCode)
    {
        if (_settingsDirectory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(Path.Combine(_settingsDirectory, "ui-language.txt"), languageCode);
        }
        catch (IOException)
        {
            // The language still changes for this session if settings are read-only.
        }
        catch (UnauthorizedAccessException)
        {
            // The language still changes for this session if settings are read-only.
        }
    }

    private sealed record TranslationCatalog(
        string Code,
        string NativeName,
        Dictionary<string, string> Translations,
        Dictionary<string, PatternTranslation> Patterns);

    private sealed record PatternTranslation(Regex Regex, string Replacement);
}
