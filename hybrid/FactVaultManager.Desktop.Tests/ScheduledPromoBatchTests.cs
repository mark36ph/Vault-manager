using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class ScheduledPromoBatchTests
{
    [Fact]
    public void SelectMissingPromos_only_returns_missing_rows_in_publish_order()
    {
        var now = new DateTimeOffset(2026, 8, 27, 16, 0, 0, TimeSpan.FromHours(1));
        var ready = Row(1, now.AddDays(1), "Ready", "Already ready");
        var later = Row(3, now.AddDays(3), "Missing", "Later");
        var earlier = Row(2, now.AddDays(2), "Missing", "Earlier");

        var selected = ScheduledPromoBatchPlanner.SelectMissingPromos([later, ready, earlier]);

        Assert.Collection(
            selected,
            row => Assert.Equal(2, row.HistoryId),
            row => Assert.Equal(3, row.HistoryId));
    }

    [Theory]
    [InlineData(14, 0, 0, "Created 14 • Skipped 0 • Failed 0")]
    [InlineData(3, 2, 1, "Created 3 • Skipped 2 • Failed 1")]
    [InlineData(-1, -2, -3, "Created 0 • Skipped 0 • Failed 0")]
    public void Summary_reports_batch_outcome(int created, int skipped, int failed, string expected)
    {
        Assert.Equal(expected, ScheduledPromoBatchPlanner.Summary(created, skipped, failed));
    }

    private static ScheduledReleaseReadinessRow Row(
        int id,
        DateTimeOffset publishAt,
        string promo,
        string title) =>
        new(
            HistoryId: id,
            PublishAt: publishAt,
            PublishAtDisplay: publishAt.ToString("O"),
            Quiz: title,
            Category: "Science",
            FullQuiz: "Scheduled",
            Package: "Ready",
            Promo: promo,
            Tracking: "Missing",
            YouTubePromo: "Missing",
            FacebookPromo: "Missing",
            InstagramPromo: "Missing",
            RelatedVideo: "Waiting",
            FirstComment: "Prepared",
            ReadyCount: 3,
            TotalChecks: 9,
            Readiness: "3/9 • Needs work",
            NextAction: promo == "Missing" ? "Create promo Short" : "Create tracking link",
            ProjectFolder: $"C:/Quiz/{id}");
}
