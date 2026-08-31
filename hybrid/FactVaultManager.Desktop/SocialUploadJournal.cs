using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public static class SocialUploadJournalStep
{
    public const string Upload = "upload";
    public const string Verification = "verification";
    public const string Thumbnail = "thumbnail";
    public const string Comment = "comment";
}

public static class SocialUploadJournalStatus
{
    public const string NotRequested = "not_requested";
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

public sealed record SocialUploadJournalEntry(
    int HistoryId,
    string Platform,
    string RemoteId,
    string RemoteUrl,
    string UploadStatus,
    string VerificationStatus,
    string ThumbnailStatus,
    string CommentStatus,
    string FailedStep,
    string LastError,
    string UpdatedAt)
{
    public string EffectiveFailedStep => StepFailed(FailedStep)
        ? FailedStep
        : UploadStatus == SocialUploadJournalStatus.Failed
            ? SocialUploadJournalStep.Upload
            : VerificationStatus == SocialUploadJournalStatus.Failed
                ? SocialUploadJournalStep.Verification
                : ThumbnailStatus == SocialUploadJournalStatus.Failed
                    ? SocialUploadJournalStep.Thumbnail
                    : CommentStatus == SocialUploadJournalStatus.Failed
                        ? SocialUploadJournalStep.Comment
                        : "";
    public bool HasFailure => EffectiveFailedStep.Length > 0;
    public bool IsComplete =>
        UploadStatus == SocialUploadJournalStatus.Complete &&
        StepFinished(VerificationStatus) &&
        StepFinished(ThumbnailStatus) &&
        StepFinished(CommentStatus);

    public string Display
    {
        get
        {
            if (HasFailure) return $"{Platform}: {FriendlyStep(EffectiveFailedStep)} failed";
            if (UploadStatus == SocialUploadJournalStatus.InProgress) return $"{Platform}: uploading";
            if (UploadStatus != SocialUploadJournalStatus.Complete) return $"{Platform}: upload pending";
            if (StepWaiting(VerificationStatus)) return $"{Platform}: verification pending";
            if (StepWaiting(ThumbnailStatus)) return $"{Platform}: thumbnail pending";
            if (StepWaiting(CommentStatus)) return $"{Platform}: comment pending";
            return $"{Platform}: complete";
        }
    }

    private static bool StepFinished(string value) =>
        value is SocialUploadJournalStatus.Complete or SocialUploadJournalStatus.NotRequested;

    private static bool StepWaiting(string value) =>
        value is SocialUploadJournalStatus.Pending or SocialUploadJournalStatus.InProgress;

    private bool StepFailed(string step) => step switch
    {
        SocialUploadJournalStep.Upload => UploadStatus == SocialUploadJournalStatus.Failed,
        SocialUploadJournalStep.Verification => VerificationStatus == SocialUploadJournalStatus.Failed,
        SocialUploadJournalStep.Thumbnail => ThumbnailStatus == SocialUploadJournalStatus.Failed,
        SocialUploadJournalStep.Comment => CommentStatus == SocialUploadJournalStatus.Failed,
        _ => false,
    };

    private static string FriendlyStep(string value) => value switch
    {
        SocialUploadJournalStep.Verification => "verification",
        SocialUploadJournalStep.Thumbnail => "thumbnail",
        SocialUploadJournalStep.Comment => "comment",
        _ => "upload",
    };
}

public static class SocialUploadJournalSummary
{
    public static string Display(IEnumerable<SocialUploadJournalEntry> entries)
    {
        var items = entries.ToList();
        if (items.Count == 0) return "No activity";
        return string.Join(" • ", items
            .OrderByDescending(item => item.HasFailure)
            .ThenBy(item => PlatformOrder(item.Platform))
            .Select(item => item.Display));
    }

    private static int PlatformOrder(string platform) => platform switch
    {
        "YouTube" => 0,
        "Facebook" => 1,
        "Instagram" => 2,
        _ => 3,
    };
}

public sealed class SocialUploadJournalStore
{
    private readonly string _databasePath;

    public SocialUploadJournalStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
    }

    public void Begin(
        int historyId,
        string platform,
        bool verificationRequested,
        bool thumbnailRequested,
        bool commentRequested)
    {
        Validate(historyId, platform);
        EnsureSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO social_upload_journal (
                history_id, platform, remote_id, remote_url,
                upload_status, verification_status, thumbnail_status, comment_status,
                failed_step, last_error, updated_at)
            VALUES ($historyId, $platform, '', '', $upload, $verification, $thumbnail, $comment, '', '', $updatedAt)
            ON CONFLICT(history_id, platform) DO UPDATE SET
                remote_id = '', remote_url = '', upload_status = excluded.upload_status,
                verification_status = excluded.verification_status,
                thumbnail_status = excluded.thumbnail_status, comment_status = excluded.comment_status,
                failed_step = '', last_error = '', updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$platform", platform.Trim());
        command.Parameters.AddWithValue("$upload", SocialUploadJournalStatus.InProgress);
        command.Parameters.AddWithValue("$verification", Requested(verificationRequested));
        command.Parameters.AddWithValue("$thumbnail", Requested(thumbnailRequested));
        command.Parameters.AddWithValue("$comment", Requested(commentRequested));
        command.Parameters.AddWithValue("$updatedAt", Timestamp());
        command.ExecuteNonQuery();

        PublicationState.BeginAttempt(historyId, platform, PublicationContentKind.Quiz);
    }

    public void RecordUploadCompleted(int historyId, string platform, string remoteId, string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteId)) throw new ArgumentException("The remote video ID is missing.", nameof(remoteId));
        Update(historyId, platform,
            "upload_status = $status, remote_id = $remoteId, remote_url = $remoteUrl, failed_step = '', last_error = ''",
            command =>
            {
                command.Parameters.AddWithValue("$status", SocialUploadJournalStatus.Complete);
                command.Parameters.AddWithValue("$remoteId", remoteId.Trim());
                command.Parameters.AddWithValue("$remoteUrl", (remoteUrl ?? "").Trim());
            });

        PublicationState.RecordUploaded(
            historyId, platform, PublicationContentKind.Quiz, remoteId, remoteUrl);
    }

    public void RecordStepCompleted(int historyId, string platform, string step)
    {
        var column = StepColumn(step);
        Update(historyId, platform,
            $"{column} = $status, failed_step = CASE WHEN failed_step = $step THEN '' ELSE failed_step END, " +
            "last_error = CASE WHEN failed_step = $step THEN '' ELSE last_error END",
            command =>
            {
                command.Parameters.AddWithValue("$status", SocialUploadJournalStatus.Complete);
                command.Parameters.AddWithValue("$step", step);
            });

        PublicationState.ClearIssue(historyId, platform, PublicationContentKind.Quiz);
    }

    public void RecordStepStarted(int historyId, string platform, string step)
    {
        var column = StepColumn(step);
        Update(historyId, platform, $"{column} = $status",
            command => command.Parameters.AddWithValue("$status", SocialUploadJournalStatus.InProgress));
    }

    public void RecordFailure(int historyId, string platform, string step, string? error)
    {
        var column = StepColumn(step);
        var message = (error ?? "Unknown upload error").Trim();
        Update(historyId, platform,
            $"{column} = $status, failed_step = $step, last_error = $error",
            command =>
            {
                command.Parameters.AddWithValue("$status", SocialUploadJournalStatus.Failed);
                command.Parameters.AddWithValue("$step", step);
                command.Parameters.AddWithValue("$error", message);
            });

        PublicationState.RecordFailure(
            historyId, platform, PublicationContentKind.Quiz, step, message);
    }

    public IReadOnlyList<SocialUploadJournalEntry> List(int? historyId = null)
    {
        EnsureSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT history_id, platform, remote_id, remote_url,
                   upload_status, verification_status, thumbnail_status, comment_status,
                   failed_step, last_error, updated_at
            FROM social_upload_journal
            """ + (historyId is null ? "" : " WHERE history_id = $historyId") +
            " ORDER BY updated_at DESC, platform";
        if (historyId is not null) command.Parameters.AddWithValue("$historyId", historyId.Value);
        using var reader = command.ExecuteReader();
        var entries = new List<SocialUploadJournalEntry>();
        while (reader.Read())
        {
            entries.Add(new SocialUploadJournalEntry(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10)));
        }
        return entries;
    }

    public void Reset(int historyId, string platform)
    {
        Validate(historyId, platform);
        EnsureSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM social_upload_journal WHERE history_id = $historyId AND platform = $platform";
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$platform", platform.Trim());
        command.ExecuteNonQuery();

        PublicationState.Reset(historyId, platform, PublicationContentKind.Quiz);
    }

    private void Update(int historyId, string platform, string assignments, Action<SqliteCommand> addParameters)
    {
        Validate(historyId, platform);
        EnsureSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE social_upload_journal SET {assignments}, updated_at = $updatedAt " +
                              "WHERE history_id = $historyId AND platform = $platform";
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$platform", platform.Trim());
        command.Parameters.AddWithValue("$updatedAt", Timestamp());
        addParameters(command);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("The upload journal entry is missing. Start the upload again.");
    }

    private void EnsureSchema()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS social_upload_journal (
                history_id INTEGER NOT NULL,
                platform TEXT NOT NULL,
                remote_id TEXT NOT NULL DEFAULT '',
                remote_url TEXT NOT NULL DEFAULT '',
                upload_status TEXT NOT NULL DEFAULT 'pending',
                verification_status TEXT NOT NULL DEFAULT 'not_requested',
                thumbnail_status TEXT NOT NULL DEFAULT 'not_requested',
                comment_status TEXT NOT NULL DEFAULT 'not_requested',
                failed_step TEXT NOT NULL DEFAULT '',
                last_error TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (history_id, platform)
            )
            """;
        command.ExecuteNonQuery();
    }

    private PublicationStateStore PublicationState => new(_databasePath);

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static string StepColumn(string step) => step switch
    {
        SocialUploadJournalStep.Upload => "upload_status",
        SocialUploadJournalStep.Verification => "verification_status",
        SocialUploadJournalStep.Thumbnail => "thumbnail_status",
        SocialUploadJournalStep.Comment => "comment_status",
        _ => throw new ArgumentException("The upload journal step is not supported.", nameof(step)),
    };

    private static string Requested(bool requested) => requested
        ? SocialUploadJournalStatus.Pending
        : SocialUploadJournalStatus.NotRequested;

    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static void Validate(int historyId, string platform)
    {
        if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));
        if (string.IsNullOrWhiteSpace(platform)) throw new ArgumentException("The upload platform is missing.", nameof(platform));
    }
}
