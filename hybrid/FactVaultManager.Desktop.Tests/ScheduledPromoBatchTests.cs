using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class ScheduledPromoBatchTests
{
    [Fact]
    public void SelectMissingPromos_only_returns_missing_rows_in_publish_order()
    {
        var now = LocalDateTime(2026, 8, 27, 16, 0);
        var ready = Row(1, now.AddDays(1), "Ready", "Already ready");
        var later = Row(3, now.AddDays(3), "Missing", "Later");
        var earlier = Row(2, now.AddDays(2), "Missing", "Earlier");

        var selected = ScheduledPromoBatchPlanner.SelectMissingPromos([later, ready, earlier]);

        Assert.Collection(
            selected,
            row => Assert.Equal(2, row.HistoryId),
            row => Assert.Equal(3, row.HistoryId));
    }

    [Fact]
    public void SelectMissingScheduledUploads_only_returns_ready_tracked_missing_youtube_or_facebook()
    {
        var now = LocalDateTime(2026, 8, 27, 16, 0);
        var ready = Row(1, now.AddDays(1), "Ready", "Ready", "Ready", "Ready", "First");
        var alreadyUploaded = Row(2, now.AddDays(2), "Ready", "Ready", "Uploaded", "Uploaded", "Done");
        var missingTracking = Row(3, now.AddDays(3), "Ready", "Missing", "Ready", "Ready", "No tracking");
        var onlyFacebook = Row(4, now.AddDays(4), "Ready", "Ready", "Uploaded", "Ready", "Facebook only");

        var selected = ScheduledPromoBatchPlanner.SelectMissingScheduledUploads(
            [onlyFacebook, missingTracking, alreadyUploaded, ready]);

        Assert.Collection(
            selected,
            target =>
            {
                Assert.Equal(1, target.HistoryId);
                Assert.True(target.YouTube);
                Assert.True(target.Facebook);
            },
            target =>
            {
                Assert.Equal(4, target.HistoryId);
                Assert.False(target.YouTube);
                Assert.True(target.Facebook);
            });
    }

    [Fact]
    public void ResolvePromoPublishAt_uses_same_release_day_and_requested_local_time()
    {
        var longForm = LocalDateTime(2026, 8, 28, 9, 0);
        var now = LocalDateTime(2026, 8, 27, 18, 0);

        var scheduled = ScheduledPromoBatchPlanner.ResolvePromoPublishAt(longForm, "18:00", now);

        Assert.Equal(longForm.Date, scheduled.Date);
        Assert.Equal(18, scheduled.Hour);
        Assert.Equal(0, scheduled.Minute);
        Assert.True(scheduled >= longForm.AddMinutes(30));
    }

    [Fact]
    public void ResolvePromoPublishAt_rejects_time_before_long_form_release()
    {
        var longForm = LocalDateTime(2026, 8, 28, 9, 0);
        var now = LocalDateTime(2026, 8, 27, 18, 0);

        var error = Assert.Throws<ArgumentException>(() =>
            ScheduledPromoBatchPlanner.ResolvePromoPublishAt(longForm, "09:15", now));

        Assert.Contains("30 minutes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(14, 0, 0, "Created 14 • Skipped 0 • Failed 0")]
    [InlineData(3, 2, 1, "Created 3 • Skipped 2 • Failed 1")]
    [InlineData(-1, -2, -3, "Created 0 • Skipped 0 • Failed 0")]
    public void Summary_reports_batch_outcome(int created, int skipped, int failed, string expected)
    {
        Assert.Equal(expected, ScheduledPromoBatchPlanner.Summary(created, skipped, failed));
    }

    [Fact]
    public void PublishingSummary_reports_platform_schedules()
    {
        Assert.Equal(
            "YouTube scheduled 7 • Facebook scheduled 6 • Failed 1",
            ScheduledPromoBatchPlanner.PublishingSummary(7, 6, 1));
    }

    private static ScheduledReleaseReadinessRow Row(
        int id,
        DateTimeOffset publishAt,
        string promo,
        string title) =>
        Row(id, publishAt, promo, "Missing", "Missing", "Missing", title);

    private static ScheduledReleaseReadinessRow Row(
        int id,
        DateTimeOffset publishAt,
        string promo,
        string tracking,
        string youtubePromo,
        string facebookPromo,
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
            Tracking: tracking,
            YouTubePromo: youtubePromo,
            FacebookPromo: facebookPromo,
            InstagramPromo: "Ready",
            RelatedVideo: "Waiting",
            FirstComment: "Prepared",
            ReadyCount: 3,
            TotalChecks: 9,
            Readiness: "3/9 • Needs work",
            NextAction: promo == "Missing" ? "Create promo Short" : "Upload promo",
            ProjectFolder: $"C:/Quiz/{id}");

    private static DateTimeOffset LocalDateTime(int year, int month, int day, int hour, int minute)
    {
        var value = DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }
}
