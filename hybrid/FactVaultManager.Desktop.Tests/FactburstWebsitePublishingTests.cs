using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsitePublishingTests
{
    [Fact]
    public void BuildQuestion_preserves_saved_answer_order_and_correct_index()
    {
        var question = FactburstWebsiteQuizBuilder.BuildQuestion(
            "Which planet is closest to the Sun?",
            ["Venus", "Mercury", "Earth", "Mars"],
            1,
            "Mercury is the closest planet to the Sun.");

        Assert.Equal(["Venus", "Mercury", "Earth", "Mars"], question.Answers);
        Assert.Equal("B", question.CorrectAnswer);
        Assert.Equal("Which planet is closest to the Sun?", question.Question);
        Assert.Equal("Mercury is the closest planet to the Sun.", question.Explanation);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(2, "C")]
    [InlineData(3, "D")]
    public void BuildQuestion_maps_saved_correct_index_to_answer_letter(int correctIndex, string expected)
    {
        var question = FactburstWebsiteQuizBuilder.BuildQuestion(
            "Question",
            ["One", "Two", "Three", "Four"],
            correctIndex,
            "Explanation");

        Assert.Equal(expected, question.CorrectAnswer);
    }

    [Fact]
    public void BuildQuestion_rejects_duplicate_answers()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            FactburstWebsiteQuizBuilder.BuildQuestion(
                "Question",
                ["Same", "Same", "Three", "Four"],
                0,
                "Explanation"));

        Assert.Contains("distinct", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildQuestion_rejects_invalid_correct_index()
    {
        Assert.Throws<InvalidDataException>(() =>
            FactburstWebsiteQuizBuilder.BuildQuestion(
                "Question",
                ["One", "Two", "Three", "Four"],
                4,
                "Explanation"));
    }
}
