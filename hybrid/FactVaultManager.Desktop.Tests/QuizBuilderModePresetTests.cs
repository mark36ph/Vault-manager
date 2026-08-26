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
        Assert.False(preset.IsMarathon);
    }

    [Theory]
    [InlineData("Marathon 30", 30)]
    [InlineData("Marathon 50", 50)]
    [InlineData("Marathon 100", 100)]
    public void Marathon_UsesProgressiveLandscapeDefaults(string name, int count)
    {
        var preset = QuizBuilderModePresets.All.Single(item => item.Name == name);

        Assert.Equal(count, preset.QuestionCount);
        Assert.Equal(8, preset.SecondsPerQuestion);
        Assert.False(preset.Vertical);
        Assert.Null(preset.Difficulty);
        Assert.True(preset.IsMarathon);
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
        Assert.False(preset.IsMarathon);
    }
}
