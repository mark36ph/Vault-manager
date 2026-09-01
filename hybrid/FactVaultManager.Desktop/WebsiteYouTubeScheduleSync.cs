using System.Globalization;

namespace FactVaultManager.Desktop;

public enum WebsiteYouTubeReleaseKind
{
    Scheduled,
    Live,
}

public sealed record WebsiteYouTubeReleasePlan(
    WebsiteYouTubeReleaseKind Kind,
    DateTimeOffset PublishAt)
{
    public bool IsScheduled => Kind == WebsiteYouTubeReleaseKind.Scheduled;
    public bool IsLive => Kind == WebsiteYouTubeReleaseKind.Live;
}

public static class WebsiteYouTubeSchedulePlanner
{
    public static WebsiteYouTubeReleasePlan? Plan(
        QuizHistorySummary history,
        DateTimeOffset now,
        DateTimeOffset? publicFallback = null)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (string.Equals(history.VideoType, "Short", StringComparison.OrdinalIgnoreCase) ||
            !history.PublishedOnYouTube ||
            string.IsNullOrWhiteSpace(history.YouTubeUrl))
        {
            return null;
        }

        if (Parse(history.YouTubeScheduledFor) is { } scheduled)
        {
            return new WebsiteYouTubeReleasePlan(
                scheduled > now ? WebsiteYouTubeReleaseKind.Scheduled : WebsiteYouTubeReleaseKind.Live,
                scheduled);
        }

        if (!string.Equals(history.YouTubePrivacy?.Trim(), "public", StringComparison.OrdinalIgnoreCase))
            return null;

        var publishAt = Parse(history.YouTubeUploadDate) ?? publicFallback ?? now;
        if (publishAt > now) publishAt = now;
        return new WebsiteYouTubeReleasePlan(WebsiteYouTubeReleaseKind.Live, publishAt);
    }

    public static DateTimeOffset ResolvePublishAtOrThrow(
        QuizHistorySummary history,
        DateTimeOffset requestedPublishAt,
        DateTimeOffset now)
    {
        var plan = Plan(history, now, requestedPublishAt);
        if (plan is not null) return plan.PublishAt;

        throw new InvalidDataException(
            "The website quiz is waiting for its long-form YouTube release. Schedule the YouTube video or make it public before publishing the website copy.");
    }

    public static bool PublishTimesMatch(
        string? websitePublishAt,
        DateTimeOffset expected,
        TimeSpan? tolerance = null)
    {
        var parsed = Parse(websitePublishAt);
        if (parsed is null) return false;
        var allowed = tolerance ?? TimeSpan.FromSeconds(1);
        return (parsed.Value.ToUniversalTime() - expected.ToUniversalTime()).Duration() <= allowed;
    }

    public static bool IsKnownNotPublic(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!history.PublishedOnYouTube) return true;
        if (Parse(history.YouTubeScheduledFor) is not null) return false;
        var privacy = (history.YouTubePrivacy ?? "").Trim().ToLowerInvariant();
        return privacy is "private" or "unlisted";
    }

    private static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}
