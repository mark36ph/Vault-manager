namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionEditingTests
{
    [Fact]
    public void Validate_TrimsEditableFieldsAndNormalizesDifficulty()
    {
        var result = QuizQuestionEditValidator.Validate(new QuizQuestionEditRequest(
            "  What is the capital of France?  ",
            " Paris ", " Rome ", " Madrid ", " Berlin ",
            0,
            "  Paris is the capital of France.  ",
            " Geography ",
            " Intermediate ",
            true));

        Assert.Equal("What is the capital of France?", result.Question);
        Assert.Equal("Paris", result.OptionA);
        Assert.Equal("Geography", result.Category);
        Assert.Equal("medium", result.Difficulty);
        Assert.True(result.IsEnabled);
    }

    [Fact]
    public void Validate_RejectsDuplicateAnswersIgnoringCase()
    {
        var request = new QuizQuestionEditRequest(
            "Which answer is unique?",
            "Alpha", "alpha", "Beta", "Gamma",
            0,
            "",
            "Language",
            "easy",
            true);

        var error = Assert.Throws<InvalidDataException>(() => QuizQuestionEditValidator.Validate(request));

        Assert.Contains("different", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsInvalidCorrectAnswerIndex()
    {
        var request = new QuizQuestionEditRequest(
            "Which planet is closest to the Sun?",
            "Mercury", "Venus", "Earth", "Mars",
            4,
            "Mercury is closest to the Sun.",
            "Space",
            "easy",
            true);

        Assert.Throws<InvalidDataException>(() => QuizQuestionEditValidator.Validate(request));
    }

    [Fact]
    public void EditingQuestionContentProducesNewFingerprint()
    {
        var before = QuizQuestionFingerprint.Create(
            "What is the capital of France?",
            new[] { "Paris", "Rome", "Madrid", "Berlin" });
        var after = QuizQuestionFingerprint.Create(
            "What is the capital city of France?",
            new[] { "Paris", "Rome", "Madrid", "Berlin" });

        Assert.NotEqual(before, after);
    }
}
