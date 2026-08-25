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
    [InlineData("Which composer wrote Symphony No. 5?", "Music")]
    [InlineData("Which film won the Academy Award for Best Picture?", "Film")]
    [InlineData("Which company uses this logo?", "Logos")]
    [InlineData("Which television sitcom features the Bluth family?", "Entertainment")]
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
        Assert.Contains("Music", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Film", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Logos", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Sports", QuizQuestionTopicCategorizer.Categories);
        Assert.Contains("Entertainment", QuizQuestionTopicCategorizer.Categories);
        Assert.DoesNotContain("Sport", QuizQuestionTopicCategorizer.Categories);
        Assert.DoesNotContain("Miscellaneous", QuizQuestionTopicCategorizer.Categories);
    }

    [Theory]
    [InlineData("film", "Film")]
    [InlineData("movies", "Film")]
    [InlineData("cinema", "Film")]
    [InlineData("film & tv", "Entertainment")]
    [InlineData("logo", "Logos")]
    [InlineData("icons", "Logos")]
    public void CategoryNormalizer_SeparatesFilmFromTelevision(string value, string expected)
    {
        Assert.Equal(expected, QuizQuestionCategoryNormalizer.Normalize(value));
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

    [Theory]
    [InlineData("Which scientist developed the theory of general relativity?", "Albert Einstein developed the theory of relativity.", "Science")]
    [InlineData("What was boxer Muhammad Ali's birth name?", "Muhammad Ali was born Cassius Clay.", "Sports")]
    [InlineData("Which company did Steve Jobs co-found in 1976?", "Steve Jobs co-founded Apple.", "Technology")]
    [InlineData("Which television personality hosted The Oprah Winfrey Show?", "Oprah Winfrey hosted the show.", "Entertainment")]
    [InlineData("Cleopatra VII belonged to which ruling dynasty of Egypt?", "Cleopatra was a Ptolemaic ruler.", "History")]
    public void NormalizeImportedCategory_MovesTextOnlyFamousPeopleOutOfIcons(
        string question,
        string explanation,
        string expected)
    {
        var category = QuizQuestionTopicCategorizer.NormalizeImportedCategory(
            "Logos",
            question,
            explanation: explanation);

        Assert.Equal(expected, category);
    }

    [Fact]
    public void NormalizeImportedCategory_KeepsActualLogoQuestionInIcons()
    {
        var category = QuizQuestionTopicCategorizer.NormalizeImportedCategory(
            "Logos",
            "Which company uses this logo?",
            ["Apple", "Microsoft", "Google", "Amazon"]);

        Assert.Equal("Logos", category);
    }

    [Fact]
    public void NormalizeImportedCategory_KeepsImageQuestionInIcons()
    {
        var category = QuizQuestionTopicCategorizer.NormalizeImportedCategory(
            "Logos",
            "Which person is pictured?",
            imagePath: "question.png");

        Assert.Equal("Logos", category);
    }
}
