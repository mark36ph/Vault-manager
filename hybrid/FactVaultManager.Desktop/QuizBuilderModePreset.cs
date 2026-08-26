namespace FactVaultManager.Desktop;

public sealed record QuizBuilderModePreset(
    string Name,
    int QuestionCount,
    int SecondsPerQuestion,
    bool Vertical,
    string? Difficulty,
    bool IsMarathon = false)
{
    public override string ToString() => Name;
}

public static class QuizBuilderModePresets
{
    public static QuizBuilderModePreset Full { get; } =
        new("Full", 10, 8, Vertical: false, Difficulty: null);

    public static QuizBuilderModePreset Marathon30 { get; } =
        new("Marathon 30", 30, 8, Vertical: false, Difficulty: null, IsMarathon: true);

    public static QuizBuilderModePreset Marathon50 { get; } =
        new("Marathon 50", 50, 8, Vertical: false, Difficulty: null, IsMarathon: true);

    public static QuizBuilderModePreset Marathon100 { get; } =
        new("Marathon 100", 100, 8, Vertical: false, Difficulty: null, IsMarathon: true);

    public static QuizBuilderModePreset Shorts { get; } =
        new("Shorts", 1, 3, Vertical: true, Difficulty: "easy");

    public static IReadOnlyList<QuizBuilderModePreset> All { get; } =
        [Full, Marathon30, Marathon50, Marathon100, Shorts];
}
