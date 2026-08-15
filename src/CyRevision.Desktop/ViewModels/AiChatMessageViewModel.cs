namespace CyRevision.Desktop.ViewModels;

public sealed class AiChatMessageViewModel : ObservableObject
{
    private string _text;

    public AiChatMessageViewModel(string role, string text, DateTimeOffset? timestamp = null)
    {
        Role = role;
        _text = text;
        Timestamp = timestamp ?? DateTimeOffset.Now;
    }

    public string Role { get; }

    public string RoleLabel => Role.ToLowerInvariant() switch
    {
        "user" => "You",
        "system" => "CyRevision",
        _ => "Codex"
    };

    public string AccentColor => Role.ToLowerInvariant() switch
    {
        "user" => "#62B0F5",
        "system" => "#F2C66D",
        _ => "#65D6B5"
    };

    public string BackgroundColor => Role.ToLowerInvariant() switch
    {
        "user" => "#18283A",
        "system" => "#2B281B",
        _ => "#172B27"
    };

    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm");

    public DateTimeOffset Timestamp { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public void Append(string delta)
    {
        if (delta.Length == 0) return;
        Text += delta;
    }
}
