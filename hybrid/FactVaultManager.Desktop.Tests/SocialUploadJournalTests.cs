namespace FactVaultManager.Desktop.Tests;

public sealed class SocialUploadJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "FactVaultManager.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Begin_PersistsRequestedSteps()
    {
        var store = Store();

        store.Begin(42, "YouTube", verificationRequested: true, thumbnailRequested: true, commentRequested: false);

        var entry = Assert.Single(store.List(42));
        Assert.Equal(SocialUploadJournalStatus.InProgress, entry.UploadStatus);
        Assert.Equal(SocialUploadJournalStatus.Pending, entry.VerificationStatus);
        Assert.Equal(SocialUploadJournalStatus.Pending, entry.ThumbnailStatus);
        Assert.Equal(SocialUploadJournalStatus.NotRequested, entry.CommentStatus);
        Assert.Equal("YouTube: uploading", entry.Display);
    }

    [Fact]
    public void CompletedUploadAndSteps_AreReportedComplete()
    {
        var store = Store();
        store.Begin(7, "Facebook", verificationRequested: true, thumbnailRequested: true, commentRequested: true);

        store.RecordUploadCompleted(7, "Facebook", "remote-7", "https://example.test/video/7");
        store.RecordStepCompleted(7, "Facebook", SocialUploadJournalStep.Verification);
        store.RecordStepCompleted(7, "Facebook", SocialUploadJournalStep.Thumbnail);
        store.RecordStepCompleted(7, "Facebook", SocialUploadJournalStep.Comment);

        var entry = Assert.Single(store.List(7));
        Assert.True(entry.IsComplete);
        Assert.Equal("remote-7", entry.RemoteId);
        Assert.Equal("Facebook: complete", entry.Display);
    }

    [Fact]
    public void Failure_SurvivesStoreRecreation_AndCanBeRetried()
    {
        var store = Store();
        store.Begin(9, "YouTube", verificationRequested: true, thumbnailRequested: false, commentRequested: false);
        store.RecordUploadCompleted(9, "YouTube", "video-9", "https://example.test/video/9");
        store.RecordFailure(9, "YouTube", SocialUploadJournalStep.Verification, "not visible yet");

        var restored = Store();
        var failed = Assert.Single(restored.List(9));
        Assert.True(failed.HasFailure);
        Assert.Equal(SocialUploadJournalStep.Verification, failed.EffectiveFailedStep);
        Assert.Equal("not visible yet", failed.LastError);

        restored.RecordStepStarted(9, "YouTube", SocialUploadJournalStep.Verification);
        restored.RecordStepCompleted(9, "YouTube", SocialUploadJournalStep.Verification);

        var completed = Assert.Single(restored.List(9));
        Assert.False(completed.HasFailure);
        Assert.True(completed.IsComplete);
    }

    [Fact]
    public void CompletingLatestFailure_RevealsAnEarlierFailedStep()
    {
        var store = Store();
        store.Begin(11, "YouTube", verificationRequested: true, thumbnailRequested: true, commentRequested: false);
        store.RecordUploadCompleted(11, "YouTube", "video-11", "https://example.test/video/11");
        store.RecordFailure(11, "YouTube", SocialUploadJournalStep.Verification, "verification failed");
        store.RecordFailure(11, "YouTube", SocialUploadJournalStep.Thumbnail, "thumbnail failed");

        store.RecordStepCompleted(11, "YouTube", SocialUploadJournalStep.Thumbnail);

        var entry = Assert.Single(store.List(11));
        Assert.True(entry.HasFailure);
        Assert.Equal(SocialUploadJournalStep.Verification, entry.EffectiveFailedStep);
    }

    [Fact]
    public void Summary_ShowsFailuresBeforeCompletedPlatforms()
    {
        var store = Store();
        store.Begin(13, "YouTube", false, false, false);
        store.RecordUploadCompleted(13, "YouTube", "yt-13", "https://example.test/yt/13");
        store.Begin(13, "Facebook", true, false, false);
        store.RecordUploadCompleted(13, "Facebook", "fb-13", "https://example.test/fb/13");
        store.RecordFailure(13, "Facebook", SocialUploadJournalStep.Verification, "failed");

        var summary = SocialUploadJournalSummary.Display(store.List(13));

        Assert.StartsWith("Facebook: verification failed", summary, StringComparison.Ordinal);
        Assert.Contains("YouTube: complete", summary, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private SocialUploadJournalStore Store() =>
        new(Path.Combine(_root, "data", "journal.db"));
}
