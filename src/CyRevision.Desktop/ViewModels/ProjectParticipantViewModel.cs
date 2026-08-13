namespace CyRevision.Desktop.ViewModels;

public sealed record ProjectParticipantViewModel(
    string DisplayName,
    string Identity,
    string Role,
    string Status,
    string LastActivity,
    string Details,
    string StatusColor,
    bool IsOnline);
