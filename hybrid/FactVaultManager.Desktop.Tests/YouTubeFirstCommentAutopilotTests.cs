using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeFirstCommentAutopilotTests
{
    [Fact]
    public void ShouldWatch_FutureScheduledFullQuizCreatedAfterActivation()
    {
        var activated = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var history = History(
            scheduledFor: activated.AddHours(4).ToString("O"),
            firstCommentId: "");

        Assert.True(YouTubeFirstCommentAutopilotPlanner.ShouldWatch(history, activated, new HashSet<int>()));
    }

    [Fact]
    public void ShouldWatch_DoesNotBackfillOldPublishedQuizOnFirstActivation()
    {
        var activated = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var history = History(
            scheduledFor: activated.AddDays(-2).ToString("O"),
            firstCommentId: "");

        Assert.False(YouTubeFirstCommentAutopilotPlanner.ShouldWatch(history, activated, new HashSet<int>()));
    }

    [Fact]
    public void ShouldWatch_KeepsPreviouslyWatchedQuizWhenYouTubeScheduleFieldIsCleared()
    {
        var activated = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var history = History(scheduledFor: "", firstCommentId: "");

        Assert.True(YouTubeFirstCommentAutopilotPlanner.ShouldWatch(history, activated, new HashSet<int> { history.Id }));
    }

    [Fact]
    public void ShouldWatch_StopsAfterFirstCommentIsRecorded()
    {
        var activated = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var history = History(
            scheduledFor: activated.AddHours(1).ToString("O"),
            firstCommentId: "comment-123");

        Assert.False(YouTubeFirstCommentAutopilotPlanner.ShouldWatch(history, activated, new HashSet<int> { history.Id }));
    }

    [Fact]
    public void ShouldWatch_IgnoresPromoShorts()
    {
        var activated = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var history = History(
            format: "9:16",
            scheduledFor: activated.AddHours(1).ToString("O"),
            firstCommentId: "");

        Assert.False(YouTubeFirstCommentAutopilotPlanner.ShouldWatch(history, activated, new HashSet<int>()));
    }

    private static QuizHistorySummary History(
        string format = "16:9",
        string scheduledFor = "",
        string firstCommentId = "") =>
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
            YouTubeFirstCommentId: firstCommentId,
            YouTubePrivacy: "private",
            YouTubeScheduledFor: scheduledFor);
}
