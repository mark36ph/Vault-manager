namespace FactVaultManager.Desktop.Tests;

public sealed class QuizBuilderModePresetTests
{
    [Fact]
    public void Full_UsesTenQuestionsEightSecondsAndLandscape()
    {
        var preset = QuizBuilderModePresets.Full;

        Assert.Equal("Full", preset.Name);
        Assert.Equal(10, preset.QuestionCount);
        Assert.Equal(8, preset.SecondsPerQuestion);
        Assert.False(preset.Vertical);
        Assert.Null(preset.Difficulty);
    }

    [Fact]
    public void Shorts_UsesOneQuestionThreeSecondsAndVertical()
    {
        var preset = QuizBuilderModePresets.Shorts;

        Assert.Equal("Shorts", preset.Name);
        Assert.Equal(1, preset.QuestionCount);
        Assert.Equal(3, preset.SecondsPerQuestion);
        Assert.True(preset.Vertical);
        Assert.Equal("easy", preset.Difficulty);
    }
}
