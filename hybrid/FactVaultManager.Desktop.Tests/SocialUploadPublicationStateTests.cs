using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class SocialUploadPublicationStateTests
{
    [Fact]
    public void JournalLifecycle_MirrorsIntoCanonicalQuizPublicationState()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVault-JournalMirror-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "factvault.db");
        try
        {
            var journal = new SocialUploadJournalStore(databasePath);
            var publication = new PublicationStateStore(databasePath);

            journal.Begin(
                31,
                PublicationPlatform.YouTube,
                verificationRequested: true,
                thumbnailRequested: false,
                commentRequested: true);

            var started = Assert.IsType<PublicationStateEntry>(publication.Get(
                31, PublicationPlatform.YouTube, PublicationContentKind.Quiz));
            Assert.Equal(PublicationStateStatus.InProgress, started.State);

            journal.RecordUploadCompleted(
                31,
                PublicationPlatform.YouTube,
                "video-31",
                "https://www.youtube.com/watch?v=video-31");

            var uploaded = Assert.IsType<PublicationStateEntry>(publication.Get(
                31, PublicationPlatform.YouTube, PublicationContentKind.Quiz));
            Assert.Equal(PublicationStateStatus.Uploaded, uploaded.State);
            Assert.Equal("video-31", uploaded.RemoteId);

            journal.RecordFailure(
                31,
                PublicationPlatform.YouTube,
                SocialUploadJournalStep.Verification,
                "Verification failed");

            var failed = Assert.IsType<PublicationStateEntry>(publication.Get(
                31, PublicationPlatform.YouTube, PublicationContentKind.Quiz));
            Assert.Equal(PublicationStateStatus.Uploaded, failed.State);
            Assert.Equal(SocialUploadJournalStep.Verification, failed.FailedStep);
            Assert.True(failed.HasIssue);

            journal.RecordStepCompleted(
                31,
                PublicationPlatform.YouTube,
                SocialUploadJournalStep.Verification);

            var recovered = Assert.IsType<PublicationStateEntry>(publication.Get(
                31, PublicationPlatform.YouTube, PublicationContentKind.Quiz));
            Assert.Equal(PublicationStateStatus.Uploaded, recovered.State);
            Assert.False(recovered.HasIssue);

            journal.Reset(31, PublicationPlatform.YouTube);
            Assert.Null(publication.Get(31, PublicationPlatform.YouTube, PublicationContentKind.Quiz));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
