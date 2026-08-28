using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeGrowthRecommendationGuardTests
{
    [Fact]
    public void Build41OrLater_HasGrowthRecommendationGuard()
    {
        Assert.True(MainShellWindow.CurrentBuildNumber >= 41);
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "InitializeYouTubeGrowthRecommendationGuard",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
    }

    [Fact]
    public void GrowthSummary_UsesThePlannedFullVideoCategory()
    {
        var summary = YouTubeGrowthUiSummaryBuilder.Build(
            ["Nature & Animals"],
            []);

        Assert.Equal("Nature & Animals", summary.RecommendedCategory);
        Assert.DoesNotContain("Short", summary.RecommendationReason, StringComparison.OrdinalIgnoreCase);
    }
}
