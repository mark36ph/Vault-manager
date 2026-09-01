using System.Globalization;

namespace FactVaultManager.Desktop;

public static class QuizContentLifecycleStage
{
    public const string NeedsAttention = "Needs attention";
    public const string Scheduled = "Scheduled";
    public const string Published = "Published";
    public const string Uploaded = "Uploaded";
    public const string Rendered = "Rendered";
    public const string Exported = "Exported";
}

public sealed record QuizContentLifecycleResult(
    string Stage,
    string NextAction,
    string Detail,
    bool NeedsAttention)
{
    public string FilterKey => NeedsAttention ? QuizContentLifecycleStage.NeedsAttention : Stage;
}

public static class QuizContentLifecycle
{
    public static readonly string[] Filters =
    [
        "All",
        QuizContentLifecycleStage.NeedsAttention,
        QuizContentLifecycleStage.Scheduled,
        QuizContentLifecycleStage.Published,
        QuizContentLifecycleStage.Uploaded,
        QuizContentLifecycleStage.Rendered,
        QuizContentLifecycleStage.Exported,
    ];

    public static QuizContentLifecycleResult Assess(
        QuizHistorySummary history,
        IReadOnlyList<PublicationStateEntry> publications,
        DateTimeOffset now,
        bool projectFolderExists,
        bool renderedVideoExists)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(publications);

        var issue = publications
            .Where(item => item.HasIssue)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
        if (issue is not null)
        {
            var content = string.Equals(issue.ContentKind, PublicationContentKind.Promo, StringComparison.Ordinal)
                ? "promo"
                : "quiz";
            var step = Humanize(issue.FailedStep);
            var action = step.Length > 0
                ? $"Fix {issue.Platform} {content}: {step}"
                : $"Fix {issue.Platform} {content} publication";
            var detail = issue.LastError.Trim().Length > 0 ? issue.LastError.Trim() : action;
            return new QuizContentLifecycleResult(
                QuizContentLifecycleStage.NeedsAttention,
                action,
                detail,
                true);
        }

        var quizPublications = publications
            .Where(item => string.Equals(item.ContentKind, PublicationContentKind.Quiz, StringComparison.Ordinal))
            .ToList();
        var scheduled = TryFutureSchedule(history.YouTubeScheduledFor, now) ||
                        quizPublications.Any(item =>
                            string.Equals(item.State, PublicationStateStatus.Scheduled, StringComparison.Ordinal) &&
                            IsFuture(item.ScheduledFor, now));
        if (scheduled)
        {
            if (!projectFolderExists)
            {
                return new QuizContentLifecycleResult(
                    QuizContentLifecycleStage.NeedsAttention,
                    "Resolve project folder",
                    "This quiz is scheduled but its local project folder cannot be found.",
                    true);
            }

            return new QuizContentLifecycleResult(
                QuizContentLifecycleStage.Scheduled,
                "Review release readiness",
                "The full quiz has a future publishing schedule.",
                false);
        }

        var explicitNonPublic = IsNonPublic(history.YouTubePrivacy) ||
                                quizPublications.Any(item => IsNonPublic(item.Visibility));
        var published = quizPublications.Any(item =>
                            string.Equals(item.State, PublicationStateStatus.Published, StringComparison.Ordinal)) ||
                        (history.PublishedOnYouTube && !explicitNonPublic);
        if (published)
        {
            return new QuizContentLifecycleResult(
                QuizContentLifecycleStage.Published,
                "Published",
                "The full quiz is published.",
                false);
        }

        var uploaded = history.PublishedOnYouTube ||
                       quizPublications.Any(item =>
                           item.HasRemotePublication ||
                           string.Equals(item.State, PublicationStateStatus.Uploaded, StringComparison.Ordinal) ||
                           string.Equals(item.State, PublicationStateStatus.InProgress, StringComparison.Ordinal));
        if (uploaded)
        {
            return new QuizContentLifecycleResult(
                QuizContentLifecycleStage.Uploaded,
                "Publish or set schedule",
                "The full quiz has been uploaded but is not currently considered published or scheduled.",
                false);
        }

        if (!projectFolderExists)
        {
            return new QuizContentLifecycleResult(
                QuizContentLifecycleStage.NeedsAttention,
                "Resolve project folder",
                "The local project folder cannot be found.",
                true);
        }

        if (renderedVideoExists)
        {
            return new QuizContentLifecycleResult(
                QuizContentLifecycleStage.Rendered,
                "Open Publish",
                "A completed video exists locally and is ready for publishing work.",
                false);
        }

        return new QuizContentLifecycleResult(
            QuizContentLifecycleStage.Exported,
            "Render final video",
            "Quiz history exists, but no completed rendered video was found in the project folder.",
            false);
    }

    public static bool MatchesFilter(QuizContentLifecycleResult lifecycle, string? filter)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        var value = (filter ?? "").Trim();
        return value.Length == 0 ||
               string.Equals(value, "All", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, lifecycle.FilterKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFutureSchedule(string? value, DateTimeOffset now) =>
        DateTimeOffset.TryParse(
            (value ?? "").Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var scheduled) &&
        scheduled > now;

    private static bool IsFuture(string? value, DateTimeOffset now) => TryFutureSchedule(value, now);

    private static bool IsNonPublic(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized is "private" or "unlisted";
    }

    private static string Humanize(string? value) =>
        (value ?? "").Trim().Replace('_', ' ').Replace('-', ' ');
}
