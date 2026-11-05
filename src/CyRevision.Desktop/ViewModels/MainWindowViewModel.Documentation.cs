namespace CyRevision.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool IsDocumentationAvailable => _documentationService.IsAvailable;
    public void SetNativeDocumentationLanguage(string code) => SelectedLanguage = Languages.FirstOrDefault(language => language.Code == code)
        ?? Languages.First(language => language.Code == "en");

    public string GetNativeDocumentationLink(string id = "getting-started", string? anchor = null, string? mediaId = null)
        => _documentationService.CreateLink(id, _localization.CurrentLanguageCode, anchor, mediaId);

    public object GetNativeDocumentation() => new
    {
        language = _localization.CurrentLanguageCode,
        available = IsDocumentationAvailable,
        total = _documentationService.PageCount,
        reader = "CyDoc",
        modules = new[] { "image" }
    };
}
