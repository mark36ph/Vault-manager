namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionTopicCategorizerTests
{
    [Theory]
    [InlineData("Which planet is the largest in our Solar System?", "Space")]
    [InlineData("What is the capital city of Australia?", "Geography")]
    [InlineData("Who wrote the play Hamlet?", "Arts & Literature")]
    [InlineData("Who painted the Mona Lisa?", "Arts & Literature")]
    [InlineData("How many hearts does an octopus have?", "Nature & Animals")]
    [InlineData("What does HTML stand for?", "Technology")]
    [InlineData("On which surface is Wimbledon played?", "Sports")]
    [InlineData("How many squares are on a standard chessboard?", "General Knowledge")]
    [InlineData("What is the square root of 144?", "Mathematics")]
    [InlineData("Which gas is released during photosynthesis?", "Science")]
    [InlineData("Which composer wrote Symphony No. 5?", "Entertainment")]
    [InlineData("The Great Fire of London occurred in which year?", "History")]
    public void Categorize_AssignsExpectedTopic(string question, string expected)
    {
        Assert.Equal(expected, QuizQuestionTopicCategorizer.Categorize(question));
    }

    [Fact]
    public void Categories_UseCanonicalImportBuckets()
    {
        Assert.Contains("General Knowledge", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Nature & Animals", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Arts & Literature", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Sports", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Entertainment", QuizQuestionTopicCategorizer.Categories);
        Assert.DoesNotContain("Sport", QuizQuestionTopicCategorizer.Categories);
        Assert.DoesNotContain("Miscellaneous", QuizQuestionTopicCategorizer.Categories);
    }

    [Fact]
    public void Categorize_UsesAnswersAndExplanationWhenHelpful()
    {
        var category = QuizQuestionTopicCategorizer.Categorize(
            "Which one is correct?",
            ["Jupiter", "Saturn", "Mars", "Venus"],
            "These are planets in the Solar System.");

        Assert.Equal("Space", category);
    }
}
