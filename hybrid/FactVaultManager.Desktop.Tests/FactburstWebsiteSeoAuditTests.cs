using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteSeoAuditTests
{
    [Fact]
    public void Build_marks_healthy_automatic_metadata_ready()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("space-quiz-1", "Space Quiz 1", "Space", ""),
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(WebsiteSeoAuditSeverity.Ready, row.Severity);
        Assert.Equal("Ready", row.Status);
        Assert.Equal("Automatic", row.Mode);
        Assert.Equal("SEO looks ready", row.Issues);
        Assert.Contains("No change recommended", row.Recommendations);
    }

    [Fact]
    public void Build_marks_saved_metadata_custom()
    {
        var quiz = Quiz(
            "history-quiz-1",
            "History Quiz 1",
            "History",
            "",
            seoTitle: "History Challenge | Factburst Quiz",
            seoDescription: "Take this ten-question history challenge and test your knowledge of important people, places and events from across the past.",
            socialTitle: "History Challenge",
            socialDescription: "Ten history questions from Factburst Quiz. Can you get every answer right and beat your previous score?");

        var row = Assert.Single(FactburstWebsiteSeoAudit.Build([quiz]));
        Assert.Equal("Custom", row.Mode);
        Assert.Equal(WebsiteSeoAuditSeverity.Ready, row.Severity);
    }

    [Fact]
    public void Build_flags_both_quizzes_when_effective_seo_title_is_duplicated()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("science-one", "Science One", "Science", "", seoTitle: "Same SEO Title"),
            Quiz("science-two", "Science Two", "Science", "", seoTitle: "Same SEO Title"),
        ]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(WebsiteSeoAuditSeverity.Warning, row.Severity));
        Assert.All(rows, row => Assert.Contains("Duplicate SEO title", row.Issues));
        Assert.All(rows, row => Assert.Contains("unique SEO title", row.Recommendations));
    }

    [Fact]
    public void Build_flags_duplicate_visible_titles_for_every_match()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("space-one", "Space Challenge", "Space", ""),
            Quiz("space-two", "Space Challenge", "Space", ""),
        ]);

        Assert.All(rows, row => Assert.Contains("Duplicate visible quiz title", row.Issues));
        Assert.All(rows, row => Assert.Contains("distinct search title", row.Recommendations));
    }

    [Fact]
    public void Build_treats_duplicate_slug_as_needs_attention()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("same-slug", "Quiz One", "Science", ""),
            Quiz("same-slug", "Quiz Two", "History", ""),
        ]);

        Assert.All(rows, row => Assert.Equal(WebsiteSeoAuditSeverity.NeedsAttention, row.Severity));
        Assert.All(rows, row => Assert.Contains("Duplicate published slug", row.Issues));
        Assert.All(rows, row => Assert.Contains("unique published slug", row.Recommendations));
    }

    [Fact]
    public void Build_checks_search_and_social_length_guidance()
    {
        var quiz = Quiz(
            "long-copy",
            "Long Copy Quiz",
            "General Knowledge",
            "",
            seoTitle: new string('T', 66),
            seoDescription: "Short description",
            socialTitle: new string('S', 101),
            socialDescription: new string('D', 201));

        var row = Assert.Single(FactburstWebsiteSeoAudit.Build([quiz]));
        Assert.Equal(WebsiteSeoAuditSeverity.Warning, row.Severity);
        Assert.Contains("SEO title is 66", row.Issues);
        Assert.Contains("SEO description is short", row.Issues);
        Assert.Contains("Social title is 101", row.Issues);
        Assert.Contains("Social description is 201", row.Issues);
        Assert.Contains("Shorten the SEO title", row.Recommendations);
        Assert.Contains("Expand the meta description", row.Recommendations);
        Assert.Contains("Shorten the social title", row.Recommendations);
        Assert.Contains("Trim the social description", row.Recommendations);
    }

    [Fact]
    public void Build_every_non_ready_row_has_reason_and_recommendation()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("warning-one", "Warning One", "History", "", seoTitle: new string('T', 66)),
            Quiz("BAD SLUG", "Broken One", "Science", ""),
        ]);

        Assert.All(rows, row =>
        {
            Assert.NotEqual(WebsiteSeoAuditSeverity.Ready, row.Severity);
            Assert.False(string.IsNullOrWhiteSpace(row.Issues));
            Assert.False(string.IsNullOrWhiteSpace(row.Recommendations));
            Assert.DoesNotContain("No change recommended", row.Recommendations);
        });
    }

    [Fact]
    public void Build_orders_structural_problems_before_warnings_and_ready_rows()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("ready-quiz", "Ready Quiz", "Space", ""),
            Quiz("warning-quiz", "Warning Quiz", "Space", "", seoTitle: new string('T', 66)),
            Quiz("BAD SLUG", "Broken Quiz", "Space", ""),
        ]);

        Assert.Equal(WebsiteSeoAuditSeverity.NeedsAttention, rows[0].Severity);
        Assert.Equal(WebsiteSeoAuditSeverity.Warning, rows[1].Severity);
        Assert.Equal(WebsiteSeoAuditSeverity.Ready, rows[2].Severity);
    }

    [Fact]
    public void Summarize_counts_catalogue_health_and_metadata_modes()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("ready-one", "Ready One", "Space", ""),
            Quiz("warning-one", "Warning One", "History", "", seoTitle: new string('T', 66)),
            Quiz("BAD SLUG", "Broken One", "Science", ""),
        ]);

        var summary = FactburstWebsiteSeoAudit.Summarize(rows);

        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Ready);
        Assert.Equal(1, summary.Warnings);
        Assert.Equal(1, summary.NeedsAttention);
        Assert.Equal(1, summary.Custom);
        Assert.Equal(2, summary.Automatic);
    }

    private static FactburstWebsiteSeoQuiz Quiz(
        string slug,
        string title,
        string category,
        string description,
        string seoTitle = "",
        string seoDescription = "",
        string socialTitle = "",
        string socialDescription = "") => new(
            slug,
            title,
            category,
            description,
            seoTitle,
            seoDescription,
            socialTitle,
            socialDescription,
            "published",
            "2026-08-31T10:00:00Z",
            "2026-08-31T10:00:00Z",
            10);
}
