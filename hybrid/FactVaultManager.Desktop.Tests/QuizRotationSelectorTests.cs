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
