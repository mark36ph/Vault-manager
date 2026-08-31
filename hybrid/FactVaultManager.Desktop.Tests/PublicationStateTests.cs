using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class PublicationStateTests
{
    [Fact]
    public void Reconcile_CombinesLegacyQuizHistoryAndDetailedJournal()
    {
        using var fixture = new PublicationFixture();
        var scheduled = DateTimeOffset.UtcNow.AddDays(2).ToString("O");
        fixture.InsertQuizHistory(
            7,
            youtubePublished: true,
            youtubeUrl: "https://www.youtube.com/watch?v=abc123",
            youtubePrivacy: "private",
            youtubeScheduledFor: scheduled,
            facebookPublished: true,
            facebookUrl: "https://www.facebook.com/reel/456");
        fixture.InsertJournal(
            7,
            PublicationPlatform.YouTube,
            "abc123",
            "https://www.youtube.com/watch?v=abc123",
            SocialUploadJournalStatus.Complete,
            SocialUploadJournalStep.Verification,
            "Verification temporarily failed");

        fixture.Store.Reconcile(7);
        var rows = fixture.Store.List(7);

        var youtube = Assert.Single(rows.Where(row =>
            row.Platform == PublicationPlatform.YouTube && row.ContentKind == PublicationContentKind.Quiz));
        Assert.Equal(PublicationStateStatus.Scheduled, youtube.State);
        Assert.Equal("abc123", youtube.RemoteId);
        Assert.Equal(SocialUploadJournalStep.Verification, youtube.FailedStep);
        Assert.Equal("Verification temporarily failed", youtube.LastError);
        Assert.Contains("needs attention", youtube.Display, StringComparison.OrdinalIgnoreCase);

        var facebook = Assert.Single(rows.Where(row =>
            row.Platform == PublicationPlatform.Facebook && row.ContentKind == PublicationContentKind.Quiz));
        Assert.Equal(PublicationStateStatus.Uploaded, facebook.State);
        Assert.Equal("https://www.facebook.com/reel/456", facebook.RemoteUrl);
    }

    [Fact]
    public void QuizAndPromo_PublicationsStayIndependentOnSamePlatform()
    {
        using var fixture = new PublicationFixture();

        fixture.Store.RecordPublished(
            12,
            PublicationPlatform.YouTube,
            PublicationContentKind.Quiz,
            remoteId: "full-video",
            remoteUrl: "https://www.youtube.com/watch?v=full-video",
            visibility: "public");
        fixture.Store.RecordUploaded(
            12,
            PublicationPlatform.YouTube,
            PublicationContentKind.Promo,
            remoteId: "promo-short",
            remoteUrl: "https://www.youtube.com/shorts/promo-short",
            visibility: "private");

        var rows = fixture.Store.List(12);
        Assert.Equal(2, rows.Count);
        var fullQuiz = Assert.Single(rows.Where(row => row.ContentKind == PublicationContentKind.Quiz));
        var promo = Assert.Single(rows.Where(row => row.ContentKind == PublicationContentKind.Promo));
        Assert.Equal("full-video", fullQuiz.RemoteId);
        Assert.Equal(PublicationStateStatus.Published, fullQuiz.State);
        Assert.Equal("promo-short", promo.RemoteId);
        Assert.Equal(PublicationStateStatus.Uploaded, promo.State);

        fixture.Store.Reset(12, PublicationPlatform.YouTube, PublicationContentKind.Promo);
        var remaining = Assert.Single(fixture.Store.List(12));
        Assert.Equal(PublicationContentKind.Quiz, remaining.ContentKind);
        Assert.Equal("full-video", remaining.RemoteId);
    }

    [Fact]
    public void FailureAfterRemoteUpload_PreservesPublicationAndRecordsIssue()
    {
        using var fixture = new PublicationFixture();
        fixture.Store.RecordUploaded(
            21,
            PublicationPlatform.Facebook,
            PublicationContentKind.Quiz,
            remoteId: "fb-21",
            remoteUrl: "https://www.facebook.com/reel/21");

        fixture.Store.RecordFailure(
            21,
            PublicationPlatform.Facebook,
            PublicationContentKind.Quiz,
            SocialUploadJournalStep.Comment,
            "Comment permission denied");

        var failed = Assert.IsType<PublicationStateEntry>(fixture.Store.Get(
            21, PublicationPlatform.Facebook, PublicationContentKind.Quiz));
        Assert.Equal(PublicationStateStatus.Uploaded, failed.State);
        Assert.Equal("fb-21", failed.RemoteId);
        Assert.Equal(SocialUploadJournalStep.Comment, failed.FailedStep);
        Assert.True(failed.HasIssue);

        fixture.Store.ClearIssue(21, PublicationPlatform.Facebook, PublicationContentKind.Quiz);
        var cleared = Assert.IsType<PublicationStateEntry>(fixture.Store.Get(
            21, PublicationPlatform.Facebook, PublicationContentKind.Quiz));
        Assert.Equal(PublicationStateStatus.Uploaded, cleared.State);
        Assert.False(cleared.HasIssue);
    }

    private sealed class PublicationFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _databasePath;

        public PublicationFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "FactVault-PublicationState-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _databasePath = Path.Combine(_root, "factvault.db");
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE quiz_history (
                    id INTEGER PRIMARY KEY,
                    published_on_youtube INTEGER NOT NULL DEFAULT 0,
                    youtube_url TEXT NOT NULL DEFAULT '',
                    youtube_upload_date TEXT NOT NULL DEFAULT '',
                    youtube_privacy TEXT NOT NULL DEFAULT '',
                    youtube_scheduled_for TEXT NOT NULL DEFAULT '',
                    published_on_facebook INTEGER NOT NULL DEFAULT 0,
                    facebook_url TEXT NOT NULL DEFAULT '',
                    facebook_upload_date TEXT NOT NULL DEFAULT '',
                    facebook_scheduled_for TEXT NOT NULL DEFAULT '',
                    published_on_instagram INTEGER NOT NULL DEFAULT 0,
                    instagram_url TEXT NOT NULL DEFAULT '',
                    instagram_upload_date TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE social_upload_journal (
                    history_id INTEGER NOT NULL,
                    platform TEXT NOT NULL,
                    remote_id TEXT NOT NULL DEFAULT '',
                    remote_url TEXT NOT NULL DEFAULT '',
                    upload_status TEXT NOT NULL DEFAULT 'pending',
                    failed_step TEXT NOT NULL DEFAULT '',
                    last_error TEXT NOT NULL DEFAULT '',
                    updated_at TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY(history_id, platform)
                );
                """;
            command.ExecuteNonQuery();
            Store = new PublicationStateStore(_databasePath);
        }

        public PublicationStateStore Store { get; }

        public void InsertQuizHistory(
            int id,
            bool youtubePublished = false,
            string youtubeUrl = "",
            string youtubePrivacy = "",
            string youtubeScheduledFor = "",
            bool facebookPublished = false,
            string facebookUrl = "")
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO quiz_history(
                    id, published_on_youtube, youtube_url, youtube_upload_date, youtube_privacy, youtube_scheduled_for,
                    published_on_facebook, facebook_url, facebook_upload_date, facebook_scheduled_for,
                    published_on_instagram, instagram_url, instagram_upload_date)
                VALUES(
                    $id, $youtubePublished, $youtubeUrl, '2026-08-31', $youtubePrivacy, $youtubeScheduledFor,
                    $facebookPublished, $facebookUrl, '2026-08-31', '',
                    0, '', '')
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$youtubePublished", youtubePublished ? 1 : 0);
            command.Parameters.AddWithValue("$youtubeUrl", youtubeUrl);
            command.Parameters.AddWithValue("$youtubePrivacy", youtubePrivacy);
            command.Parameters.AddWithValue("$youtubeScheduledFor", youtubeScheduledFor);
            command.Parameters.AddWithValue("$facebookPublished", facebookPublished ? 1 : 0);
            command.Parameters.AddWithValue("$facebookUrl", facebookUrl);
            command.ExecuteNonQuery();
        }

        public void InsertJournal(
            int historyId,
            string platform,
            string remoteId,
            string remoteUrl,
            string uploadStatus,
            string failedStep,
            string lastError)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO social_upload_journal(
                    history_id, platform, remote_id, remote_url, upload_status, failed_step, last_error, updated_at)
                VALUES($historyId, $platform, $remoteId, $remoteUrl, $uploadStatus, $failedStep, $lastError, $updatedAt)
                """;
            command.Parameters.AddWithValue("$historyId", historyId);
            command.Parameters.AddWithValue("$platform", platform);
            command.Parameters.AddWithValue("$remoteId", remoteId);
            command.Parameters.AddWithValue("$remoteUrl", remoteUrl);
            command.Parameters.AddWithValue("$uploadStatus", uploadStatus);
            command.Parameters.AddWithValue("$failedStep", failedStep);
            command.Parameters.AddWithValue("$lastError", lastError);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
