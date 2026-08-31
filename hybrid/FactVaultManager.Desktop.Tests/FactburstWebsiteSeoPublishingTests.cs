using System.Net;
using System.Net.Http;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteSeoPublishingTests
{
    [Fact]
    public void Defaults_create_branded_search_and_social_copy()
    {
        var values = FactburstWebsiteSeoDefaults.Create(
            "Space Quiz 42",
            "Space",
            "",
            10);

        Assert.Contains("Factburst Quiz", values.SeoTitle, StringComparison.OrdinalIgnoreCase);
        Assert.True(values.SeoTitle.Length <= FactburstWebsiteSeoDefaults.RecommendedTitleLength + 1);
        Assert.Contains("10-question", values.SeoDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Space", values.SeoDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Space Quiz 42", values.SocialTitle);
        Assert.Contains("10/10", values.SocialDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Effective_preserves_saved_overrides_and_fills_missing_values()
    {
        var quiz = new FactburstWebsiteSeoQuiz(
            "space-quiz-42",
            "Space Quiz 42",
            "Space",
            "Short description",
            "Custom SEO title",
            "",
            "Custom social title",
            "",
            "published",
            "2026-08-31T12:00:00Z",
            "2026-08-31T12:00:00Z",
            10);

        var values = FactburstWebsiteSeoDefaults.Effective(quiz);

        Assert.Equal("Custom SEO title", values.SeoTitle);
        Assert.Equal("Custom social title", values.SocialTitle);
        Assert.NotEmpty(values.SeoDescription);
        Assert.NotEmpty(values.SocialDescription);
    }

    [Fact]
    public void Clean_urls_use_stable_production_routes()
    {
        Assert.Equal(
            "https://factburstquiz.com/quiz/space-quiz-42",
            FactburstWebsiteSeoDefaults.CleanQuizUrl("Space-Quiz-42"));
        Assert.Equal(
            "https://factburstquiz.com/social/quiz/space-quiz-42.png",
            FactburstWebsiteSeoDefaults.SocialImageUrl("Space-Quiz-42"));
    }

    [Fact]
    public async Task Client_fetches_private_seo_inventory_with_tracker_authentication()
    {
        HttpRequestMessage? captured = null;
        using var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"quizzes\":[{\"slug\":\"space-quiz-42\",\"title\":\"Space Quiz 42\",\"category\":\"Space\",\"description\":\"Quiz\",\"seo_title\":\"SEO\",\"seo_description\":\"Description\",\"social_title\":\"Social\",\"social_description\":\"Social description\",\"status\":\"published\",\"publish_at\":\"\",\"updated_at\":\"\",\"question_count\":10}]}",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var client = new FactburstWebsiteSeoAdminClient(handler);

        var quizzes = await client.FetchAsync("https://go.factburstquiz.com/", "secret-key");

        Assert.Single(quizzes);
        Assert.Equal("space-quiz-42", quizzes[0].Slug);
        Assert.NotNull(captured);
        Assert.Equal("https://go.factburstquiz.com/api/site/quiz-seo", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Client_patches_only_the_selected_slug()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var handler = new StubHandler(async request =>
        {
            captured = request;
            body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new FactburstWebsiteSeoAdminClient(handler);
        var values = new FactburstWebsiteSeoValues(
            "SEO title",
            "SEO description",
            "Social title",
            "Social description");

        await client.UpdateAsync("https://go.factburstquiz.com", "secret-key", "space-quiz-42", values);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        Assert.Equal("https://go.factburstquiz.com/api/site/quiz-seo/space-quiz-42", captured.RequestUri!.ToString());
        Assert.Contains("\"seo_title\":\"SEO title\"", body, StringComparison.Ordinal);
        Assert.Contains("\"social_description\":\"Social description\"", body, StringComparison.Ordinal);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
