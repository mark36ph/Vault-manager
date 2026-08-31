using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteSeoAutoFixTests
{
    [Fact]
    public void Create_fixes_duplicate_seo_title_with_unique_search_title()
    {
        var quizzes = new List<FactburstWebsiteSeoQuiz>
        {
            Quiz("science-alpha", "Science Alpha", "Science", seoTitle: "Science Challenge | Factburst Quiz"),
            Quiz("science-beta", "Science Beta", "Science", seoTitle: "Science Challenge | Factburst Quiz"),
        };
        var row = FactburstWebsiteSeoAudit.Build(quizzes).First(item => item.Slug == "science-alpha");

        var proposal = FactburstWebsiteSeoAutoFix.Create(row, quizzes);

        Assert.True(proposal.CanApply);
        Assert.Contains(proposal.Changes, change => change.Field == "SEO title");
        Assert.NotEqual(proposal.Before.SeoTitle, proposal.After.SeoTitle);
        Assert.True(proposal.After.SeoTitle.Length <= FactburstWebsiteSeoDefaults.RecommendedTitleLength);
        AssertReadyAfterApply(row, proposal, quizzes);
    }

    [Fact]
    public void Create_fixes_duplicate_visible_titles_by_distinguishing_search_title()
    {
        var quizzes = new List<FactburstWebsiteSeoQuiz>
        {
            Quiz("space-challenge-one", "Space Challenge", "Space"),
            Quiz("space-challenge-two", "Space Challenge", "Space"),
        };
        var row = FactburstWebsiteSeoAudit.Build(quizzes).First(item => item.Slug == "space-challenge-one");
        Assert.Contains("Duplicate visible quiz title", row.Issues);

        var proposal = FactburstWebsiteSeoAutoFix.Create(row, quizzes);

        Assert.True(proposal.CanApply);
        Assert.Contains(proposal.Changes, change => change.Field == "SEO title");
        AssertReadyAfterApply(row, proposal, quizzes);
    }

    [Fact]
    public void Create_expands_short_meta_description()
    {
        var quiz = Quiz(
            "history-short-description",
            "History Challenge",
            "History",
            seoDescription: "A short history quiz.");
        var quizzes = new List<FactburstWebsiteSeoQuiz> { quiz };
        var row = Assert.Single(FactburstWebsiteSeoAudit.Build(quizzes));
        Assert.Equal(WebsiteSeoAuditSeverity.Warning, row.Severity);

        var proposal = FactburstWebsiteSeoAutoFix.Create(row, quizzes);

        Assert.True(proposal.CanApply);
        Assert.Contains(proposal.Changes, change => change.Field == "Meta description");
        Assert.InRange(proposal.After.SeoDescription.Length, 70, FactburstWebsiteSeoDefaults.RecommendedDescriptionLength);
        AssertReadyAfterApply(row, proposal, quizzes);
    }

    [Fact]
    public void Create_replaces_overlong_search_and_social_copy()
    {
        var quiz = Quiz(
            "technology-long-copy",
            "Technology Challenge",
            "Technology",
            seoTitle: new string('T', 90),
            seoDescription: new string('D', 220),
            socialTitle: new string('S', 130),
            socialDescription: new string('C', 240));
        var quizzes = new List<FactburstWebsiteSeoQuiz> { quiz };
        var row = Assert.Single(FactburstWebsiteSeoAudit.Build(quizzes));

        var proposal = FactburstWebsiteSeoAutoFix.Create(row, quizzes);

        Assert.True(proposal.CanApply);
        Assert.Equal(4, proposal.Changes.Count);
        Assert.True(proposal.After.SeoTitle.Length <= FactburstWebsiteSeoDefaults.RecommendedTitleLength);
        Assert.True(proposal.After.SeoDescription.Length <= FactburstWebsiteSeoDefaults.RecommendedDescriptionLength);
        Assert.True(proposal.After.SocialTitle.Length <= FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength);
        Assert.True(proposal.After.SocialDescription.Length <= FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength);
        AssertReadyAfterApply(row, proposal, quizzes);
    }

    [Fact]
    public void Create_does_not_offer_automatic_fix_for_structural_slug_problem()
    {
        var quiz = Quiz("BAD SLUG", "Broken Quiz", "Science");
        var quizzes = new List<FactburstWebsiteSeoQuiz> { quiz };
        var row = Assert.Single(FactburstWebsiteSeoAudit.Build(quizzes));
        Assert.Equal(WebsiteSeoAuditSeverity.NeedsAttention, row.Severity);

        var proposal = FactburstWebsiteSeoAutoFix.Create(row, quizzes);

        Assert.False(proposal.CanApply);
        Assert.Empty(proposal.Changes);
    }

    [Fact]
    public void Duplicate_visible_title_is_not_an_seo_warning_when_search_titles_are_unique()
    {
        var rows = FactburstWebsiteSeoAudit.Build([
            Quiz("film-one", "Film Challenge", "Film", seoTitle: "Classic Film Challenge | Factburst Quiz"),
            Quiz("film-two", "Film Challenge", "Film", seoTitle: "Modern Film Challenge | Factburst Quiz"),
        ]);

        Assert.All(rows, row => Assert.Equal(WebsiteSeoAuditSeverity.Ready, row.Severity));
    }

    private static void AssertReadyAfterApply(
        WebsiteSeoAuditRow row,
        WebsiteSeoAutoFixProposal proposal,
        IReadOnlyList<FactburstWebsiteSeoQuiz> quizzes)
    {
        var updated = row.Source with
        {
            SeoTitle = proposal.After.SeoTitle,
            SeoDescription = proposal.After.SeoDescription,
            SocialTitle = proposal.After.SocialTitle,
            SocialDescription = proposal.After.SocialDescription,
        };
        var inventory = quizzes
            .Select(quiz => ReferenceEquals(quiz, row.Source) ? updated : quiz)
            .ToList();
        var refreshed = FactburstWebsiteSeoAudit.Build(inventory)
            .Single(item => item.Slug == row.Slug);
        Assert.Equal(WebsiteSeoAuditSeverity.Ready, refreshed.Severity);
    }

    private static FactburstWebsiteSeoQuiz Quiz(
        string slug,
        string title,
        string category,
        string description = "",
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
