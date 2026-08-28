using System.Globalization;

namespace FactVaultManager.Desktop;

public sealed record ScheduledReleaseReadinessRow(
    int HistoryId,
    DateTimeOffset PublishAt,
    string PublishAtDisplay,
    string Quiz,
    string Category,
    string FullQuiz,
    string Package,
    string Promo,
    string Tracking,
    string YouTubePromo,
    string FacebookPromo,
    string InstagramPromo,
    string RelatedVideo,
    string FirstComment,
    int ReadyCount,
    int TotalChecks,
    string Readiness,
    string NextAction,
    string ProjectFolder);

public static class ScheduledReleaseReadinessPlanner
{
    public const int CheckCount = 8;

    public static IReadOnlyList<ScheduledReleaseReadinessRow> Build(
        IEnumerable<QuizHistorySummary> histories,
        ISet<string>? trackerCampaigns,
        bool trackerConfigured,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(histories);

        var rows = new List<ScheduledReleaseReadinessRow>();
        foreach (var history in histories)
        {
            if (!history.PublishedOnYouTube ||
                !string.Equals(history.VideoType, "Video", StringComparison.Ordinal) ||
                !TryFutureSchedule(history.YouTubeScheduledFor, now, out var publishAt))
            {
                continue;
            }

            var packageReady = QuizYouTubePackaging.Exists(history.ProjectFolder);
            var promoReady = PromoExists(history.ProjectFolder);
            var youtubePromo = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
            var facebookPromo = QuizPromoShortSocialPublicationStore.LoadFacebook(history.ProjectFolder);
            var instagramPromo = QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder);
            var campaignSlug = FactburstLinkTrackerClient.CampaignSlug(history);
            var trackingReady = trackerCampaigns?.Contains(campaignSlug) == true;
            var tracking = trackerCampaigns is null
                ? trackerConfigured ? "Unavailable" : "Not configured"
                : trackingReady ? "Ready" : "Missing";

            var longVideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl) ?? "";
            var relatedReady = youtubePromo is not null &&
                               longVideoId.Length > 0 &&
                               SafeRelatedVideoIsSet(history.ProjectFolder, youtubePromo.VideoId, longVideoId);
            var related = youtubePromo is null
                ? "Waiting"
                : longVideoId.Length == 0
                    ? "Invalid full link"
                    : relatedReady ? "Set" : "Needs setting";

            var firstCommentReady = history.PinnedComment.Trim().Length > 0;
            var readyCount = 1 +
                             Bool(packageReady) +
                             Bool(promoReady) +
                             Bool(trackingReady) +
                             Bool(youtubePromo is not null) +
                             Bool(facebookPromo is not null) +
                             Bool(relatedReady) +
                             Bool(firstCommentReady);

            var nextAction = NextAction(
                packageReady,
                promoReady,
                tracking,
                youtubePromo is not null,
                facebookPromo is not null,
                relatedReady,
                firstCommentReady);

            rows.Add(new ScheduledReleaseReadinessRow(
                history.Id,
                publishAt,
                publishAt.LocalDateTime.ToString("ddd dd MMM • HH:mm", CultureInfo.InvariantCulture),
                history.UploadTitleDisplay,
                history.AnalyticsCategory,
                "Scheduled",
                packageReady ? "Ready" : "Missing",
                promoReady ? "Ready" : "Missing",
                tracking,
                youtubePromo is not null ? "Uploaded" : promoReady ? "Ready" : "Missing",
                facebookPromo is not null ? "Uploaded" : promoReady ? "Ready" : "Missing",
                instagramPromo is not null ? "Uploaded" : promoReady ? "Release day" : "Waiting",
                related,
                firstCommentReady ? "Prepared" : "Missing",
                readyCount,
                CheckCount,
                ReadinessLabel(readyCount, CheckCount),
                nextAction,
                history.ProjectFolder));
        }

        return rows
            .OrderBy(row => row.PublishAt)
            .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.HistoryId)
            .ToList();
    }

    public static bool TryFutureSchedule(
        string? value,
        DateTimeOffset now,
        out DateTimeOffset scheduled)
    {
        if (DateTimeOffset.TryParse(
                (value ?? "").Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out scheduled) && scheduled > now)
        {
            return true;
        }

        scheduled = default;
        return false;
    }

    public static string ReadinessLabel(int readyCount, int totalChecks)
    {
        if (totalChecks <= 0) return "—";
        var ready = Math.Clamp(readyCount, 0, totalChecks);
        var label = ready == totalChecks
            ? "Ready"
            : ready >= Math.Max(1, totalChecks - 2)
                ? "Nearly ready"
                : "Needs work";
        return $"{ready}/{totalChecks} • {label}";
    }

    private static string NextAction(
        bool packageReady,
        bool promoReady,
        string tracking,
        bool youtubePromo,
        bool facebookPromo,
        bool relatedReady,
        bool firstCommentReady)
    {
        if (!packageReady) return "Create YouTube package";
        if (!promoReady) return "Create promo Short";
        if (string.Equals(tracking, "Missing", StringComparison.Ordinal)) return "Create tracking link";
        if (string.Equals(tracking, "Unavailable", StringComparison.Ordinal)) return "Check tracker connection";
        if (string.Equals(tracking, "Not configured", StringComparison.Ordinal)) return "Configure Link Tracker";
        if (!youtubePromo || !facebookPromo) return "Schedule promo";
        if (!relatedReady) return "Set Related video";
        if (!firstCommentReady) return "Prepare first comment";
        return "Ready for release";
    }

    private static bool PromoExists(string projectFolder)
    {
        try { return QuizPromoShortPaths.FindExisting(projectFolder) is not null; }
        catch { return false; }
    }

    private static bool SafeRelatedVideoIsSet(string projectFolder, string promoVideoId, string longVideoId)
    {
        try { return QuizPromoRelatedVideoStore.IsSetFor(projectFolder, promoVideoId, longVideoId); }
        catch { return false; }
    }

    private static int Bool(bool value) => value ? 1 : 0;
}
