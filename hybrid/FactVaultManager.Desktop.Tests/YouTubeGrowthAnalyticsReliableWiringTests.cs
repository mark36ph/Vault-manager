using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeGrowthAnalyticsReliableWiringTests
{
    [Fact]
    public void MainWindowInitializer_ExposesReliableGrowthUiEntryPoint()
    {
        var method = typeof(MainShellWindow).GetMethod(
            "InitializeYouTubeGrowthAnalyticsUiReliably",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        Assert.NotNull(method);
    }

    [Fact]
    public void GrowthSummary_StillRecommendsFullVideoCategoryFromPlan()
    {
        var summary = YouTubeGrowthUiSummaryBuilder.Build(
            ["Science"],
            Array.Empty<YouTubeGrowthSnapshot>());

        Assert.Equal("Science", summary.RecommendedCategory);
        Assert.Contains("learning", summary.RecommendationReason, StringComparison.OrdinalIgnoreCase);
    }
}
