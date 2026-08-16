namespace FactVaultManager.Desktop.Tests;

public sealed class QuizDraftOperationsTests
{
    [Fact]
    public void Move_ReordersSelectedQuestionWithoutChangingOthers()
    {
        var first = Question(1, "First");
        var second = Question(2, "Second");
        var third = Question(3, "Third");

        var result = QuizDraftOperations.Move([first, second, third], second.Id, -1);

        Assert.Equal([2, 1, 3], result.Select(question => question.Id));
    }

    [Fact]
    public void Add_RejectsDuplicateQuestion()
    {
        var question = Question(7, "Duplicate");

        var error = Assert.Throws<InvalidOperationException>(() =>
            QuizDraftOperations.Add([question], question));

        Assert.Contains("already", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replace_KeepsDraftPosition()
    {
        var first = Question(1, "First");
        var second = Question(2, "Second");
        var replacement = Question(9, "Replacement");

        var result = QuizDraftOperations.Replace([first, second], second.Id, replacement);

        Assert.Equal([1, 9], result.Select(question => question.Id));
    }

    [Fact]
    public void ShuffleAnswers_PreservesCorrectAnswerAndQuestionIdentity()
    {
        var original = Question(42, "Capital?", correctIndex: 2);
        var originalAnswers = original.Answers.ToArray();
        var originalCorrect = original.CorrectAnswer;

        var shuffled = QuizAnswerShuffler.ShuffleQuestion(original, new Random(12345));

        Assert.Equal(original.Id, shuffled.Id);
        Assert.Equal(original.Question, shuffled.Question);
        Assert.Equal(original.Category, shuffled.Category);
        Assert.Equal(originalCorrect, shuffled.CorrectAnswer);
        Assert.Equal(originalAnswers.OrderBy(value => value), shuffled.Answers.OrderBy(value => value));
        Assert.Equal(originalAnswers, original.Answers);
    }

    private static QuizQuestion Question(int id, string text, int correctIndex = 0) => new(
        id,
        text,
        "Alpha",
        "Bravo",
        "Charlie",
        "Delta",
        correctIndex,
        "Explanation",
        "Science",
        "medium",
        "Test",
        3,
        true);
}
