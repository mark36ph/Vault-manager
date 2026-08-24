namespace FactVaultManager.Desktop;

public sealed record QuizBuilderModePreset(
    string Name,
    int QuestionCount,
    int SecondsPerQuestion,
    bool Vertical)
{
    public override string ToString() => Name;
}

public static class QuizBuilderModePresets
{
    public static QuizBuilderModePreset Full { get; } =
        new("Full", 10, 8, Vertical: false);

    public static QuizBuilderModePreset Shorts { get; } =
        new("Shorts", 1, 3, Vertical: true);

    public static IReadOnlyList<QuizBuilderModePreset> All { get; } =
        [Full, Shorts];
}
