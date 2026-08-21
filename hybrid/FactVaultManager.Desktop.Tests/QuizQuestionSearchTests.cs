using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizQuestionSearchTests
{
    [Theory]
    [InlineData("123", 123)]
    [InlineData(" #123 ", 123)]
    [InlineData("No. 123", 123)]
    public void ExactId_AcceptsQuestionNumberFormats(string search, int expected)
    {
        Assert.Equal(expected, QuizQuestionSearch.ExactId(search));
    }

    [Theory]
    [InlineData("")]
    [InlineData("history")]
    [InlineData("0")]
    [InlineData("-5")]
    public void ExactId_IgnoresNonQuestionNumbers(string search)
    {
        Assert.Null(QuizQuestionSearch.ExactId(search));
    }
}
