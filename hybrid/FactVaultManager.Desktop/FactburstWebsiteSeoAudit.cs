using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public enum WebsiteSeoAuditSeverity
{
    Ready = 0,
    Warning = 1,
    NeedsAttention = 2,
}

public sealed record WebsiteSeoAuditRow(
    WebsiteSeoAuditSeverity Severity,
    string Status,
    string Quiz,
    string Category,
    string Mode,
    string SeoTitle,
    string Issues,
    string Slug,
    FactburstWebsiteSeoQuiz Source);

public sealed record WebsiteSeoAuditSummary(
    int Total,
    int Ready,
    int Warnings,
    int NeedsAttention,
    int Custom,
    int Automatic);

public static class FactburstWebsiteSeoAudit
{
    private static readonly Regex SlugPattern = new("^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.Compiled);

    public static IReadOnlyList<WebsiteSeoAuditRow> Build(IEnumerable<FactburstWebsiteSeoQuiz> quizzes)
    {
        ArgumentNullException.ThrowIfNull(quizzes);
        var items = quizzes.Where(quiz => quiz is not null).ToList();

        var duplicateSlugs = DuplicateKeys(items, quiz => Normalize(quiz.Slug));
        var duplicateTitles = DuplicateKeys(items, quiz => Normalize(quiz.Title));
        var duplicateSeoTitles = DuplicateKeys(items, quiz => Normalize(FactburstWebsiteSeoDefaults.Effective(quiz).SeoTitle));

        var rows = items.Select(quiz => BuildRow(quiz, duplicateSlugs, duplicateTitles, duplicateSeoTitles)).ToList();
        return rows
            .OrderByDescending(row => row.Severity)
            .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static WebsiteSeoAuditSummary Summarize(IEnumerable<WebsiteSeoAuditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var items = rows.ToList();
        return new WebsiteSeoAuditSummary(
            items.Count,
            items.Count(row => row.Severity == WebsiteSeoAuditSeverity.Ready),
            items.Count(row => row.Severity == WebsiteSeoAuditSeverity.Warning),
            items.Count(row => row.Severity == WebsiteSeoAuditSeverity.NeedsAttention),
            items.Count(row => string.Equals(row.Mode, "Custom", StringComparison.Ordinal)),
            items.Count(row => string.Equals(row.Mode, "Automatic", StringComparison.Ordinal)));
    }

    private static WebsiteSeoAuditRow BuildRow(
        FactburstWebsiteSeoQuiz quiz,
        IReadOnlySet<string> duplicateSlugs,
        IReadOnlySet<string> duplicateTitles,
        IReadOnlySet<string> duplicateSeoTitles)
    {
        var slug = Normalize(quiz.Slug);
        var visibleTitle = Compact(quiz.Title);
        var category = Compact(quiz.Category);
        var effective = FactburstWebsiteSeoDefaults.Effective(quiz);
        var issues = new List<string>();
        var severe = false;

        if (!SlugPattern.IsMatch(slug))
        {
            issues.Add("Invalid or missing published slug");
            severe = true;
        }
        if (slug.Length > 0 && duplicateSlugs.Contains(slug))
        {
            issues.Add("Duplicate published slug");
            severe = true;
        }
        if (visibleTitle.Length == 0)
        {
            issues.Add("Visible quiz title is missing");
            severe = true;
        }
        if (category.Length == 0)
        {
            issues.Add("Quiz category is missing");
            severe = true;
        }
        if (effective.SeoTitle.Length == 0 || effective.SeoDescription.Length == 0 ||
            effective.SocialTitle.Length == 0 || effective.SocialDescription.Length == 0)
        {
            issues.Add("Required search or social metadata is missing");
            severe = true;
        }

        if (visibleTitle.Length > 0 && duplicateTitles.Contains(Normalize(visibleTitle)))
            issues.Add("Duplicate visible quiz title");
        if (effective.SeoTitle.Length > 0 && duplicateSeoTitles.Contains(Normalize(effective.SeoTitle)))
            issues.Add("Duplicate SEO title");
        if (effective.SeoTitle.Length > FactburstWebsiteSeoDefaults.RecommendedTitleLength)
            issues.Add($"SEO title is {effective.SeoTitle.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedTitleLength})");
        if (effective.SeoDescription.Length > 0 && effective.SeoDescription.Length < 70)
            issues.Add($"SEO description is short ({effective.SeoDescription.Length} characters)");
        if (effective.SeoDescription.Length > FactburstWebsiteSeoDefaults.RecommendedDescriptionLength)
            issues.Add($"SEO description is {effective.SeoDescription.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedDescriptionLength})");
        if (effective.SocialTitle.Length > FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength)
            issues.Add($"Social title is {effective.SocialTitle.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength})");
        if (effective.SocialDescription.Length > FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength)
            issues.Add($"Social description is {effective.SocialDescription.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength})");

        var severity = severe
            ? WebsiteSeoAuditSeverity.NeedsAttention
            : issues.Count > 0
                ? WebsiteSeoAuditSeverity.Warning
                : WebsiteSeoAuditSeverity.Ready;
        var mode = HasCustomMetadata(quiz) ? "Custom" : "Automatic";
        return new WebsiteSeoAuditRow(
            severity,
            severity switch
            {
                WebsiteSeoAuditSeverity.NeedsAttention => "Needs attention",
                WebsiteSeoAuditSeverity.Warning => "Warning",
                _ => "Ready",
            },
            visibleTitle.Length > 0 ? visibleTitle : "Untitled quiz",
            category.Length > 0 ? category : "—",
            mode,
            effective.SeoTitle,
            issues.Count > 0 ? string.Join(" • ", issues) : "SEO looks ready",
            slug,
            quiz);
    }

    private static HashSet<string> DuplicateKeys(
        IEnumerable<FactburstWebsiteSeoQuiz> quizzes,
        Func<FactburstWebsiteSeoQuiz, string> selector)
    {
        return quizzes
            .Select(selector)
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasCustomMetadata(FactburstWebsiteSeoQuiz quiz) =>
        !string.IsNullOrWhiteSpace(quiz.SeoTitle) ||
        !string.IsNullOrWhiteSpace(quiz.SeoDescription) ||
        !string.IsNullOrWhiteSpace(quiz.SocialTitle) ||
        !string.IsNullOrWhiteSpace(quiz.SocialDescription);

    private static string Normalize(string? value) => Compact(value).ToLowerInvariant();

    private static string Compact(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
