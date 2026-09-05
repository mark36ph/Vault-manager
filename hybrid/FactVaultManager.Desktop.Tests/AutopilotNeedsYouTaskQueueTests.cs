using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotNeedsYouTaskQueueTests
{
    [Fact]
    public void BuildsRelatedInstagramAndReplyTasksFromCurrentState()
    {
        var publishAt = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
        var rows = new[] { Row(41, publishAt, "Space Quiz Episode 10", "Uploaded", "Next day", "Needs setting"), Row(42, publishAt.AddDays(1), "History Quiz Episode 8", "Uploaded", "Uploaded", "Set") };
        var state = new FactburstFullAutopilotState { ReplyDrafts = [new YouTubeReplyDraft { CommentId = "comment-1", VideoId = "video-1", Author = "Viewer One", CommentText = "I got 9/10", Draft = "Nice score — thanks for playing!", CreatedAtUtc = new DateTime(2026, 8, 29, 9, 0, 0, DateTimeKind.Utc) }] };
        var due = AutopilotNeedsYouTaskPlanner.PromoDueAt(publishAt);
        var tasks = AutopilotNeedsYouTaskPlanner.Build(rows, state, due);
        Assert.Equal(3, tasks.Count);
        Assert.Single(tasks, task => task.Kind == AutopilotNeedsYouTaskKind.RelatedVideo);
        Assert.Single(tasks, task => task.Kind == AutopilotNeedsYouTaskKind.InstagramPromo);
        var reply = Assert.Single(tasks, task => task.Kind == AutopilotNeedsYouTaskKind.ViewerReply);
        Assert.Equal("comment-1", reply.CommentId);
        Assert.Equal("Nice score — thanks for playing!", reply.Draft);
    }

    [Fact]
    public void FutureInstagramPromoDoesNotAppearUntilDue()
    {
        var publishAt = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
        var row = Row(41, publishAt, "Space Quiz Episode 10", "Uploaded", "Next day", "Set");
        var due = AutopilotNeedsYouTaskPlanner.PromoDueAt(publishAt);
        var beforeDue = AutopilotNeedsYouTaskPlanner.Build([row], new FactburstFullAutopilotState(), due.AddMinutes(-1));
        var atDue = AutopilotNeedsYouTaskPlanner.Build([row], new FactburstFullAutopilotState(), due);
        Assert.DoesNotContain(beforeDue, task => task.Kind == AutopilotNeedsYouTaskKind.InstagramPromo);
        var instagram = Assert.Single(atDue, task => task.Kind == AutopilotNeedsYouTaskKind.InstagramPromo);
        Assert.Equal(due, instagram.DueAt);
    }

    [Fact]
    public void DoesNotQueueWaitingOrUploadedInstagramPromo()
    {
        var publishAt = DateTimeOffset.UtcNow.AddDays(-2);
        var rows = new[] { Row(9, publishAt, "Waiting Quiz", "Uploaded", "Waiting", "Set"), Row(10, publishAt, "Uploaded Quiz", "Uploaded", "Uploaded", "Set") };
        var tasks = AutopilotNeedsYouTaskPlanner.Build(rows, new FactburstFullAutopilotState(), DateTimeOffset.Now.AddDays(7));
        Assert.Empty(tasks);
    }

    [Fact]
    public void RemovingReplyDraftOnlyRemovesMatchingComment()
    {
        var state = new FactburstFullAutopilotState { ReplyDrafts = [new YouTubeReplyDraft { CommentId = "a", Draft = "A" }, new YouTubeReplyDraft { CommentId = "b", Draft = "B" }, new YouTubeReplyDraft { CommentId = "a", Draft = "A newer" }] };
        Assert.True(AutopilotNeedsYouTaskPlanner.RemoveReplyDraft(state, "a"));
        var remaining = Assert.Single(state.ReplyDrafts);
        Assert.Equal("b", remaining.CommentId);
    }

    [Fact]
    public void DuplicateReplyDraftsAppearOnlyOnceInQueue()
    {
        var older = new DateTime(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);
        var state = new FactburstFullAutopilotState { ReplyDrafts = [new YouTubeReplyDraft { CommentId = "same", Draft = "Old", CreatedAtUtc = older }, new YouTubeReplyDraft { CommentId = "same", Draft = "New", CreatedAtUtc = older.AddMinutes(5) }] };
        var task = Assert.Single(AutopilotNeedsYouTaskPlanner.Build([], state));
        Assert.Equal("New", task.Draft);
    }

    private static ScheduledReleaseReadinessRow Row(int historyId, DateTimeOffset publishAt, string quiz, string youtubePromo, string instagramPromo, string relatedVideo) =>
        new(historyId, publishAt, publishAt.ToString("O"), quiz, "General Knowledge", "Scheduled", "Ready", "Ready", "Ready", youtubePromo, "Uploaded", instagramPromo, relatedVideo, "Prepared", 8, 8, "8/8 • Ready", "Ready for release", $"C:\\Projects\\{historyId}");
}
