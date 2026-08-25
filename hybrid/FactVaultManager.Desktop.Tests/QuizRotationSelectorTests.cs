namespace FactVaultManager.Desktop.Tests;

public sealed class QuizRotationSelectorTests
{
    [Fact]
    public void Select_PrefersLeastUsedQuestions()
    {
        var questions = new[]
        {
            Question(1, 8),
            Question(2, 0),
            Question(3, 3),
            Question(4, 1),
        };

        var selected = QuizRotationSelector.Select(
            questions,
            2,
            preferLeastUsed: true,
            random: new Random(123));

        Assert.Equal(new[] { 2, 4 }, selected.Select(question => question.Id).OrderBy(id => id));
    }

    [Fact]
    public void Select_AvoidsRecentlyUsedQuestionsWhenFreshPoolIsLargeEnough()
    {
        var questions = Enumerable.Range(1, 6)
            .Select(id => Question(id, 0))
            .ToList();
        var recent = new HashSet<int> { 1, 2, 3 };

        var selected = QuizRotationSelector.Select(
            questions,
            3,
            preferLeastUsed: false,
            recentlyUsedQuestionIds: recent,
            random: new Random(123));

        Assert.DoesNotContain(selected, question => recent.Contains(question.Id));
        Assert.Equal(new[] { 4, 5, 6 }, selected.Select(question => question.Id).OrderBy(id => id));
    }

    [Fact]
    public void Select_ReusesRecentQuestionsOnlyWhenNeededToFillDraft()
    {
        var questions = Enumerable.Range(1, 5)
            .Select(id => Question(id, 0))
            .ToList();
        var recent = new HashSet<int> { 1, 2, 3, 4 };

        var selected = QuizRotationSelector.Select(
            questions,
            3,
            preferLeastUsed: false,
            recentlyUsedQuestionIds: recent,
            random: new Random(123));

        Assert.Contains(selected, question => question.Id == 5);
        Assert.Equal(2, QuizRotationSelector.CountRecentFallbacks(selected, recent));
    }

    [Fact]
    public void Select_StillExcludesDisabledQuestions()
    {
        var questions = new[]
        {
            Question(1, 0) with { IsEnabled = false },
            Question(2, 4),
            Question(3, 0),
        };

        var selected = QuizRotationSelector.Select(
            questions,
            2,
            preferLeastUsed: true,
            random: new Random(123));

        Assert.Equal(new[] { 2, 3 }, selected.Select(question => question.Id).OrderBy(id => id));
    }

    [Fact]
    public void DifficultyProgression_SelectsThreeThreeThreeOneInAscendingRounds()
    {
        var questions = new List<QuizQuestion>();
        var id = 1;
        foreach (var difficulty in QuizDifficultyCatalog.StorageValues)
        {
            for (var index = 0; index < 5; index++)
                questions.Add(Question(id++, 0) with { Difficulty = difficulty });
        }

        var selected = QuizDifficultyProgressionSelector.Select(questions, 10, random: new Random(123));

        Assert.Equal(10, selected.Count);
        Assert.Equal(3, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Easy));
        Assert.Equal(3, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Medium));
        Assert.Equal(3, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Hard));
        Assert.Equal(1, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Insane));
        Assert.Equal(selected.OrderBy(question => question.DifficultyLevel).Select(question => question.Id),
            selected.Select(question => question.Id));
    }

    [Fact]
    public void DifficultyProgression_ShortSelectsEasyQuestion()
    {
        var questions = new[]
        {
            Question(1, 0) with { Difficulty = "hard" },
            Question(2, 0) with { Difficulty = "easy" },
        };

        var selected = QuizDifficultyProgressionSelector.Select(questions, 1, random: new Random(123));

        Assert.Single(selected);
        Assert.Equal(QuizDifficulty.Easy, selected[0].DifficultyLevel);
    }

    [Fact]
    public void DifficultyProgression_FillsMissingInsaneSlotWithoutBlockingDraft()
    {
        var questions = Enumerable.Range(1, 12)
            .Select(id => Question(id, 0) with { Difficulty = id <= 4 ? "easy" : id <= 8 ? "medium" : "hard" })
            .ToList();

        var selected = QuizDifficultyProgressionSelector.Select(questions, 10, random: new Random(123));

        Assert.Equal(10, selected.Count);
        Assert.Empty(selected.Where(question => question.DifficultyLevel == QuizDifficulty.Insane));
        Assert.Equal(selected.OrderBy(question => question.DifficultyLevel).Select(question => question.Id),
            selected.Select(question => question.Id));
    }

    private static QuizQuestion Question(int id, int timesUsed) => new(
        id,
        $"Question {id}?",
        "A", "B", "C", "D",
        0,
        "Explanation",
        "General Knowledge",
        "medium",
        "Test",
        timesUsed);
}
