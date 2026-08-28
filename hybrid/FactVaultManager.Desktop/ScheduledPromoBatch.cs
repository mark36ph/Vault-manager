using System.Globalization;

namespace FactVaultManager.Desktop;

public sealed record ScheduledPromoPublishingTarget(
    int HistoryId,
    DateTimeOffset LongFormPublishAt,
    string Quiz,
    bool YouTube,
    bool Facebook);

public static class ScheduledPromoBatchPlanner
{
    public static IReadOnlyList<ScheduledReleaseReadinessRow> SelectMissingPromos(
        IEnumerable<ScheduledReleaseReadinessRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows
            .Where(row => string.Equals(row.Promo, "Missing", StringComparison.Ordinal))
            .OrderBy(row => row.PublishAt)
            .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.HistoryId)
            .ToList();
    }

    public static IReadOnlyList<ScheduledPromoPublishingTarget> SelectMissingScheduledUploads(
        IEnumerable<ScheduledReleaseReadinessRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows
            .Where(row => string.Equals(row.Promo, "Ready", StringComparison.Ordinal) &&
                          string.Equals(row.Tracking, "Ready", StringComparison.Ordinal))
            .Select(row => new ScheduledPromoPublishingTarget(
                row.HistoryId,
                row.PublishAt,
                row.Quiz,
                string.Equals(row.YouTubePromo, "Ready", StringComparison.Ordinal),
                string.Equals(row.FacebookPromo, "Ready", StringComparison.Ordinal)))
            .Where(target => target.YouTube || target.Facebook)
            .OrderBy(target => target.LongFormPublishAt)
            .ThenBy(target => target.Quiz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.HistoryId)
            .ToList();
    }

    public static IReadOnlyList<ScheduledReleaseReadinessRow> SelectMissingRelatedVideos(
        IEnumerable<ScheduledReleaseReadinessRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows
            .Where(row => string.Equals(row.YouTubePromo, "Uploaded", StringComparison.Ordinal) &&
                          string.Equals(row.RelatedVideo, "Needs setting", StringComparison.Ordinal))
            .OrderBy(row => row.PublishAt)
            .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.HistoryId)
            .ToList();
    }

    public static DateTimeOffset ResolvePromoPublishAt(
        DateTimeOffset longFormPublishAt,
        string? timeText,
        DateTimeOffset now)
    {
        if (!TimeOnly.TryParseExact(
                (timeText ?? "").Trim(),
                new[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            throw new ArgumentException("Enter the promo publication time as HH:mm, for example 18:00.");
        }

        var localDate = longFormPublishAt.LocalDateTime.Date.AddDays(1);
        var localDateTime = DateTime.SpecifyKind(localDate.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localDateTime))
            throw new ArgumentException("That promo publication time does not exist because the clocks change then. Choose another time.");
        var scheduled = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
        if (scheduled < now.AddMinutes(10))
            throw new ArgumentException("Schedule promo publication for at least 10 minutes from now.");
        return scheduled;
    }

    public static string Summary(int created, int skipped, int failed)
    {
        created = Math.Max(0, created);
        skipped = Math.Max(0, skipped);
        failed = Math.Max(0, failed);
        return $"Created {created.ToString("N0", CultureInfo.InvariantCulture)} • " +
               $"Skipped {skipped.ToString("N0", CultureInfo.InvariantCulture)} • " +
               $"Failed {failed.ToString("N0", CultureInfo.InvariantCulture)}";
    }

    public static string PublishingSummary(int youtube, int facebook, int failed)
    {
        youtube = Math.Max(0, youtube);
        facebook = Math.Max(0, facebook);
        failed = Math.Max(0, failed);
        return $"YouTube scheduled {youtube.ToString("N0", CultureInfo.InvariantCulture)} • " +
               $"Facebook scheduled {facebook.ToString("N0", CultureInfo.InvariantCulture)} • " +
               $"Failed {failed.ToString("N0", CultureInfo.InvariantCulture)}";
    }
}
