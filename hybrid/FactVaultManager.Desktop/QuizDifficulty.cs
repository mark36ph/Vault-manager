namespace FactVaultManager.Desktop;

public enum QuizDifficulty
{
    Easy = 1,
    Medium = 2,
    Hard = 3,
    Insane = 4,
}

public static class QuizDifficultyCatalog
{
    public static IReadOnlyList<string> StorageValues { get; } =
        ["easy", "medium", "hard", "insane"];

    public static QuizDifficulty Parse(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "easy" or "beginner" => QuizDifficulty.Easy,
            "hard" or "difficult" or "expert" => QuizDifficulty.Hard,
            "insane" or "extreme" or "impossible" => QuizDifficulty.Insane,
            _ => QuizDifficulty.Medium,
        };
    }

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return "medium";
        var level = normalized switch
        {
            "easy" or "beginner" => QuizDifficulty.Easy,
            "medium" or "normal" or "intermediate" => QuizDifficulty.Medium,
            "hard" or "difficult" or "expert" => QuizDifficulty.Hard,
            "insane" or "extreme" or "impossible" => QuizDifficulty.Insane,
            _ => throw new InvalidDataException("Difficulty must be easy, medium, hard, or insane."),
        };
        return StorageName(level);
    }

    public static string StorageName(QuizDifficulty difficulty) =>
        difficulty.ToString().ToLowerInvariant();

    public static string RoundBanner(string? difficulty)
    {
        var level = Parse(difficulty);
        return $"ROUND {(int)level}: {level.ToString().ToUpperInvariant()}";
    }
}

public static class QuizDifficultyProgressionSelector
{
    public const string FullDescription = "3 Easy → 3 Medium → 3 Hard → 1 Insane";

    private static readonly IReadOnlyList<(QuizDifficulty Difficulty, int Count)> FullTargets =
    [
        (QuizDifficulty.Easy, 3),
        (QuizDifficulty.Medium, 3),
        (QuizDifficulty.Hard, 3),
        (QuizDifficulty.Insane, 1),
    ];

    public static bool Applies(int count, string? difficultyFilter) =>
        string.IsNullOrWhiteSpace(difficultyFilter) && count is 1 or 10 or 30 or 50 or 100;

    public static IReadOnlyList<(QuizDifficulty Difficulty, int Count)> TargetsFor(int count) => count switch
    {
        1 => [(QuizDifficulty.Easy, 1)],
        10 => FullTargets,
        30 => MarathonTargets(30),
        50 => MarathonTargets(50),
        100 => MarathonTargets(100),
        _ => throw new ArgumentOutOfRangeException(nameof(count),
            "Difficulty progression supports 1, 10, 30, 50, or 100 questions."),
    };

    public static string DescriptionFor(int count)
    {
        if (count == 10)
            return FullDescription;
        var targets = TargetsFor(count);
        return string.Join(" → ", targets.Select(target =>
            $"{target.Count} {target.Difficulty}"));
    }

    public static IReadOnlyList<QuizQuestion> Select(
        IEnumerable<QuizQuestion> questions,
        int count,
        bool preferLeastUsed = false,
        IReadOnlySet<int>? recentlyUsedQuestionIds = null,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var targets = TargetsFor(count);

        var pool = questions
            .Where(question => question.IsEnabled)
            .GroupBy(question => question.Id)
            .Select(group => group.First())
            .ToList();
        if (count > pool.Count)
            throw new InvalidOperationException($"Only {pool.Count} enabled matching quiz questions are available, but {count} were requested.");

        random ??= Random.Shared;
        recentlyUsedQuestionIds ??= new HashSet<int>();
        var selected = new List<QuizQuestion>(count);

        foreach (var target in targets)
        {
            var candidates = pool
                .Where(question => !selected.Any(item => item.Id == question.Id) &&
                                   question.DifficultyLevel == target.Difficulty)
                .ToList();
            var take = Math.Min(target.Count, candidates.Count);
            if (take > 0)
            {
                selected.AddRange(QuizRotationSelector.Select(
                    candidates, take, preferLeastUsed, recentlyUsedQuestionIds, random));
            }
        }

        if (selected.Count < count)
        {
            var selectedIds = selected.Select(question => question.Id).ToHashSet();
            var remaining = pool.Where(question => !selectedIds.Contains(question.Id)).ToList();
            selected.AddRange(QuizRotationSelector.Select(
                remaining, count - selected.Count, preferLeastUsed, recentlyUsedQuestionIds, random));
        }

        return selected
            .Select((question, index) => new { Question = question, Index = index })
            .OrderBy(item => item.Question.DifficultyLevel)
            .ThenBy(item => item.Index)
            .Select(item => item.Question)
            .ToList();
    }

    private static IReadOnlyList<(QuizDifficulty Difficulty, int Count)> MarathonTargets(int count)
    {
        var standard = (int)Math.Round(count * 0.30, MidpointRounding.AwayFromZero);
        var insane = count - (standard * 3);
        return
        [
            (QuizDifficulty.Easy, standard),
            (QuizDifficulty.Medium, standard),
            (QuizDifficulty.Hard, standard),
            (QuizDifficulty.Insane, insane),
        ];
    }
}
