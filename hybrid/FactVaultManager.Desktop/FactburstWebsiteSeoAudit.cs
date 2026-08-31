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
    string Recommendations,
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
        var recommendations = new List<string>();
        var severe = false;

        if (!SlugPattern.IsMatch(slug))
        {
            issues.Add("Invalid or missing published slug");
            recommendations.Add("Use a lowercase, hyphenated slug containing only letters, numbers and hyphens. If this quiz is already published, repair the inventory carefully so existing links are not broken.");
            severe = true;
        }
        if (slug.Length > 0 && duplicateSlugs.Contains(slug))
        {
            issues.Add("Duplicate published slug");
            recommendations.Add("Resolve the duplicate inventory so every website quiz has one unique published slug before publishing or resyncing more copies.");
            severe = true;
        }
        if (visibleTitle.Length == 0)
        {
            issues.Add("Visible quiz title is missing");
            recommendations.Add("Add a clear, specific visible quiz title that describes the topic players will be tested on.");
            severe = true;
        }
        if (category.Length == 0)
        {
            issues.Add("Quiz category is missing");
            recommendations.Add("Assign the quiz to the most specific supported Factburst category so search metadata and catalogue grouping have clear context.");
            severe = true;
        }
        if (effective.SeoTitle.Length == 0 || effective.SeoDescription.Length == 0 ||
            effective.SocialTitle.Length == 0 || effective.SocialDescription.Length == 0)
        {
            issues.Add("Required search or social metadata is missing");
            recommendations.Add("Open Edit selected and fill all four search/social fields. Use the suggested values as a safe starting point, then make the copy specific to this quiz.");
            severe = true;
        }

        var hasDuplicateSeoTitle = effective.SeoTitle.Length > 0 && duplicateSeoTitles.Contains(Normalize(effective.SeoTitle));
        if (visibleTitle.Length > 0 && duplicateTitles.Contains(Normalize(visibleTitle)) && hasDuplicateSeoTitle)
        {
            issues.Add("Duplicate visible quiz title is also producing an indistinguishable search title");
            recommendations.Add("Use Fix SEO to give this quiz a distinct search title while keeping the published quiz title and URL unchanged, or rename the visible quiz manually if you also want the catalogue title to differ.");
        }
        if (hasDuplicateSeoTitle)
        {
            issues.Add("Duplicate SEO title");
            recommendations.Add($"Give this quiz a unique SEO title. Add the specific topic or challenge angle while keeping the title at {FactburstWebsiteSeoDefaults.RecommendedTitleLength} characters or fewer where practical.");
        }
        if (effective.SeoTitle.Length > FactburstWebsiteSeoDefaults.RecommendedTitleLength)
        {
            issues.Add($"SEO title is {effective.SeoTitle.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedTitleLength})");
            recommendations.Add($"Shorten the SEO title to {FactburstWebsiteSeoDefaults.RecommendedTitleLength} characters or fewer, keeping the quiz topic near the beginning so it is less likely to be truncated in search results.");
        }
        if (effective.SeoDescription.Length > 0 && effective.SeoDescription.Length < 70)
        {
            issues.Add($"SEO description is short ({effective.SeoDescription.Length} characters)");
            recommendations.Add("Expand the meta description to roughly 120–160 useful characters. Describe what the quiz covers and give players a reason to click without repeating the title word-for-word.");
        }
        if (effective.SeoDescription.Length > FactburstWebsiteSeoDefaults.RecommendedDescriptionLength)
        {
            issues.Add($"SEO description is {effective.SeoDescription.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedDescriptionLength})");
            recommendations.Add($"Trim the meta description to about 120–{FactburstWebsiteSeoDefaults.RecommendedDescriptionLength} characters so the important message appears before search engines truncate it.");
        }
        if (effective.SocialTitle.Length > FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength)
        {
            issues.Add($"Social title is {effective.SocialTitle.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength})");
            recommendations.Add($"Shorten the social title to {FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength} characters or fewer so it stays readable on shared preview cards.");
        }
        if (effective.SocialDescription.Length > FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength)
        {
            issues.Add($"Social description is {effective.SocialDescription.Length} characters (recommended ≤ {FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength})");
            recommendations.Add($"Trim the social description to {FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength} characters or fewer and lead with the strongest reason to play the quiz.");
        }

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
            recommendations.Count > 0
                ? string.Join(" ", recommendations.Distinct(StringComparer.OrdinalIgnoreCase))
                : "No change recommended. The current search and social metadata passes the audit checks.",
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
