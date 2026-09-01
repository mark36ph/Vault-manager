using System.Globalization;

namespace FactVaultManager.Desktop;

public sealed record InstagramPromoFollowupNeed(
    int HistoryId,
    string Quiz,
    string ProjectFolder,
    DateTimeOffset YouTubePublishedAt,
    string Detail,
    bool PromoFileReady,
    bool HasFailure);

public static class InstagramPromoFollowupPlanner
{
    public static readonly TimeSpan AutomaticWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan NeedsYouWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(30);

    public static DateTimeOffset? ReleaseAt(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return DateTimeOffset.TryParse(
            history.YouTubeScheduledFor.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var scheduled)
            ? scheduled
            : null;
    }

    public static bool IsWithinWindow(
        QuizHistorySummary history,
        DateTimeOffset now,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(history);
        var release = ReleaseAt(history);
        return string.Equals(history.VideoType, "Video", StringComparison.Ordinal) &&
               history.PublishedOnYouTube &&
               release is not null &&
               release <= now &&
               release >= now - window;
    }

    public static bool RetryAllowed(PublicationStateEntry? publication, DateTimeOffset now)
    {
        if (publication is null || !publication.HasIssue || publication.LastAttemptAt.Trim().Length == 0)
            return true;
        return !DateTimeOffset.TryParse(
                   publication.LastAttemptAt,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var lastAttempt) ||
               now - lastAttempt >= RetryCooldown;
    }

    public static bool IsVerifiedYouTubePublic(
        int historyId,
        FactburstFullAutopilotState state,
        IEnumerable<PublicationStateEntry> publications)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(publications);

        if (state.PostReleaseAudits
            .Where(record => record.HistoryId == historyId)
            .OrderByDescending(record => record.CheckedAtUtc)
            .FirstOrDefault()?.IsPublic == true)
        {
            return true;
        }

        return publications.Any(entry =>
            entry.HistoryId == historyId &&
            string.Equals(entry.ContentKind, PublicationContentKind.Quiz, StringComparison.Ordinal) &&
            string.Equals(entry.Platform, PublicationPlatform.YouTube, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.State, PublicationStateStatus.Published, StringComparison.Ordinal) &&
            (string.Equals(entry.Source, "autopilot-youtube-public", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.Visibility, "public", StringComparison.OrdinalIgnoreCase)));
    }
}
