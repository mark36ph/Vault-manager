using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotNeedsYouAlignedPlannerTests
{
    [Fact]
    public void ScreenshotState_ExcludesFutureAndWaitingInstagramPromosFromNeedsYou()
    {
        var publishAt = new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(0, 14)
            .Select(index => Row(
                historyId: 100 + index,
                publishAt: publishAt.AddDays(index),
                quiz: $"Quiz {index + 1}",
                youtubePromo: index == 0 ? "Uploaded" : "Missing",
                instagramPromo: index < 8 ? "Next day" : "Waiting",
                relatedVideo: index == 0 ? "Needs setting" : "Waiting"))
            .ToList();
        var state = new FactburstFullAutopilotState
        {
            ReplyDrafts = Enumerable.Range(0, 12)
                .Select(index => new YouTubeReplyDraft
                {
                    CommentId = $"comment-{index}",
                    VideoId = $"video-{index}",
                    Author = $"Viewer {index}",
                    CommentText = "Quiz comment",
                    Draft = "Thanks for playing!",
                    CreatedAtUtc = new DateTime(2026, 8, 29, 9, index, 0, DateTimeKind.Utc),
                })
                .ToList(),
        };
        var beforeAnyInstagramDue = AutopilotNeedsYouTaskPlanner.PromoDueAt(publishAt).AddMinutes(-1);

        var tasks = AutopilotNeedsYouAlignedPlanner.Build(rows, state, [], beforeAnyInstagramDue);

        Assert.Equal(26, tasks.Count);
        Assert.Equal(14, AutopilotNeedsYouAlignedPlanner.Count(tasks, AutopilotAlignedTaskKind.RelatedVideo));
        Assert.Equal(0, AutopilotNeedsYouAlignedPlanner.Count(tasks, AutopilotAlignedTaskKind.InstagramPromo));
        Assert.Equal(12, AutopilotNeedsYouAlignedPlanner.Count(tasks, AutopilotAlignedTaskKind.ViewerReply));
        Assert.Single(tasks.Where(task => task.Kind == AutopilotAlignedTaskKind.RelatedVideo && task.ActionReady));
        Assert.Contains(tasks, task => task.Kind == AutopilotAlignedTaskKind.RelatedVideo && task.State == "Waiting" && !task.ActionReady);
        Assert.Equal("26 shown • 14 Related video • 0 Instagram • 12 Replies", AutopilotNeedsYouAlignedPlanner.Summary(tasks, tasks.Count));
    }

    [Fact]
    public void InstagramPromo_AppearsOnlyWhenNextDayAndPostingTimeHasArrived()
    {
        var publishAt = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
        var due = AutopilotNeedsYouTaskPlanner.PromoDueAt(publishAt);
        var nextDay = Row(41, publishAt, "Ready promo", "Uploaded", "Next day", "Set");
        var waiting = Row(42, publishAt, "Waiting promo", "Uploaded", "Waiting", "Set");

        var beforeDue = AutopilotNeedsYouAlignedPlanner.Build(
            [nextDay, waiting],
            new FactburstFullAutopilotState(),
            [],
            due.AddMinutes(-1));
        var atDue = AutopilotNeedsYouAlignedPlanner.Build(
            [nextDay, waiting],
            new FactburstFullAutopilotState(),
            [],
            due);

        Assert.DoesNotContain(beforeDue, task => task.Kind == AutopilotAlignedTaskKind.InstagramPromo);
        var instagram = Assert.Single(atDue.Where(task => task.Kind == AutopilotAlignedTaskKind.InstagramPromo));
        Assert.Equal(41, instagram.HistoryId);
        Assert.True(instagram.ActionReady);
        Assert.Equal(due, instagram.DueAt);
    }

    [Fact]
    public void CompletedManualStates_DoNotAppear()
    {
        var row = Row(
            9,
            DateTimeOffset.UtcNow.AddDays(2),
            "Science Quiz",
            "Uploaded",
            "Uploaded",
            "Set");

        var tasks = AutopilotNeedsYouAlignedPlanner.Build([row], new FactburstFullAutopilotState(), []);

        Assert.Empty(tasks);
    }

    private static ScheduledReleaseReadinessRow Row(
        int historyId,
        DateTimeOffset publishAt,
        string quiz,
        string youtubePromo,
        string instagramPromo,
        string relatedVideo) =>
        new(
            historyId,
            publishAt,
            publishAt.ToString("O"),
            quiz,
            "General Knowledge",
            "Scheduled",
            "Ready",
            "Ready",
            "Ready",
            youtubePromo,
            "Uploaded",
            instagramPromo,
            relatedVideo,
            "Prepared",
            8,
            8,
            "8/8 • Ready",
            "Ready for release",
            $"C:\\Projects\\{historyId}");
}
