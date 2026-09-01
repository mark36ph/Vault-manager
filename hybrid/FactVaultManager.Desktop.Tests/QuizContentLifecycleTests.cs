namespace FactVaultManager.Desktop.Tests;

public sealed class QuizContentLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PublicationFailure_WinsOverPublishedState()
    {
        var history = History(published: true);
        var failure = Publication(
            PublicationPlatform.YouTube,
            PublicationContentKind.Promo,
            PublicationStateStatus.Failed,
            failedStep: "verification",
            error: "Related video verification failed.");

        var result = QuizContentLifecycle.Assess(history, [failure], Now, false, false);

        Assert.Equal(QuizContentLifecycleStage.NeedsAttention, result.Stage);
        Assert.True(result.NeedsAttention);
        Assert.Contains("YouTube promo", result.NextAction, StringComparison.Ordinal);
        Assert.Contains("verification", result.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FutureSchedule_WithMissingFolder_NeedsAttention()
    {
        var history = History(
            published: true,
            scheduledFor: Now.AddDays(2).ToString("O"));

        var result = QuizContentLifecycle.Assess(history, [], Now, false, false);

        Assert.Equal(QuizContentLifecycleStage.NeedsAttention, result.Stage);
        Assert.Equal("Resolve project folder", result.NextAction);
    }

    [Fact]
    public void FutureSchedule_WithProjectFolder_IsScheduled()
    {
        var history = History(
            published: true,
            scheduledFor: Now.AddDays(2).ToString("O"));

        var result = QuizContentLifecycle.Assess(history, [], Now, true, true);

        Assert.Equal(QuizContentLifecycleStage.Scheduled, result.Stage);
        Assert.Equal("Review release readiness", result.NextAction);
    }

    [Fact]
    public void PrivateRemoteUpload_IsUploadedRatherThanPublished()
    {
        var history = History(published: true, privacy: "private");
        var uploaded = Publication(
            PublicationPlatform.YouTube,
            PublicationContentKind.Quiz,
            PublicationStateStatus.Uploaded,
            visibility: "private",
            remoteUrl: "https://www.youtube.com/watch?v=test");

        var result = QuizContentLifecycle.Assess(history, [uploaded], Now, true, true);

        Assert.Equal(QuizContentLifecycleStage.Uploaded, result.Stage);
        Assert.Equal("Publish or set schedule", result.NextAction);
    }

    [Fact]
    public void RenderedLocalQuiz_IsReadyForPublishWork()
    {
        var result = QuizContentLifecycle.Assess(History(), [], Now, true, true);

        Assert.Equal(QuizContentLifecycleStage.Rendered, result.Stage);
        Assert.Equal("Open Publish", result.NextAction);
    }

    [Fact]
    public void ExportedQuizWithoutRenderedVideo_ShowsRenderNextAction()
    {
        var result = QuizContentLifecycle.Assess(History(), [], Now, true, false);

        Assert.Equal(QuizContentLifecycleStage.Exported, result.Stage);
        Assert.Equal("Render final video", result.NextAction);
    }

    [Fact]
    public void HistoricalPublishedQuiz_DoesNotBecomeAttentionBecauseFolderWasArchived()
    {
        var result = QuizContentLifecycle.Assess(History(published: true), [], Now, false, false);

        Assert.Equal(QuizContentLifecycleStage.Published, result.Stage);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public void Filters_MatchDerivedStageAndAttention()
    {
        var rendered = new QuizContentLifecycleResult(
            QuizContentLifecycleStage.Rendered,
            "Open Publish",
            "",
            false);
        var attention = new QuizContentLifecycleResult(
            QuizContentLifecycleStage.NeedsAttention,
            "Fix upload",
            "",
            true);

        Assert.True(QuizContentLifecycle.MatchesFilter(rendered, "All"));
        Assert.True(QuizContentLifecycle.MatchesFilter(rendered, "Rendered"));
        Assert.False(QuizContentLifecycle.MatchesFilter(rendered, "Published"));
        Assert.True(QuizContentLifecycle.MatchesFilter(attention, "Needs attention"));
    }

    private static QuizHistorySummary History(
        bool published = false,
        string privacy = "",
        string scheduledFor = "") =>
        new(
            Id: 42,
            Title: "Test quiz",
            Created: "2026-09-01 09:00:00",
            QuestionCount: 10,
            Categories: "General Knowledge",
            Format: "16:9",
            QuestionSeconds: 10,
            ShuffleAnswers: false,
            ProjectFolder: @"C:\Factburst\Test Quiz",
            SeriesName: "Test Quiz",
            EpisodeNumber: 1,
            YouTubeTitle: "Test Quiz #001",
            YouTubeDescription: "",
            Hashtags: "",
            PinnedComment: "",
            PublishedOnYouTube: published,
            YouTubeUrl: published ? "https://www.youtube.com/watch?v=test" : "",
            YouTubePrivacy: privacy,
            YouTubeScheduledFor: scheduledFor);

    private static PublicationStateEntry Publication(
        string platform,
        string contentKind,
        string state,
        string visibility = "",
        string remoteUrl = "",
        string failedStep = "",
        string error = "") =>
        new(
            HistoryId: 42,
            Platform: platform,
            ContentKind: contentKind,
            State: state,
            RemoteId: remoteUrl.Length > 0 ? "remote-42" : "",
            RemoteUrl: remoteUrl,
            Visibility: visibility,
            ScheduledFor: "",
            PublishedAt: "",
            FailedStep: failedStep,
            LastError: error,
            LastAttemptAt: Now.ToString("O"),
            Source: "test",
            UpdatedAt: Now.ToString("O"));
}
