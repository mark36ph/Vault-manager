namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionTopicCategorizerTests
{
    [Theory]
    [InlineData("Which planet is the largest in our Solar System?", "Space")]
    [InlineData("What is the capital city of Australia?", "Geography")]
    [InlineData("Who wrote the play Hamlet?", "Literature")]
    [InlineData("Who painted the Mona Lisa?", "Art & Culture")]
    [InlineData("How many hearts does an octopus have?", "Nature")]
    [InlineData("What does HTML stand for?", "Technology")]
    [InlineData("On which surface is Wimbledon played?", "Sport")]
    [InlineData("What is the square root of 144?", "Mathematics")]
    [InlineData("Which gas is released during photosynthesis?", "Science")]
    [InlineData("Which composer wrote Symphony No. 5?", "Music")]
    [InlineData("The Great Fire of London occurred in which year?", "History")]
    public void Categorize_AssignsExpectedTopic(string question, string expected)
    {
        Assert.Equal(expected, QuizQuestionTopicCategorizer.Categorize(question));
    }

    [Fact]
    public void Categories_DoNotUseGeneralKnowledgeAsTopicBucket()
    {
        Assert.DoesNotContain("General Knowledge", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Miscellaneous", QuizQuestionTopicCategorizer.Categories);
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
