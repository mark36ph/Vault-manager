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

    [Fact]
    public void GrowthAnalytics_DoesNotRefreshFromYouTubeWhenAnalyticsPageOpens()
    {
        var reliable = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.YouTubeGrowthAnalyticsUiReliable.cs");
        var autopilot = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.YouTubeAnalyticsAutopilot.cs");

        Assert.Contains("Opening Analytics uses the last saved snapshot only.", reliable, StringComparison.Ordinal);
        Assert.DoesNotContain("await RefreshYouTubeGrowthAnalyticsAsync(showErrors: false);", reliable, StringComparison.Ordinal);
        Assert.DoesNotContain("YouTubeAnalyticsAutopilotWindow_Loaded", autopilot, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(TimeSpan.FromSeconds(3));", autopilot, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
