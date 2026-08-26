using System.Windows;

namespace FactVaultManager.Desktop;

public sealed record QuizCardLayoutProfile(
    string Key,
    string DisplayName,
    bool StatusBeforeTitle,
    Thickness StageMargin,
    double LogoRowHeight,
    double TitleRowHeight,
    double StatusRowHeight,
    double QuestionRowHeight,
    double TitleWidth,
    double TitleHeight,
    double QuestionWidth,
    double QuestionHeight,
    double AnswerWidth,
    HorizontalAlignment TitleAlignment);

public static class QuizCardLayoutCatalog
{
    private static readonly IReadOnlyList<QuizCardLayoutProfile> Layouts =
    [
        new(
            "classic",
            "Classic",
            StatusBeforeTitle: false,
            StageMargin: new Thickness(62, 16, 62, 24),
            LogoRowHeight: 150,
            TitleRowHeight: 96,
            StatusRowHeight: 86,
            QuestionRowHeight: 220,
            TitleWidth: 1110,
            TitleHeight: 82,
            QuestionWidth: 1240,
            QuestionHeight: 198,
            AnswerWidth: 1400,
            TitleAlignment: HorizontalAlignment.Center),
        new(
            "status-first",
            "Status First",
            StatusBeforeTitle: true,
            StageMargin: new Thickness(70, 18, 70, 24),
            LogoRowHeight: 140,
            TitleRowHeight: 90,
            StatusRowHeight: 92,
            QuestionRowHeight: 226,
            TitleWidth: 1040,
            TitleHeight: 76,
            QuestionWidth: 1320,
            QuestionHeight: 204,
            AnswerWidth: 1440,
            TitleAlignment: HorizontalAlignment.Center),
        new(
            "wide-focus",
            "Wide Focus",
            StatusBeforeTitle: false,
            StageMargin: new Thickness(82, 14, 82, 22),
            LogoRowHeight: 132,
            TitleRowHeight: 84,
            StatusRowHeight: 82,
            QuestionRowHeight: 244,
            TitleWidth: 920,
            TitleHeight: 72,
            QuestionWidth: 1500,
            QuestionHeight: 220,
            AnswerWidth: 1520,
            TitleAlignment: HorizontalAlignment.Left),
    ];

    public static IReadOnlyList<string> DisplayNames => Layouts.Select(layout => layout.DisplayName).ToArray();

    public static QuizCardLayoutProfile Resolve(string? value)
    {
        value = (value ?? "").Trim();
        return Layouts.FirstOrDefault(layout =>
                   string.Equals(layout.Key, value, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(layout.DisplayName, value, StringComparison.OrdinalIgnoreCase))
               ?? Layouts[0];
    }

    public static string Normalize(string? value) => Resolve(value).Key;
}

public sealed record QuizVisualVariation(string ThemeKey, string LayoutKey)
{
    public string DisplayName =>
        $"{QuizVisualThemeCatalog.Resolve(ThemeKey).DisplayName} • {QuizCardLayoutCatalog.Resolve(LayoutKey).DisplayName}";
}

public static class QuizVisualVariationPlanner
{
    public static IReadOnlyList<string> AutomaticThemeKeys { get; } =
        ["dark", "bright", "game-show"];

    public static IReadOnlyList<string> AutomaticLayoutKeys { get; } =
        ["classic", "status-first", "wide-focus"];

    public static bool Applies(bool vertical, string? quizType) =>
        !vertical && QuizTypeCatalog.Normalize(quizType) == QuizTypeCatalog.Standard;

    public static QuizVisualVariation Pick(
        string? currentThemeKey,
        string? currentLayoutKey,
        Random? random = null)
    {
        random ??= Random.Shared;
        var currentTheme = QuizVisualThemeCatalog.Normalize(currentThemeKey);
        var currentLayout = QuizCardLayoutCatalog.Normalize(currentLayoutKey);
        var options = AutomaticThemeKeys
            .SelectMany(theme => AutomaticLayoutKeys.Select(layout => new QuizVisualVariation(theme, layout)))
            .Where(option => !(
                string.Equals(option.ThemeKey, currentTheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(option.LayoutKey, currentLayout, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (options.Length == 0)
            return new QuizVisualVariation(currentTheme, currentLayout);
        return options[random.Next(options.Length)];
    }
}
