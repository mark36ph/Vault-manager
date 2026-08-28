using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeGrowthAnalyticsUiTests
{
    [Fact]
    public void Summary_UsesGrowthPlanForNextFullVideo()
    {
        var now = DateTime.UtcNow;
        var snapshots = new List<YouTubeGrowthSnapshot>
        {
            Snapshot(1, "Science", 86, "Winner", now),
            Snapshot(2, "Science", 78, "Healthy", now),
            Snapshot(3, "History", 61, "Healthy", now),
        };

        var summary = YouTubeGrowthUiSummaryBuilder.Build(["Science"], snapshots);

        Assert.Equal("Science", summary.RecommendedCategory);
        Assert.Equal("Science", summary.TopCategory);
        Assert.Equal(1, summary.Winners);
        Assert.Contains("Winner", summary.RecommendationReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_IgnoresLearningRowsWhenChoosingTopGrowthCategoryIfMatureEvidenceExists()
    {
        var now = DateTime.UtcNow;
        var snapshots = new List<YouTubeGrowthSnapshot>
        {
            Snapshot(1, "Film", 99, "Learning", now),
            Snapshot(2, "Space", 72, "Healthy", now),
            Snapshot(3, "Space", 82, "Winner", now),
        };

        var summary = YouTubeGrowthUiSummaryBuilder.Build(["Space"], snapshots);

        Assert.Equal("Space", summary.TopCategory);
        Assert.Equal(1, summary.Learning);
        Assert.Equal(1, summary.Winners);
    }

    [Fact]
    public void Summary_UsesBestRealCategoryWhileAllFullVideosAreLearning()
    {
        var now = DateTime.UtcNow;
        var summary = YouTubeGrowthUiSummaryBuilder.Build(
            ["Mathematics"],
            [
                Snapshot(1, "Mathematics", 55, "Learning", now),
                Snapshot(2, "Space", 72, "Learning", now),
                Snapshot(3, "Space", 82, "Learning", now),
                Snapshot(4, "History", 76, "Learning", now),
            ]);

        Assert.Equal("Mathematics", summary.RecommendedCategory);
        Assert.Equal("Space", summary.TopCategory);
        Assert.NotEqual("Learning", summary.TopCategory);
        Assert.Contains("still learning", summary.RecommendationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_FallsBackToRecommendedCategoryWhenNoSnapshotsExist()
    {
        var summary = YouTubeGrowthUiSummaryBuilder.Build(["Mathematics"], []);

        Assert.Equal("Mathematics", summary.TopCategory);
        Assert.NotEqual("Learning", summary.TopCategory);
    }

    private static YouTubeGrowthSnapshot Snapshot(
        int historyId,
        string category,
        double score,
        string label,
        DateTime checkedAtUtc) =>
        new(
            HistoryId: historyId,
            VideoId: "video-" + historyId,
            Category: category,
            CheckedAtUtc: checkedAtUtc,
            AgeDays: 7,
            Views: 100,
            ViewsPerDay: 14.3,
            EstimatedMinutesWatched: 220,
            AverageViewDurationSeconds: 95,
            AverageViewPercentage: 48,
            SubscribersGained: 2,
            SubscribersLost: 0,
            Likes: 5,
            Comments: 1,
            Score: score,
            Label: label,
            Reason: "test");
}
