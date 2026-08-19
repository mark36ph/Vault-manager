namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionImportCleanupTests
{
    [Theory]
    [InlineData("Sport", "Sports")]
    [InlineData("Nature", "Nature & Animals")]
    [InlineData("Literature", "Arts & Literature")]
    [InlineData("Art & Culture", "Arts & Literature")]
    [InlineData("Music", "Music")]
    [InlineData("Classical Music", "Music")]
    [InlineData("Film & TV", "Entertainment")]
    [InlineData("Miscellaneous", "General Knowledge")]
    public void CategoryNormalizer_MergesLegacyAliases(string input, string expected)
    {
        Assert.Equal(expected, QuizQuestionCategoryNormalizer.Normalize(input));
    }

    [Fact]
    public void CategoryNormalizer_PreservesUnknownCustomCategory()
    {
        Assert.Equal("My Custom Quiz", QuizQuestionCategoryNormalizer.Normalize(" My Custom Quiz "));
    }

    [Theory]
    [InlineData("What is the capital city of Australia?", "What is the capital of Australia?", "Canberra")]
    [InlineData("Which is the largest living land animal?", "What is the largest living land animal?", "African elephant")]
    [InlineData("What is the SI unit of electric current?", "Which SI unit measures electric current?", "ampere")]
    public void DuplicateDetector_FindsRewordedSameFact(string first, string second, string answer)
    {
        Assert.True(QuizQuestionDuplicateDetector.IsLikelyDuplicate(first, answer, second, answer));
    }

    [Fact]
    public void DuplicateDetector_DoesNotMergeQuestionsWithDifferentAnswers()
    {
        Assert.False(QuizQuestionDuplicateDetector.IsLikelyDuplicate(
            "Which planet is closest to the Sun?",
            "Mercury",
            "Which planet is farthest from the Sun?",
            "Neptune"));
    }

    [Fact]
    public void DuplicateDetector_DoesNotMergeDifferentMathOperators()
    {
        Assert.False(QuizQuestionDuplicateDetector.IsLikelyDuplicate(
            "What is 2 + 2?",
            "4",
            "What is 2 - 2?",
            "0"));
    }
}
