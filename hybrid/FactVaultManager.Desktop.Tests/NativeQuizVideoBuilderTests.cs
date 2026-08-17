namespace FactVaultManager.Desktop.Tests;

public sealed class NativeQuizVideoBuilderTests
{
    [Fact]
    public void LandscapeFormat_UsesYouTubeDimensions()
    {
        var options = new QuizVideoBuildOptions("General Knowledge Quiz", Vertical: false);

        Assert.Equal(1920, options.Width);
        Assert.Equal(1080, options.Height);
    }

    [Fact]
    public void VerticalFormat_UsesShortsDimensions()
    {
        var options = new QuizVideoBuildOptions("Quick Quiz", Vertical: true);

        Assert.Equal(1080, options.Width);
        Assert.Equal(1920, options.Height);
    }

    [Fact]
    public void EstimatedDuration_IncludesQuestionAndAnswerTime()
    {
        var options = new QuizVideoBuildOptions(
            "Ten Question Quiz",
            QuestionSeconds: 8,
            AnswerSeconds: 3);

        Assert.Equal(114, options.EstimatedDuration(10));
    }

    [Fact]
    public void EstimatedDuration_AddsNarrationBeforeAnswerTime()
    {
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 8, AnswerSeconds: 3);

        Assert.Equal(20.25, options.EstimatedDuration(1, narrationSeconds: 5.25));
    }

    [Fact]
    public void Countdown_DefaultsToFinalThreeSeconds()
    {
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 8);

        Assert.Equal(3, options.CountdownSeconds);
    }

    [Fact]
    public void Countdown_UsesAvailableQuestionTimeForShortQuestions()
    {
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 2);

        Assert.Equal(2, options.CountdownSeconds);
    }

    [Fact]
    public void Countdown_CanBeDisabledWithoutChangingDuration()
    {
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: 8, ShowCountdown: false);

        Assert.Equal(0, options.CountdownSeconds);
        Assert.Equal(15, options.EstimatedDuration(1));
    }

    [Fact]
    public void RevealPulse_DefaultsToHalfSecondAndCanBeDisabled()
    {
        var enabled = new QuizVideoBuildOptions("Quiz", AnswerSeconds: 3);
        var disabled = new QuizVideoBuildOptions("Quiz", AnswerSeconds: 3, AnimateAnswerReveal: false);

        Assert.Equal(0.5, enabled.RevealEmphasisSeconds);
        Assert.Equal(0, disabled.RevealEmphasisSeconds);
    }

    [Fact]
    public void NarrationScript_QuestionOnly_DoesNotReadChoices()
    {
        var script = QuizNarrationScript.Create(Question(), includeAnswers: false);

        Assert.Equal("Which planet is largest?", script);
        Assert.DoesNotContain("Jupiter", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NarrationScript_WithChoices_ReadsLettersInBankOrder()
    {
        var script = QuizNarrationScript.Create(Question(), includeAnswers: true);

        Assert.Equal("Which planet is largest? A. Earth. B. Mars. C. Jupiter. D. Venus.", script);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(61)]
    public void Validate_RejectsInvalidQuestionDuration(int seconds)
    {
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: seconds);

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void Validate_AllowsQuizWithoutLogo()
    {
        var options = new QuizVideoBuildOptions("Quiz", QuizLogoPath: "");

        options.Validate();
    }

    [Fact]
    public void Validate_RejectsMissingQuizLogo()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-quiz-logo-{Guid.NewGuid():N}.png");
        var options = new QuizVideoBuildOptions("Quiz", QuizLogoPath: missing);

        Assert.Throws<FileNotFoundException>(() => options.Validate());
    }

    [Fact]
    public void QuizBranding_RejectsUnsupportedLogoExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quiz-logo-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not an image");
        try
        {
            Assert.Throws<InvalidDataException>(() => QuizBranding.ValidateLogoPath(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static QuizQuestion Question() => new(
        1,
        "Which planet is largest?",
        "Earth",
        "Mars",
        "Jupiter",
        "Venus",
        2,
        "Jupiter is the largest planet.",
        "Space",
        "easy",
        "Test",
        0);
}
