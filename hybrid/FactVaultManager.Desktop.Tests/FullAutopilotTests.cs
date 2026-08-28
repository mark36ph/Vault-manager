using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FullAutopilotTests
{
    [Fact]
    public void FacebookFirstCommentWatch_IncludesFutureReleaseAndRejectsBackCatalogue()
    {
        var activated = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(activated);

        Assert.True(FullAutopilotReleasePlanner.ShouldWatchFacebookFirstComment(
            History(facebookScheduledFor: now.AddHours(2).ToString("O")), activated, now));
        Assert.False(FullAutopilotReleasePlanner.ShouldWatchFacebookFirstComment(
            History(facebookScheduledFor: now.AddDays(-2).ToString("O")), activated, now));
    }

    [Fact]
    public void YouTubePostReleaseWatch_OnlyTracksFullVideos()
    {
        var activated = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(activated);
        Assert.True(FullAutopilotReleasePlanner.ShouldWatchYouTubePostRelease(
            History(youtubeScheduledFor: now.AddHours(1).ToString("O")), activated, now));
        Assert.False(FullAutopilotReleasePlanner.ShouldWatchYouTubePostRelease(
            History(format: "9:16", youtubeScheduledFor: now.AddHours(1).ToString("O")), activated, now));
    }

    [Theory]
    [InlineData("Great quiz, loved it!", "Glad you enjoyed")]
    [InlineData("Question 4 is wrong", "check it")]
    [InlineData("Why is that the answer?", "Thanks for the question")]
    [InlineData("I got 8/10", "Nice score")]
    [InlineData("hello", "What score did you get")]
    public void ReplyDraftPlanner_PreparesShortApprovalDrafts(string comment, string expected)
    {
        Assert.Contains(expected, YouTubeReplyDraftPlanner.Draft(comment), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WinnerFollowUpPlanner_DeduplicatesWinnerAndConsumesOneSlot()
    {
        var checkedAt = DateTime.UtcNow;
        var state = new FactburstFullAutopilotState { ActivatedAtUtc = checkedAt.AddMinutes(-5) };
        var snapshot = Snapshot("winner-video", "Science", checkedAt, "Winner");

        Assert.Equal(1, YouTubeWinnerFollowUpPlanner.EnqueueNewWinners(state, [snapshot, snapshot], checkedAt));
        Assert.Equal(0, YouTubeWinnerFollowUpPlanner.EnqueueNewWinners(state, [snapshot], checkedAt));

        var consumed = YouTubeWinnerFollowUpPlanner.ConsumeNext(state, checkedAt.AddMinutes(1));
        Assert.NotNull(consumed);
        Assert.Equal("Science", consumed!.Category);
        Assert.True(consumed.Consumed);
        Assert.Null(YouTubeWinnerFollowUpPlanner.ConsumeNext(state, checkedAt.AddMinutes(2)));
    }

    [Fact]
    public void WinnerPromoSchedule_SpreadsThreePushesAcrossLaterDays()
    {
        var now = new DateTimeOffset(2026, 8, 29, 11, 0, 0, TimeSpan.Zero);
        var scheduled = YouTubeWinnerPromoSchedulePlanner.Create(now, 3);

        Assert.Equal(3, scheduled.Count);
        Assert.True(scheduled[0] > now);
        Assert.True(scheduled[1] > scheduled[0]);
        Assert.True(scheduled[2] > scheduled[1]);
        Assert.Equal(2, (scheduled[1].Date - scheduled[0].Date).Days);
        Assert.Equal(3, (scheduled[2].Date - scheduled[1].Date).Days);
    }

    [Fact]
    public void PostReleaseAuditService_ParsesTitlePrivacyAndThumbnail()
    {
        const string json = """
            {"items":[{"id":"abcdefghijk","snippet":{"title":"Science Quiz","thumbnails":{"high":{"url":"https://img.example/test.jpg"}}},"status":{"privacyStatus":"public"}}]}
            """;

        var item = Assert.Single(YouTubePostReleaseAuditService.Parse(json));
        Assert.Equal("abcdefghijk", item.VideoId);
        Assert.Equal("Science Quiz", item.Title);
        Assert.Equal("public", item.PrivacyStatus);
        Assert.True(item.ThumbnailPresent);
    }

    [Fact]
    public void FullAutopilotStateStore_PersistsQueuesAndDrafts()
    {
        var root = Path.Combine(Path.GetTempPath(), "factburst-full-autopilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = Path.Combine(root, "settings.json");
            var state = new FactburstFullAutopilotState
            {
                ActivatedAtUtc = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc),
                FacebookFirstCommentWatchIds = [7],
                YouTubePostReleaseWatchIds = [9],
                ReplyDrafts = [new YouTubeReplyDraft { CommentId = "c1", Draft = "Thanks!", CreatedAtUtc = DateTime.UtcNow }],
            };
            FactburstFullAutopilotStateStore.Save(settings, state);
            var restored = FactburstFullAutopilotStateStore.Load(settings);

            Assert.Equal([7], restored.FacebookFirstCommentWatchIds);
            Assert.Equal([9], restored.YouTubePostReleaseWatchIds);
            Assert.Equal("Thanks!", Assert.Single(restored.ReplyDrafts).Draft);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static YouTubeGrowthSnapshot Snapshot(string videoId, string category, DateTime checkedAt, string label) =>
        new(
            HistoryId: 101,
            VideoId: videoId,
            Category: category,
            CheckedAtUtc: checkedAt,
            AgeDays: 3,
            Views: 100,
            ViewsPerDay: 33.3,
            EstimatedMinutesWatched: 120,
            AverageViewDurationSeconds: 90,
            AverageViewPercentage: 55,
            SubscribersGained: 2,
            SubscribersLost: 0,
            Likes: 10,
            Comments: 4,
            Score: 88,
            Label: label,
            Reason: "Strong velocity");

    private static QuizHistorySummary History(
        string format = "16:9",
        string youtubeScheduledFor = "",
        string facebookScheduledFor = "") =>
        new(
            Id: 101,
            Title: "Science Quiz",
            Created: "2026-08-29 09:00:00",
            QuestionCount: 10,
            Categories: "Science",
            Format: format,
            QuestionSeconds: 8,
            ShuffleAnswers: true,
            ProjectFolder: "C:/Factburst/Science",
            SeriesName: "Science Quiz",
            EpisodeNumber: 1,
            YouTubeTitle: "Can You Get 10/10? | Science Quiz #001",
            YouTubeDescription: "Description",
            Hashtags: "#quiz",
            PinnedComment: "How many did you get right?",
            PublishedOnYouTube: true,
            YouTubeUrl: "https://www.youtube.com/watch?v=abcdefghijk",
            PublishedOnFacebook: true,
            FacebookUrl: "https://www.facebook.com/reel/123456789",
            YouTubeFirstCommentId: "",
            FacebookFirstCommentId: "",
            YouTubePrivacy: "private",
            YouTubeScheduledFor: youtubeScheduledFor,
            FacebookScheduledFor: facebookScheduledFor);
}
