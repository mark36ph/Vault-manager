using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotNeedsYouTaskQueueTests
{
    [Fact]
    public void BuildsRelatedInstagramAndReplyTasksFromCurrentState()
    {
        var publishAt = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Row(
                historyId: 41,
                publishAt,
                quiz: "Space Quiz Episode 10",
                youtubePromo: "Uploaded",
                instagramPromo: "Next day",
                relatedVideo: "Needs setting"),
            Row(
                historyId: 42,
                publishAt.AddDays(1),
                quiz: "History Quiz Episode 8",
                youtubePromo: "Uploaded",
                instagramPromo: "Uploaded",
                relatedVideo: "Set"),
        };
        var state = new FactburstFullAutopilotState
        {
            ReplyDrafts =
            [
                new YouTubeReplyDraft
                {
                    CommentId = "comment-1",
                    VideoId = "video-1",
                    Author = "Viewer One",
                    CommentText = "I got 9/10",
                    Draft = "Nice score — thanks for playing!",
                    CreatedAtUtc = new DateTime(2026, 8, 29, 9, 0, 0, DateTimeKind.Utc),
                },
            ],
        };

        var tasks = AutopilotNeedsYouTaskPlanner.Build(rows, state);

        Assert.Equal(3, tasks.Count);
        Assert.Single(tasks.Where(task => task.Kind == AutopilotNeedsYouTaskKind.RelatedVideo));
        Assert.Single(tasks.Where(task => task.Kind == AutopilotNeedsYouTaskKind.InstagramPromo));
        var reply = Assert.Single(tasks.Where(task => task.Kind == AutopilotNeedsYouTaskKind.ViewerReply));
        Assert.Equal("comment-1", reply.CommentId);
        Assert.Equal("Nice score — thanks for playing!", reply.Draft);
    }

    [Fact]
    public void DoesNotQueueCompletedRelatedVideoOrUploadedInstagramPromo()
    {
        var rows = new[]
        {
            Row(
                historyId: 9,
                DateTimeOffset.UtcNow.AddDays(2),
                quiz: "Science Quiz",
                youtubePromo: "Uploaded",
                instagramPromo: "Uploaded",
                relatedVideo: "Set"),
        };

        var tasks = AutopilotNeedsYouTaskPlanner.Build(rows, new FactburstFullAutopilotState());

        Assert.Empty(tasks);
    }

    [Fact]
    public void RemovingReplyDraftOnlyRemovesMatchingComment()
    {
        var state = new FactburstFullAutopilotState
        {
            ReplyDrafts =
            [
                new YouTubeReplyDraft { CommentId = "a", Draft = "A" },
                new YouTubeReplyDraft { CommentId = "b", Draft = "B" },
                new YouTubeReplyDraft { CommentId = "a", Draft = "A newer" },
            ],
        };

        var removed = AutopilotNeedsYouTaskPlanner.RemoveReplyDraft(state, "a");

        Assert.True(removed);
        var remaining = Assert.Single(state.ReplyDrafts);
        Assert.Equal("b", remaining.CommentId);
    }

    [Fact]
    public void DuplicateReplyDraftsAppearOnlyOnceInQueue()
    {
        var older = new DateTime(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);
        var state = new FactburstFullAutopilotState
        {
            ReplyDrafts =
            [
                new YouTubeReplyDraft { CommentId = "same", Draft = "Old", CreatedAtUtc = older },
                new YouTubeReplyDraft { CommentId = "same", Draft = "New", CreatedAtUtc = older.AddMinutes(5) },
            ],
        };

        var task = Assert.Single(AutopilotNeedsYouTaskPlanner.Build([], state));

        Assert.Equal("New", task.Draft);
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
