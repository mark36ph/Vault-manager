using System.Globalization;

namespace FactVaultManager.Desktop;

public static class YouTubeScheduleIntegrity
{
    public static DateTimeOffset? ResolveScheduledFor(
        string? savedSchedule,
        string? livePrivacy,
        DateTimeOffset? livePublishAt,
        DateTimeOffset now)
    {
        if (livePublishAt.HasValue)
            return livePublishAt;

        if (string.Equals((livePrivacy ?? "").Trim(), "public", StringComparison.OrdinalIgnoreCase))
            return null;

        if (DateTimeOffset.TryParse(
                (savedSchedule ?? "").Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var saved) &&
            saved > now)
        {
            return saved;
        }

        return null;
    }
}

public sealed partial class DesktopDataService
{
    public IReadOnlyList<QuizHistorySummary> GetFutureScheduledYouTubeQuizHistory(
        DateTimeOffset now,
        int limit = 10_000)
    {
        EnsureQuizHistorySchema();
        limit = Math.Clamp(limit, 1, 50_000);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, created, question_count, categories, format,
                   question_seconds, shuffle_answers, project_folder,
                   series_name, episode_number, youtube_title, youtube_description,
                   youtube_hashtags, pinned_comment, published_on_youtube, youtube_url,
                   youtube_views, youtube_likes, youtube_upload_date,
                   published_on_facebook, facebook_url, facebook_views, facebook_reactions,
                   facebook_comments, facebook_shares, facebook_upload_date,
                   published_on_instagram, instagram_url, instagram_upload_date,
                   youtube_first_comment_id, facebook_first_comment_id,
                   youtube_privacy, youtube_scheduled_for, facebook_scheduled_for
            FROM quiz_history
            WHERE published_on_youtube = 1
              AND TRIM(youtube_scheduled_for) <> ''
              AND LOWER(TRIM(format)) <> '9:16'
            ORDER BY id DESC
            """;

        using var reader = command.ExecuteReader();
        var scheduled = new List<(QuizHistorySummary History, DateTimeOffset PublishAt)>();
        while (reader.Read())
        {
            var history = new QuizHistorySummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7) != 0,
                reader.GetString(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetInt32(15) != 0,
                reader.GetString(16),
                reader.GetInt64(17),
                reader.GetInt64(18),
                reader.GetString(19),
                reader.GetInt32(20) != 0,
                reader.GetString(21),
                reader.GetInt64(22),
                reader.GetInt64(23),
                reader.GetInt64(24),
                reader.GetInt64(25),
                reader.GetString(26),
                reader.GetInt32(27) != 0,
                reader.GetString(28),
                reader.GetString(29),
                reader.GetString(30),
                reader.GetString(31),
                reader.GetString(32),
                reader.GetString(33),
                reader.GetString(34));

            if (!DateTimeOffset.TryParse(
                    history.YouTubeScheduledFor,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var publishAt) ||
                publishAt <= now)
            {
                continue;
            }

            scheduled.Add((history, publishAt));
        }

        return scheduled
            .OrderBy(item => item.PublishAt)
            .ThenBy(item => item.History.Id)
            .Take(limit)
            .Select(item => item.History)
            .ToList();
    }
}
