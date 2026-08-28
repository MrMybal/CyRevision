using Avalonia;
using Avalonia.Media;

namespace CyRevision.Desktop.SystemIntegration;

internal sealed record InterfaceThemePreset(
    string Id,
    string Name,
    string Description,
    string Background,
    string Surface,
    string SurfaceAlt,
    string Card,
    string Border,
    string Foreground,
    string ForegroundStrong,
    string MutedForeground,
    string Accent,
    string AccentBright,
    string AccentStrong,
    string AccentMuted)
{
    public override string ToString() => Name;
}

internal static class InterfaceThemeService
{
    public const string DefaultPresetId = "cyrevision-dark";

    public static IReadOnlyList<InterfaceThemePreset> Presets { get; } =
    [
        new("cyrevision-dark", "CyRevision Dark", "The original charcoal interface with the CyRevision teal accent.",
            "#1E1F22", "#2B2D30", "#242529", "#26282B", "#393B40", "#DFE1E5", "#F2F3F5", "#9B9DA3",
            "#78D7B7", "#7DE2CC", "#3BAA96", "#203D50"),
        new("midnight-blue", "Midnight Blue", "A deep blue workspace with a clear cyan accent.",
            "#10151D", "#172131", "#121B28", "#1B2737", "#304158", "#DCE8F5", "#F4F9FF", "#91A4BA",
            "#65C7F2", "#8EDCFF", "#3A9FCC", "#173B54"),
        new("graphite", "Graphite", "A neutral low-saturation palette for long coding sessions.",
            "#202124", "#2B2C2F", "#252629", "#303134", "#47484D", "#DDDEE2", "#F3F3F4", "#A0A1A7",
            "#B7C2CC", "#D5DEE5", "#8796A3", "#3A4248"),
        new("high-contrast", "High Contrast", "Stronger separation, brighter text and a vivid accessible accent.",
            "#0B0D10", "#171A1F", "#111419", "#1D2127", "#69717C", "#F0F2F5", "#FFFFFF", "#C1C7D0",
            "#56E6C1", "#8CFFE2", "#22C99D", "#174D40")
    ];

    public static bool IsKnownPreset(string? id) =>
        Presets.Any(preset => string.Equals(preset.Id, id, StringComparison.Ordinal));

    public static InterfaceThemePreset GetPreset(string? id) =>
        Presets.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.Ordinal))
        ?? Presets[0];

    public static void Apply(string? presetId)
    {
        if (Application.Current is null) return;
        InterfaceThemePreset preset = GetPreset(presetId);
        Set("CyBackgroundBrush", preset.Background);
        Set("CySurfaceBrush", preset.Surface);
        Set("CySurfaceAltBrush", preset.SurfaceAlt);
        Set("CyCardBrush", preset.Card);
        Set("CyBorderBrush", preset.Border);
        Set("CyForegroundBrush", preset.Foreground);
        Set("CyForegroundStrongBrush", preset.ForegroundStrong);
        Set("CyMutedForegroundBrush", preset.MutedForeground);
        Set("CyAccentBrush", preset.Accent);
        Set("CyAccentBrightBrush", preset.AccentBright);
        Set("CyAccentStrongBrush", preset.AccentStrong);
        Set("CyAccentMutedBrush", preset.AccentMuted);
    }

    private static void Set(string key, string color) =>
        Application.Current!.Resources[key] = new SolidColorBrush(Color.Parse(color));
}