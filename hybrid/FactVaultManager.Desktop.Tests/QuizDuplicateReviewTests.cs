namespace FactVaultManager.Desktop.Tests;

public sealed class QuizDuplicateReviewTests
{
    [Fact]
    public void FindCandidates_FlagsSameAndRewordedDuplicates()
    {
        var questions = new[]
        {
            Question(1, "What is the capital of France?", "Paris"),
            Question(2, "Which city is the capital of France?", "Paris"),
            Question(3, "What is the capital of France", "Paris"),
            Question(4, "What is the capital of Spain?", "Madrid"),
        };

        var candidates = QuizDuplicateReview.FindCandidates(questions);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, item => Assert.Equal(1, item.KeepId));
        Assert.Contains(candidates, item => item.DuplicateId == 2 && item.MatchType == "Reworded");
        Assert.Contains(candidates, item => item.DuplicateId == 3 && item.MatchType == "Same wording");
        Assert.DoesNotContain(candidates, item => item.DuplicateId == 4);
    }

    [Fact]
    public void FindCandidates_DoesNotFlagSameWordingWithDifferentCorrectAnswer()
    {
        var questions = new[]
        {
            Question(1, "Which country has this capital?", "France"),
            Question(2, "Which country has this capital?", "Spain"),
        };

        Assert.Empty(QuizDuplicateReview.FindCandidates(questions));
    }

    private static QuizQuestion Question(int id, string text, string answer) => new(
        id,
        text,
        answer,
        "Wrong B",
        "Wrong C",
        "Wrong D",
        0,
        "",
        "General Knowledge",
        "medium",
        "Test",
        0);
}
