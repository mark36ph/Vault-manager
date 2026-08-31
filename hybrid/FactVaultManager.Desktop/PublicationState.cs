using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public static class PublicationPlatform
{
    public const string YouTube = "YouTube";
    public const string Facebook = "Facebook";
    public const string Instagram = "Instagram";
    public const string Website = "Website";
}

public static class PublicationContentKind
{
    public const string Quiz = "quiz";
    public const string Promo = "promo";
}

public static class PublicationStateStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Uploaded = "uploaded";
    public const string Scheduled = "scheduled";
    public const string Published = "published";
    public const string Failed = "failed";
}

public sealed record PublicationStateEntry(
    int HistoryId,
    string Platform,
    string ContentKind,
    string State,
    string RemoteId,
    string RemoteUrl,
    string Visibility,
    string ScheduledFor,
    string PublishedAt,
    string FailedStep,
    string LastError,
    string LastAttemptAt,
    string Source,
    string UpdatedAt)
{
    public bool HasIssue => FailedStep.Length > 0 || LastError.Length > 0 || State == PublicationStateStatus.Failed;
    public bool IsScheduled => State == PublicationStateStatus.Scheduled;
    public bool IsPublished => State == PublicationStateStatus.Published;
    public bool HasRemotePublication => RemoteId.Length > 0 || RemoteUrl.Length > 0 ||
                                        State is PublicationStateStatus.Uploaded or PublicationStateStatus.Scheduled or PublicationStateStatus.Published;

    public string Display
    {
        get
        {
            var label = State switch
            {
                PublicationStateStatus.InProgress => "Uploading",
                PublicationStateStatus.Uploaded => Visibility.Length > 0 ? FriendlyVisibility(Visibility) : "Uploaded",
                PublicationStateStatus.Scheduled => FormatSchedule(ScheduledFor),
                PublicationStateStatus.Published => "Published",
                PublicationStateStatus.Failed => "Failed",
                _ => "Pending",
            };
            return HasIssue && State != PublicationStateStatus.Failed
                ? $"{label} • needs attention"
                : label;
        }
    }

    private static string FriendlyVisibility(string value) => value.Trim().ToLowerInvariant() switch
    {
        "private" => "Private",
        "unlisted" => "Unlisted",
        "public" => "Published",
        _ => "Uploaded",
    };

    private static string FormatSchedule(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var scheduled))
            return $"Scheduled {scheduled.ToLocalTime():dd-MM-yyyy HH:mm}";
        return "Scheduled";
    }
}

public static class PublicationStateSummary
{
    public static string Display(IEnumerable<PublicationStateEntry> entries, string contentKind = PublicationContentKind.Quiz)
    {
        var items = entries
            .Where(item => string.Equals(item.ContentKind, contentKind, StringComparison.Ordinal))
            .OrderBy(item => PlatformOrder(item.Platform))
            .ToList();
        if (items.Count == 0) return "No publication activity";
        return string.Join(" • ", items.Select(item => $"{item.Platform}: {item.Display}"));
    }

    public static string PlatformDisplay(
        IEnumerable<PublicationStateEntry> entries,
        string platform,
        string contentKind,
        string emptyDisplay)
    {
        var item = entries.FirstOrDefault(entry =>
            string.Equals(entry.Platform, platform, StringComparison.Ordinal) &&
            string.Equals(entry.ContentKind, contentKind, StringComparison.Ordinal));
        return item?.Display ?? emptyDisplay;
    }

    private static int PlatformOrder(string platform) => platform switch
    {
        PublicationPlatform.YouTube => 0,
        PublicationPlatform.Facebook => 1,
        PublicationPlatform.Instagram => 2,
        PublicationPlatform.Website => 3,
        _ => 4,
    };
}

public sealed class PublicationStateStore
{
    private const string QuizHistoryMigration = "quiz-history-v1";
    private const string SocialJournalMigration = "social-upload-journal-v1";
    private readonly string _databasePath;

    public PublicationStateStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
    }

    public IReadOnlyList<PublicationStateEntry> List(int? historyId = null, string? contentKind = null)
    {
        EnsureSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string>();
        if (historyId is not null)
        {
            if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));
            filters.Add("history_id = $historyId");
            command.Parameters.AddWithValue("$historyId", historyId.Value);
        }
        if (!string.IsNullOrWhiteSpace(contentKind))
        {
            filters.Add("content_kind = $contentKind");
            command.Parameters.AddWithValue("$contentKind", NormalizeContentKind(contentKind));
        }

        command.CommandText = """
            SELECT history_id, platform, content_kind, state, remote_id, remote_url,
                   visibility, scheduled_for, published_at, failed_step, last_error,
                   last_attempt_at, source, updated_at
            FROM publication_state
            """ + (filters.Count == 0 ? "" : " WHERE " + string.Join(" AND ", filters)) +
            " ORDER BY history_id DESC, content_kind, platform";
        using var reader = command.ExecuteReader();
        var entries = new List<PublicationStateEntry>();
        while (reader.Read()) entries.Add(ReadEntry(reader));
        return entries;
    }

    public PublicationStateEntry? Get(int historyId, string platform, string contentKind)
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        return Get(connection, historyId, platform, contentKind);
    }

    public void BeginAttempt(int historyId, string platform, string contentKind)
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        var existing = Get(connection, historyId, platform, contentKind);
        var now = Timestamp();
        Upsert(connection, NewEntry(
            historyId, platform, contentKind,
            PublicationStateStatus.InProgress,
            existing?.RemoteId ?? "",
            existing?.RemoteUrl ?? "",
            existing?.Visibility ?? "",
            existing?.ScheduledFor ?? "",
            existing?.PublishedAt ?? "",
            "", "", now, "publication-state", now));
    }

    public void RecordUploaded(
        int historyId,
        string platform,
        string contentKind,
        string? remoteId,
        string? remoteUrl,
        string? visibility = null,
        DateTimeOffset? uploadedAt = null,
        string source = "publication-state")
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        var existing = Get(connection, historyId, platform, contentKind);
        var state = existing?.State is PublicationStateStatus.Scheduled or PublicationStateStatus.Published
            ? existing.State
            : PublicationStateStatus.Uploaded;
        var now = Timestamp();
        Upsert(connection, NewEntry(
            historyId, platform, contentKind, state,
            Prefer(remoteId, existing?.RemoteId), Prefer(remoteUrl, existing?.RemoteUrl),
            Prefer(visibility, existing?.Visibility), existing?.ScheduledFor ?? "",
            existing?.PublishedAt ?? NormalizeTimestamp(uploadedAt),
            "", "", now, source, now));
    }

    public void RecordScheduled(
        int historyId,
        string platform,
        string contentKind,
        DateTimeOffset scheduledFor,
        string? remoteId = null,
        string? remoteUrl = null,
        string? visibility = null,
        string source = "publication-state")
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        var existing = Get(connection, historyId, platform, contentKind);
        var now = Timestamp();
        Upsert(connection, NewEntry(
            historyId, platform, contentKind, PublicationStateStatus.Scheduled,
            Prefer(remoteId, existing?.RemoteId), Prefer(remoteUrl, existing?.RemoteUrl),
            Prefer(visibility, existing?.Visibility), NormalizeTimestamp(scheduledFor),
            existing?.PublishedAt ?? "", "", "", now, source, now));
    }

    public void RecordPublished(
        int historyId,
        string platform,
        string contentKind,
        string? remoteId = null,
        string? remoteUrl = null,
        DateTimeOffset? publishedAt = null,
        string? visibility = null,
        string source = "publication-state")
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        var existing = Get(connection, historyId, platform, contentKind);
        var now = Timestamp();
        Upsert(connection, NewEntry(
            historyId, platform, contentKind, PublicationStateStatus.Published,
            Prefer(remoteId, existing?.RemoteId), Prefer(remoteUrl, existing?.RemoteUrl),
            Prefer(visibility, existing?.Visibility), "",
            NormalizeTimestamp(publishedAt ?? DateTimeOffset.UtcNow),
            "", "", now, source, now));
    }

    public void RecordFailure(
        int historyId,
        string platform,
        string contentKind,
        string failedStep,
        string? error,
        string? remoteId = null,
        string? remoteUrl = null,
        string source = "publication-state")
    {
        Validate(historyId, platform, contentKind);
        failedStep = (failedStep ?? "").Trim();
        if (failedStep.Length == 0) failedStep = "upload";
        EnsureSchema();
        using var connection = OpenConnection();
        var existing = Get(connection, historyId, platform, contentKind);
        var hasRemote = Prefer(remoteId, existing?.RemoteId).Length > 0 || Prefer(remoteUrl, existing?.RemoteUrl).Length > 0;
        var state = hasRemote && existing?.State is PublicationStateStatus.Uploaded or PublicationStateStatus.Scheduled or PublicationStateStatus.Published
            ? existing.State
            : PublicationStateStatus.Failed;
        var now = Timestamp();
        Upsert(connection, NewEntry(
            historyId, platform, contentKind, state,
            Prefer(remoteId, existing?.RemoteId), Prefer(remoteUrl, existing?.RemoteUrl),
            existing?.Visibility ?? "", existing?.ScheduledFor ?? "", existing?.PublishedAt ?? "",
            failedStep, string.IsNullOrWhiteSpace(error) ? "Unknown publication error" : error.Trim(),
            now, source, now));
    }

    public void ClearIssue(int historyId, string platform, string contentKind)
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        var existing = Get(connection, historyId, platform, contentKind);
        if (existing is null) return;
        var state = existing.State == PublicationStateStatus.Failed && existing.HasRemotePublication
            ? PublicationStateStatus.Uploaded
            : existing.State;
        Upsert(connection, existing with
        {
            State = state,
            FailedStep = "",
            LastError = "",
            UpdatedAt = Timestamp(),
        });
    }

    public void Reset(int historyId, string platform, string contentKind)
    {
        Validate(historyId, platform, contentKind);
        EnsureSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM publication_state
            WHERE history_id = $historyId AND platform = $platform AND content_kind = $contentKind
            """;
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$platform", platform.Trim());
        command.Parameters.AddWithValue("$contentKind", NormalizeContentKind(contentKind));
        command.ExecuteNonQuery();
    }

    public void SyncQuizHistory(int historyId)
    {
        if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));
        EnsureSchema();
        using var connection = OpenConnection();
        if (!TableExists(connection, "quiz_history")) return;
        var row = ReadLegacyQuizHistory(connection, historyId);
        if (row is null) return;
        SyncLegacyQuizHistoryRow(connection, row);
    }

    public void SyncPromoMetadata(int historyId, string? projectFolder)
    {
        if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));
        var folder = (projectFolder ?? "").Trim();
        if (folder.Length == 0) return;

        string metadataPath;
        try
        {
            metadataPath = QuizPromoShortPaths.Metadata(Path.GetFullPath(folder));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }
        if (!File.Exists(metadataPath)) return;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not backfill promo publication state: {error.Message}");
            return;
        }
        if (root is null) return;

        SyncPromoNode(historyId, PublicationPlatform.YouTube, root["youtube_upload"] as JsonObject, "video_id");
        SyncPromoNode(historyId, PublicationPlatform.Facebook, root["facebook_upload"] as JsonObject, "video_id");
        SyncPromoNode(historyId, PublicationPlatform.Instagram, root["instagram_upload"] as JsonObject, "media_id");
    }

    private void SyncPromoNode(int historyId, string platform, JsonObject? node, string idKey)
    {
        if (node is null) return;
        var remoteId = NodeText(node, idKey);
        if (remoteId.Length == 0) return;
        var uploadedAt = ParseTimestamp(NodeText(node, "uploaded_at"));
        RecordUploaded(
            historyId,
            platform,
            PublicationContentKind.Promo,
            remoteId,
            NodeText(node, "url"),
            platform == PublicationPlatform.YouTube ? NodeText(node, "privacy") : "",
            uploadedAt,
            "promo-metadata-backfill");
    }

    private void EnsureSchema()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS publication_state (
                    history_id INTEGER NOT NULL,
                    platform TEXT NOT NULL,
                    content_kind TEXT NOT NULL,
                    state TEXT NOT NULL DEFAULT 'pending',
                    remote_id TEXT NOT NULL DEFAULT '',
                    remote_url TEXT NOT NULL DEFAULT '',
                    visibility TEXT NOT NULL DEFAULT '',
                    scheduled_for TEXT NOT NULL DEFAULT '',
                    published_at TEXT NOT NULL DEFAULT '',
                    failed_step TEXT NOT NULL DEFAULT '',
                    last_error TEXT NOT NULL DEFAULT '',
                    last_attempt_at TEXT NOT NULL DEFAULT '',
                    source TEXT NOT NULL DEFAULT '',
                    updated_at TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY(history_id, platform, content_kind)
                );
                CREATE INDEX IF NOT EXISTS ix_publication_state_status
                    ON publication_state(content_kind, state, platform);
                CREATE TABLE IF NOT EXISTS publication_state_migrations (
                    name TEXT PRIMARY KEY,
                    applied_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        ApplyMigration(connection, QuizHistoryMigration, BackfillQuizHistory);
        ApplyMigration(connection, SocialJournalMigration, BackfillSocialJournal);
    }

    private void ApplyMigration(SqliteConnection connection, string name, Action<SqliteConnection> migrate)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM publication_state_migrations WHERE name = $name LIMIT 1";
            check.Parameters.AddWithValue("$name", name);
            if (check.ExecuteScalar() is not null) return;
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            migrate(connection);
            using var mark = connection.CreateCommand();
            mark.Transaction = transaction;
            mark.CommandText = "INSERT INTO publication_state_migrations(name, applied_at) VALUES($name, $appliedAt)";
            mark.Parameters.AddWithValue("$name", name);
            mark.Parameters.AddWithValue("$appliedAt", Timestamp());
            mark.ExecuteNonQuery();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void BackfillQuizHistory(SqliteConnection connection)
    {
        if (!TableExists(connection, "quiz_history")) return;
        var rows = ReadAllLegacyQuizHistory(connection);
        foreach (var row in rows) SyncLegacyQuizHistoryRow(connection, row);
    }

    private void BackfillSocialJournal(SqliteConnection connection)
    {
        if (!TableExists(connection, "social_upload_journal")) return;
        var rows = new List<LegacyJournalRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT history_id, platform, remote_id, remote_url, upload_status,
                       failed_step, last_error, updated_at
                FROM social_upload_journal
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new LegacyJournalRow(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            }
        }

        foreach (var row in rows)
        {
            var existing = Get(connection, row.HistoryId, row.Platform, PublicationContentKind.Quiz);
            var state = row.UploadStatus switch
            {
                SocialUploadJournalStatus.InProgress => PublicationStateStatus.InProgress,
                SocialUploadJournalStatus.Complete when existing?.State is PublicationStateStatus.Scheduled or PublicationStateStatus.Published => existing.State,
                SocialUploadJournalStatus.Complete => PublicationStateStatus.Uploaded,
                SocialUploadJournalStatus.Failed when row.RemoteId.Length == 0 && row.RemoteUrl.Length == 0 => PublicationStateStatus.Failed,
                SocialUploadJournalStatus.Failed => existing?.State is PublicationStateStatus.Scheduled or PublicationStateStatus.Published
                    ? existing.State
                    : PublicationStateStatus.Uploaded,
                _ => existing?.State ?? PublicationStateStatus.Pending,
            };
            Upsert(connection, NewEntry(
                row.HistoryId, row.Platform, PublicationContentKind.Quiz, state,
                Prefer(row.RemoteId, existing?.RemoteId), Prefer(row.RemoteUrl, existing?.RemoteUrl),
                existing?.Visibility ?? "", existing?.ScheduledFor ?? "", existing?.PublishedAt ?? "",
                row.FailedStep, row.LastError, row.UpdatedAt, "social-upload-journal-backfill", row.UpdatedAt));
        }
    }

    private void SyncLegacyQuizHistoryRow(SqliteConnection connection, LegacyQuizHistoryRow row)
    {
        SyncLegacyPlatform(
            connection, row.Id, PublicationPlatform.YouTube, row.PublishedOnYouTube, row.YouTubeUrl,
            row.YouTubeUploadDate, row.YouTubePrivacy, row.YouTubeScheduledFor);
        SyncLegacyPlatform(
            connection, row.Id, PublicationPlatform.Facebook, row.PublishedOnFacebook, row.FacebookUrl,
            row.FacebookUploadDate, "", row.FacebookScheduledFor);
        SyncLegacyPlatform(
            connection, row.Id, PublicationPlatform.Instagram, row.PublishedOnInstagram, row.InstagramUrl,
            row.InstagramUploadDate, "", "");
    }

    private void SyncLegacyPlatform(
        SqliteConnection connection,
        int historyId,
        string platform,
        bool published,
        string remoteUrl,
        string uploadDate,
        string visibility,
        string scheduledFor)
    {
        var existing = Get(connection, historyId, platform, PublicationContentKind.Quiz);
        var schedule = ParseTimestamp(scheduledFor);
        if (!published && remoteUrl.Trim().Length == 0 && schedule is null)
        {
            if (existing is not null && existing.Source.Contains("backfill", StringComparison.OrdinalIgnoreCase))
                Delete(connection, historyId, platform, PublicationContentKind.Quiz);
            return;
        }

        string state;
        if (schedule is not null && schedule > DateTimeOffset.Now)
            state = PublicationStateStatus.Scheduled;
        else if (platform == PublicationPlatform.YouTube && published &&
                 string.Equals(visibility.Trim(), "public", StringComparison.OrdinalIgnoreCase))
            state = PublicationStateStatus.Published;
        else if (published && scheduledFor.Trim().Length > 0)
            state = PublicationStateStatus.Published;
        else
            state = PublicationStateStatus.Uploaded;

        Upsert(connection, NewEntry(
            historyId, platform, PublicationContentKind.Quiz, state,
            existing?.RemoteId ?? "", remoteUrl.Trim(), visibility.Trim(),
            schedule is not null && schedule > DateTimeOffset.Now ? NormalizeTimestamp(schedule.Value) : "",
            uploadDate.Trim(), existing?.FailedStep ?? "", existing?.LastError ?? "",
            existing?.LastAttemptAt ?? "", "quiz-history-backfill", Timestamp()));
    }

    private static LegacyQuizHistoryRow? ReadLegacyQuizHistory(SqliteConnection connection, int historyId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = LegacyQuizHistorySelect + " WHERE id = $historyId";
        command.Parameters.AddWithValue("$historyId", historyId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadLegacyQuizHistoryRow(reader) : null;
    }

    private static IReadOnlyList<LegacyQuizHistoryRow> ReadAllLegacyQuizHistory(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = LegacyQuizHistorySelect;
        using var reader = command.ExecuteReader();
        var rows = new List<LegacyQuizHistoryRow>();
        while (reader.Read()) rows.Add(ReadLegacyQuizHistoryRow(reader));
        return rows;
    }

    private const string LegacyQuizHistorySelect = """
        SELECT id,
               COALESCE(published_on_youtube, 0), COALESCE(youtube_url, ''), COALESCE(youtube_upload_date, ''),
               COALESCE(youtube_privacy, ''), COALESCE(youtube_scheduled_for, ''),
               COALESCE(published_on_facebook, 0), COALESCE(facebook_url, ''), COALESCE(facebook_upload_date, ''),
               COALESCE(facebook_scheduled_for, ''),
               COALESCE(published_on_instagram, 0), COALESCE(instagram_url, ''), COALESCE(instagram_upload_date, '')
        FROM quiz_history
        """;

    private static LegacyQuizHistoryRow ReadLegacyQuizHistoryRow(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetInt32(1) != 0, reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.GetInt32(6) != 0, reader.GetString(7), reader.GetString(8), reader.GetString(9),
        reader.GetInt32(10) != 0, reader.GetString(11), reader.GetString(12));

    private static PublicationStateEntry? Get(
        SqliteConnection connection,
        int historyId,
        string platform,
        string contentKind)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT history_id, platform, content_kind, state, remote_id, remote_url,
                   visibility, scheduled_for, published_at, failed_step, last_error,
                   last_attempt_at, source, updated_at
            FROM publication_state
            WHERE history_id = $historyId AND platform = $platform AND content_kind = $contentKind
            """;
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$platform", platform.Trim());
        command.Parameters.AddWithValue("$contentKind", NormalizeContentKind(contentKind));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    private static PublicationStateEntry ReadEntry(SqliteDataReader reader) => new(
        reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
        reader.GetString(12), reader.GetString(13));

    private static PublicationStateEntry NewEntry(
        int historyId,
        string platform,
        string contentKind,
        string state,
        string remoteId,
        string remoteUrl,
        string visibility,
        string scheduledFor,
        string publishedAt,
        string failedStep,
        string lastError,
        string lastAttemptAt,
        string source,
        string updatedAt) => new(
            historyId, platform.Trim(), NormalizeContentKind(contentKind), state,
            remoteId.Trim(), remoteUrl.Trim(), visibility.Trim(), scheduledFor.Trim(), publishedAt.Trim(),
            failedStep.Trim(), lastError.Trim(), lastAttemptAt.Trim(), source.Trim(), updatedAt.Trim());

    private static void Upsert(SqliteConnection connection, PublicationStateEntry entry)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO publication_state(
                history_id, platform, content_kind, state, remote_id, remote_url,
                visibility, scheduled_for, published_at, failed_step, last_error,
                last_attempt_at, source, updated_at)
            VALUES(
                $historyId, $platform, $contentKind, $state, $remoteId, $remoteUrl,
                $visibility, $scheduledFor, $publishedAt, $failedStep, $lastError,
                $lastAttemptAt, $source, $updatedAt)
            ON CONFLICT(history_id, platform, content_kind) DO UPDATE SET
                state = excluded.state,
                remote_id = CASE WHEN excluded.remote_id <> '' THEN excluded.remote_id ELSE publication_state.remote_id END,
                remote_url = CASE WHEN excluded.remote_url <> '' THEN excluded.remote_url ELSE publication_state.remote_url END,
                visibility = CASE WHEN excluded.visibility <> '' THEN excluded.visibility ELSE publication_state.visibility END,
                scheduled_for = excluded.scheduled_for,
                published_at = CASE WHEN excluded.published_at <> '' THEN excluded.published_at ELSE publication_state.published_at END,
                failed_step = excluded.failed_step,
                last_error = excluded.last_error,
                last_attempt_at = CASE WHEN excluded.last_attempt_at <> '' THEN excluded.last_attempt_at ELSE publication_state.last_attempt_at END,
                source = excluded.source,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$historyId", entry.HistoryId);
        command.Parameters.AddWithValue("$platform", entry.Platform);
        command.Parameters.AddWithValue("$contentKind", entry.ContentKind);
        command.Parameters.AddWithValue("$state", entry.State);
        command.Parameters.AddWithValue("$remoteId", entry.RemoteId);
        command.Parameters.AddWithValue("$remoteUrl", entry.RemoteUrl);
        command.Parameters.AddWithValue("$visibility", entry.Visibility);
        command.Parameters.AddWithValue("$scheduledFor", entry.ScheduledFor);
        command.Parameters.AddWithValue("$publishedAt", entry.PublishedAt);
        command.Parameters.AddWithValue("$failedStep", entry.FailedStep);
        command.Parameters.AddWithValue("$lastError", entry.LastError);
        command.Parameters.AddWithValue("$lastAttemptAt", entry.LastAttemptAt);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt);
        command.ExecuteNonQuery();
    }

    private static void Delete(SqliteConnection connection, int historyId, string platform, string contentKind)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM publication_state WHERE history_id=$historyId AND platform=$platform AND content_kind=$contentKind";
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$contentKind", NormalizeContentKind(contentKind));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static string NormalizeContentKind(string? contentKind)
    {
        var value = (contentKind ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            PublicationContentKind.Quiz => PublicationContentKind.Quiz,
            PublicationContentKind.Promo => PublicationContentKind.Promo,
            _ => throw new ArgumentException("The publication content kind is not supported.", nameof(contentKind)),
        };
    }

    private static void Validate(int historyId, string? platform, string? contentKind)
    {
        if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));
        if (string.IsNullOrWhiteSpace(platform)) throw new ArgumentException("The publication platform is missing.", nameof(platform));
        _ = NormalizeContentKind(contentKind);
    }

    private static string Prefer(string? value, string? fallback)
    {
        var candidate = (value ?? "").Trim();
        return candidate.Length > 0 ? candidate : (fallback ?? "").Trim();
    }

    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static string NormalizeTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string NormalizeTimestamp(DateTimeOffset? value) =>
        value is null ? "" : NormalizeTimestamp(value.Value);

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(
            (value ?? "").Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static string NodeText(JsonObject node, string key) =>
        node[key]?.GetValue<string>()?.Trim() ?? "";

    private sealed record LegacyQuizHistoryRow(
        int Id,
        bool PublishedOnYouTube,
        string YouTubeUrl,
        string YouTubeUploadDate,
        string YouTubePrivacy,
        string YouTubeScheduledFor,
        bool PublishedOnFacebook,
        string FacebookUrl,
        string FacebookUploadDate,
        string FacebookScheduledFor,
        bool PublishedOnInstagram,
        string InstagramUrl,
        string InstagramUploadDate);

    private sealed record LegacyJournalRow(
        int HistoryId,
        string Platform,
        string RemoteId,
        string RemoteUrl,
        string UploadStatus,
        string FailedStep,
        string LastError,
        string UpdatedAt);
}
