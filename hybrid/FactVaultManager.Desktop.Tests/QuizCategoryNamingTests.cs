namespace FactVaultManager.Desktop.Tests;

public sealed class QuizCategoryNamingTests
{
    [Theory]
    [InlineData(null, "General Knowledge Quiz")]
    [InlineData("", "General Knowledge Quiz")]
    [InlineData("Science", "Science Quiz")]
    [InlineData("History", "History Quiz")]
    [InlineData("Music", "Music Quiz")]
    [InlineData("Film", "Film Quiz")]
    [InlineData("Geography Quiz", "Geography Quiz")]
    [InlineData("Movie Trivia", "Movie Trivia")]
    public void SuggestSeriesName_UsesSelectedCategory(string? category, string expected)
    {
        Assert.Equal(expected, QuizPublishMetadataGenerator.SuggestSeriesName(category));
    }
}
