namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionDisplayOrderTests
{
    [Fact]
    public void Preserve_KeepsPreviousVisibleOrder_AfterQuestionContentChanges()
    {
        var current = new[]
        {
            Question(3, "Changed wording"),
            Question(1, "First"),
            Question(2, "Second"),
        };

        var ordered = QuizQuestionDisplayOrder.Preserve(current, new[] { 1, 2, 3 });

        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(question => question.Id));
        Assert.Equal("Changed wording", ordered[2].Question);
    }

    [Fact]
    public void Preserve_AppendsNewQuestionsAfterKnownRows()
    {
        var current = new[]
        {
            Question(9, "Newer"),
            Question(4, "Four"),
            Question(2, "Two"),
            Question(8, "New"),
        };

        var ordered = QuizQuestionDisplayOrder.Preserve(current, new[] { 4, 2 });

        Assert.Equal(new[] { 4, 2, 8, 9 }, ordered.Select(question => question.Id));
    }

    [Fact]
    public void Preserve_IgnoresRowsNoLongerVisible()
    {
        var current = new[]
        {
            Question(7, "Seven"),
            Question(5, "Five"),
        };

        var ordered = QuizQuestionDisplayOrder.Preserve(current, new[] { 5, 6, 7 });

        Assert.Equal(new[] { 5, 7 }, ordered.Select(question => question.Id));
    }

    private static QuizQuestion Question(int id, string text) => new(
        id,
        text,
        "A",
        "B",
        "C",
        "D",
        0,
        "Explanation",
        "Science",
        "medium",
        "Test",
        0,
        true);
}
