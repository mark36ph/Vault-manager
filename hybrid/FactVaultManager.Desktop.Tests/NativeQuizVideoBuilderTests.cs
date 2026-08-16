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

    [Theory]
    [InlineData(1)]
    [InlineData(61)]
    public void Validate_RejectsInvalidQuestionDuration(int seconds)
    {
        var options = new QuizVideoBuildOptions("Quiz", QuestionSeconds: seconds);

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
