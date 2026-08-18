using System.Windows.Media;

namespace FactVaultManager.Desktop;

public sealed record QuizVisualTheme(
    string Key,
    string DisplayName,
    Color Background,
    Color Text,
    Color Accent,
    Color AccentSoft,
    Color Panel,
    Color PanelBorder,
    Color PanelText,
    Color Muted,
    Color Correct,
    Color CorrectBorder,
    Color Countdown,
    Color Narration);

public static class QuizVisualThemeCatalog
{
    private static readonly IReadOnlyList<QuizVisualTheme> Themes =
    [
        new(
            "dark",
            "Dark",
            Color.FromRgb(6, 10, 28),
            Color.FromRgb(248, 250, 255),
            Color.FromRgb(44, 164, 255),
            Color.FromRgb(255, 214, 61),
            Color.FromRgb(18, 25, 55),
            Color.FromRgb(63, 84, 164),
            Color.FromRgb(238, 243, 255),
            Color.FromRgb(162, 174, 211),
            Color.FromRgb(13, 73, 60),
            Color.FromRgb(52, 211, 153),
            Color.FromRgb(255, 215, 64),
            Color.FromRgb(173, 110, 255)),
        new(
            "clean",
            "Clean",
            Color.FromRgb(245, 248, 252),
            Color.FromRgb(17, 24, 39),
            Color.FromRgb(37, 99, 235),
            Color.FromRgb(191, 219, 254),
            Colors.White,
            Color.FromRgb(203, 213, 225),
            Color.FromRgb(17, 24, 39),
            Color.FromRgb(71, 85, 105),
            Color.FromRgb(220, 252, 231),
            Color.FromRgb(22, 163, 74),
            Color.FromRgb(202, 138, 4),
            Color.FromRgb(109, 40, 217)),
        new(
            "bright",
            "Bright",
            Color.FromRgb(12, 74, 110),
            Colors.White,
            Color.FromRgb(34, 211, 238),
            Color.FromRgb(165, 243, 252),
            Color.FromRgb(14, 116, 144),
            Color.FromRgb(103, 232, 249),
            Colors.White,
            Color.FromRgb(207, 250, 254),
            Color.FromRgb(21, 128, 61),
            Color.FromRgb(134, 239, 172),
            Color.FromRgb(253, 224, 71),
            Color.FromRgb(216, 180, 254)),
        new(
            "game-show",
            "Game Show",
            Color.FromRgb(46, 16, 101),
            Colors.White,
            Color.FromRgb(250, 204, 21),
            Color.FromRgb(254, 240, 138),
            Color.FromRgb(88, 28, 135),
            Color.FromRgb(192, 132, 252),
            Colors.White,
            Color.FromRgb(233, 213, 255),
            Color.FromRgb(21, 128, 61),
            Color.FromRgb(134, 239, 172),
            Color.FromRgb(251, 146, 60),
            Color.FromRgb(244, 114, 182)),
        new(
            "minimal",
            "Minimal",
            Color.FromRgb(250, 250, 249),
            Color.FromRgb(28, 25, 23),
            Color.FromRgb(68, 64, 60),
            Color.FromRgb(214, 211, 209),
            Color.FromRgb(245, 245, 244),
            Color.FromRgb(214, 211, 209),
            Color.FromRgb(28, 25, 23),
            Color.FromRgb(87, 83, 78),
            Color.FromRgb(220, 252, 231),
            Color.FromRgb(22, 101, 52),
            Color.FromRgb(161, 98, 7),
            Color.FromRgb(91, 33, 182)),
    ];

    public static IReadOnlyList<string> DisplayNames => Themes.Select(theme => theme.DisplayName).ToArray();

    public static QuizVisualTheme Resolve(string? value)
    {
        value = (value ?? "").Trim();
        return Themes.FirstOrDefault(theme =>
                   string.Equals(theme.Key, value, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(theme.DisplayName, value, StringComparison.OrdinalIgnoreCase))
               ?? Themes[0];
    }

    public static string Normalize(string? value) => Resolve(value).Key;
}

public static class QuizLogoPositionCatalog
{
    public static IReadOnlyList<string> Positions { get; } =
    [
        "Bottom right",
        "Bottom left",
        "Top right",
        "Top left",
    ];

    public static string Normalize(string? value)
    {
        value = (value ?? "").Trim();
        return Positions.FirstOrDefault(position => string.Equals(position, value, StringComparison.OrdinalIgnoreCase))
               ?? "Bottom right";
    }
}
