namespace FactVaultManager.Desktop;

public sealed record QuizBuilderModePreset(
    string Name,
    int QuestionCount,
    int SecondsPerQuestion,
    bool Vertical,
    string? Difficulty)
{
    public override string ToString() => Name;
}

public static class QuizBuilderModePresets
{
    public static QuizBuilderModePreset Full { get; } =
        new("Full", 10, 8, Vertical: false, Difficulty: null);

    public static QuizBuilderModePreset Shorts { get; } =
        new("Shorts", 1, 3, Vertical: true, Difficulty: "easy");

    public static IReadOnlyList<QuizBuilderModePreset> All { get; } =
        [Full, Shorts];
}
