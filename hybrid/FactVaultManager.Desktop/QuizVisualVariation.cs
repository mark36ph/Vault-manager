namespace FactVaultManager.Desktop;

public enum QuizCardFrameStyle
{
    CleanFrame,
    CornerGlow,
    StageAccent,
}

public sealed record QuizCardLayoutProfile(
    string Key,
    string DisplayName,
    QuizCardFrameStyle FrameStyle,
    double EdgeInset);

public static class QuizCardLayoutCatalog
{
    private static readonly IReadOnlyList<QuizCardLayoutProfile> Layouts =
    [
        new(
            "clean-frame",
            "Clean Frame",
            QuizCardFrameStyle.CleanFrame,
            EdgeInset: 22),
        new(
            "corner-glow",
            "Corner Glow",
            QuizCardFrameStyle.CornerGlow,
            EdgeInset: 24),
        new(
            "stage-accent",
            "Stage Accent",
            QuizCardFrameStyle.StageAccent,
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

    private static readonly IReadOnlyList<QuizVisualVariation> ApprovedLooks =
    [
        new("dark", "clean-frame"),
        new("bright", "corner-glow"),
        new("game-show", "stage-accent"),
    ];

    public static IReadOnlyList<string> AutomaticThemeKeys { get; } =
        ApprovedLooks.Select(look => look.ThemeKey).ToArray();

    public static IReadOnlyList<string> AutomaticLayoutKeys { get; } =
        ApprovedLooks.Select(look => look.LayoutKey).ToArray();

    public static bool Applies(bool vertical, string? quizType) =>
        !vertical && QuizTypeCatalog.Normalize(quizType) == QuizTypeCatalog.Standard;

    public static QuizVisualVariation ForTheme(string? themeKey)
    {
        var normalizedTheme = QuizVisualThemeCatalog.Normalize(themeKey);
        return ApprovedLooks.FirstOrDefault(look =>
                   string.Equals(look.ThemeKey, normalizedTheme, StringComparison.OrdinalIgnoreCase))
               ?? ApprovedLooks[0];
    }

    public static QuizVisualVariation NextAfter(QuizVisualVariation? previous)
    {
        if (previous is null)
            return ApprovedLooks[0];

        var currentIndex = -1;
        for (var index = 0; index < ApprovedLooks.Count; index++)
        {
            if (!string.Equals(
                    ApprovedLooks[index].ThemeKey,
                    previous.ThemeKey,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            currentIndex = index;
            break;
        }

        return currentIndex < 0
            ? ApprovedLooks[0]
            : ApprovedLooks[(currentIndex + 1) % ApprovedLooks.Count];
    }

    public static QuizVisualVariation ForQuestions(IReadOnlyList<QuizQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            return ApprovedLooks[0];

        var hash = FnvOffset;
        foreach (var question in questions)
        {
            Hash(ref hash, question.Id);
            Hash(ref hash, (int)question.DifficultyLevel);
            foreach (var character in question.Category ?? "")
                Hash(ref hash, character);
        }
        Hash(ref hash, questions.Count);

        return ApprovedLooks[(int)(hash % (ulong)ApprovedLooks.Count)];
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
