namespace FactVaultManager.Desktop;

public enum QuizCardRailSide
{
    None,
    Left,
    Right,
}

public sealed record QuizCardLayoutProfile(
    string Key,
    string DisplayName,
    double CardScale,
    QuizCardRailSide RailSide,
    double RailWidth,
    double EdgeInset);

public static class QuizCardLayoutCatalog
{
    private static readonly IReadOnlyList<QuizCardLayoutProfile> Layouts =
    [
        new(
            "classic-frame",
            "Classic Frame",
            CardScale: 1.0,
            RailSide: QuizCardRailSide.None,
            RailWidth: 0,
            EdgeInset: 18),
        new(
            "left-rail",
            "Left Rail",
            CardScale: 0.93,
            RailSide: QuizCardRailSide.Left,
            RailWidth: 94,
            EdgeInset: 24),
        new(
            "right-rail",
            "Right Rail",
            CardScale: 0.93,
            RailSide: QuizCardRailSide.Right,
            RailWidth: 94,
            EdgeInset: 24),
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
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static IReadOnlyList<string> AutomaticThemeKeys { get; } =
        ["dark", "bright", "game-show"];

    public static IReadOnlyList<string> AutomaticLayoutKeys { get; } =
        ["classic-frame", "left-rail", "right-rail"];

    public static bool Applies(bool vertical, string? quizType) =>
        !vertical && QuizTypeCatalog.Normalize(quizType) == QuizTypeCatalog.Standard;

    public static QuizVisualVariation ForQuestions(IReadOnlyList<QuizQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            return new QuizVisualVariation(AutomaticThemeKeys[0], AutomaticLayoutKeys[0]);

        var hash = FnvOffset;
        foreach (var question in questions)
        {
            Hash(ref hash, question.Id);
            Hash(ref hash, (int)question.DifficultyLevel);
            foreach (var character in question.Category ?? "")
                Hash(ref hash, character);
        }
        Hash(ref hash, questions.Count);

        var themeIndex = (int)(hash % (ulong)AutomaticThemeKeys.Count);
        var layoutIndex = (int)((hash / (ulong)AutomaticThemeKeys.Count) % (ulong)AutomaticLayoutKeys.Count);
        return new QuizVisualVariation(
            AutomaticThemeKeys[themeIndex],
            AutomaticLayoutKeys[layoutIndex]);
    }

    private static void Hash(ref ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= FnvPrime;
        }
    }
}
