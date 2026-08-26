namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThumbnailClaimSafetyTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(6, false)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    public void AutomaticHooks_DoNotInventPerformancePercentages(int count, bool logoQuiz)
    {
        var hook = QuizThumbnailIntelligence.DefaultHook(count, logoQuiz);

        Assert.DoesNotContain("%", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("99", hook, StringComparison.Ordinal);
    }
}
