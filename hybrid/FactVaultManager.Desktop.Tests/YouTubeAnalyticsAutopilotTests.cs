using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeAnalyticsAutopilotTests
{
    [Fact]
    public void ParseResponse_UsesColumnNamesInsteadOfFixedPositions()
    {
        const string json = """
        {
          "columnHeaders": [
            { "name": "video" },
            { "name": "averageViewPercentage" },
            { "name": "views" },
            { "name": "estimatedMinutesWatched" },
            { "name": "averageViewDuration" },
            { "name": "subscribersGained" },
            { "name": "subscribersLost" },
            { "name": "likes" },
            { "name": "comments" }
          ],
          "rows": [["abc123XYZ", 51.4, 420, 812.5, 116.2, 7, 1, 33, 4]]
        }
        """;

        var metric = Assert.Single(YouTubeAnalyticsAutopilotService.ParseResponse(json));
        Assert.Equal("abc123XYZ", metric.VideoId);
        Assert.Equal(420, metric.Views);
        Assert.Equal(51.4, metric.AverageViewPercentage, 1);
        Assert.Equal(812.5, metric.EstimatedMinutesWatched, 1);
        Assert.Equal(7, metric.SubscribersGained);
        Assert.Equal(1, metric.SubscribersLost);
    }

    [Fact]
    public void Classifier_FindsWinnerAndPackagingRescue()
    {
        var winner = new YouTubeGrowthMetric("winner", 1200, 0, 0, 52, 8, 0, 50, 7);
        var rescue = new YouTubeGrowthMetric("rescue", 120, 0, 0, 58, 1, 0, 8, 2);

        var winnerResult = YouTubeGrowthClassifier.Assess(winner, ageDays: 7, medianViewsPerDay: 80);
        var rescueResult = YouTubeGrowthClassifier.Assess(rescue, ageDays: 7, medianViewsPerDay: 80);

        Assert.Equal("Winner", winnerResult.Label);
        Assert.Equal("Packaging rescue", rescueResult.Label);
    }

    [Fact]
    public void Classifier_KeepsYoungVideosInLearning()
    {
        var metric = new YouTubeGrowthMetric("young", 500, 0, 0, 60, 5, 0, 20, 3);
        var result = YouTubeGrowthClassifier.Assess(metric, ageDays: 1.2, medianViewsPerDay: 50);
        Assert.Equal("Learning", result.Label);
    }

    [Fact]
    public void CategoryPlanner_UsesPerformanceThenRotationAndExperimentSlots()
    {
        var now = DateTime.UtcNow;
        var snapshots = new List<YouTubeGrowthSnapshot>
        {
            Snapshot("Science", 94, now),
            Snapshot("Space", 82, now),
            Snapshot("History", 58, now),
            Snapshot("Film", 35, now),
        };
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Science"] = 7,
            ["Space"] = 5,
            ["History"] = 2,
            ["Film"] = 1,
        };

        var plan = YouTubeGrowthCategoryPlanner.BuildPlan(
            ["Science", "Space", "History", "Film"],
            snapshots,
            counts,
            5);

        Assert.Equal(5, plan.Count);
        Assert.Equal("Science", plan[0]);
        Assert.Equal("Space", plan[1]);
        Assert.Equal("History", plan[2]);
        Assert.Equal("Film", plan[3]);
        Assert.Equal("Film", plan[4]);
    }

    [Fact]
    public void OAuthAuthorizationUri_RequestsManagementAndAnalyticsScopes()
    {
        var uri = new Uri(YouTubeOAuthService.CreateAuthorizationUri(
            "client-id",
            "http://127.0.0.1:12345/",
            "state",
            "challenge"));
        var query = Uri.UnescapeDataString(uri.Query);

        Assert.Contains(YouTubeOAuthService.ManagementScope, query, StringComparison.Ordinal);
        Assert.Contains(YouTubeOAuthService.AnalyticsReadonlyScope, query, StringComparison.Ordinal);
    }

    private static YouTubeGrowthSnapshot Snapshot(string category, double score, DateTime checkedAt) =>
        new(
            HistoryId: category.GetHashCode(),
            VideoId: category,
            Category: category,
            CheckedAtUtc: checkedAt,
            AgeDays: 7,
            Views: 100,
            ViewsPerDay: 14,
            EstimatedMinutesWatched: 0,
            AverageViewDurationSeconds: 0,
            AverageViewPercentage: 45,
            SubscribersGained: 1,
            SubscribersLost: 0,
            Likes: 5,
            Comments: 1,
            Score: score,
            Label: "Healthy",
            Reason: "test");
}
