using System.Net;
using System.Net.Http;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstLinkTrackerTests
{
    [Fact]
    public void CampaignSlug_UsesSeriesAndEpisode()
    {
        var history = History(id: 41, series: "Space Quiz", episode: 21);

        Assert.Equal("space-021", FactburstLinkTrackerClient.CampaignSlug(history));
    }

    [Fact]
    public void BuildLinks_UsesSeparateSourcePaths()
    {
        var links = FactburstLinkTrackerClient.BuildLinks(
            "https://tracker.example.workers.dev/",
            "space-021");

        Assert.Equal("https://tracker.example.workers.dev/fb/space-021", links.FacebookUrl);
        Assert.Equal("https://tracker.example.workers.dev/ig/space-021", links.InstagramUrl);
        Assert.Equal("https://tracker.example.workers.dev/yt/space-021", links.YouTubePromoUrl);
    }

    [Fact]
    public void ReplaceFullQuizLink_ReplacesYouTubeDestinationOnly()
    {
        const string description = "Try it now\n\nWatch the full quiz: https://www.youtube.com/watch?v=abc123XYZ_0\n\n#Quiz";

        var result = FactburstLinkTrackerClient.ReplaceFullQuizLink(
            description,
            "https://go.example.com/fb/space-021");

        Assert.Contains("https://go.example.com/fb/space-021", result);
        Assert.DoesNotContain("youtube.com/watch", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#Quiz", result);
    }

    [Fact]
    public async Task CreateCampaign_SendsBearerTokenAndCampaignPayload()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpHandler(async request =>
        {
            captured = request;
            body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            return JsonResponse("{\"ok\":true,\"slug\":\"space-021\"}");
        });
        var client = new FactburstLinkTrackerClient(new HttpClient(handler));

        var links = await client.CreateOrUpdateCampaignAsync(
            "https://tracker.example.workers.dev",
            "secret-token-value-123456789",
            "space-021",
            41,
            "Space Quiz #021",
            "https://youtu.be/abc123XYZ_0");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://tracker.example.workers.dev/api/campaigns", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token-value-123456789", captured.Headers.Authorization.Parameter);
        Assert.Contains("\"quiz_id\":41", body);
        Assert.Contains("\"destination_url\":\"https://youtu.be/abc123XYZ_0\"", body);
        Assert.Equal("https://tracker.example.workers.dev/fb/space-021", links.FacebookUrl);
    }

    [Fact]
    public async Task FetchStats_ParsesSourceAttributedCampaignTotals()
    {
        var handler = new StubHttpHandler(_ => Task.FromResult(JsonResponse("""
            {
              "campaigns": [
                {
                  "slug": "space-021",
                  "quiz_id": 41,
                  "title": "Space Quiz #021",
                  "facebook_clicks": 84,
                  "instagram_clicks": 31,
                  "youtube_promo_clicks": 126,
                  "total_clicks": 241
                }
              ]
            }
            """)));
        var client = new FactburstLinkTrackerClient(new HttpClient(handler));

        var stats = await client.FetchStatsAsync(
            "https://tracker.example.workers.dev",
            "secret-token-value-123456789");

        var campaign = Assert.Single(stats);
        Assert.Equal(41, campaign.QuizId);
        Assert.Equal(84, campaign.FacebookClicks);
        Assert.Equal(31, campaign.InstagramClicks);
        Assert.Equal(126, campaign.YouTubePromoClicks);
        Assert.Equal(241, campaign.TotalClicks);
    }

    [Theory]
    [InlineData(0, 0, true, "Waiting for tracked clicks")]
    [InlineData(10, 100, true, "Early data")]
    [InlineData(25, 100, true, "Promo gaining traction")]
    [InlineData(60, 30, true, "High promo traffic / check long-form")]
    [InlineData(60, 30, false, "Create promo")]
    public void FunnelClassifier_UsesNonConversionSignals(long clicks, long views, bool promoCreated, string expected)
    {
        Assert.Equal(expected, FactburstFunnelClassifier.Label(clicks, views, promoCreated));
    }

    private static QuizHistorySummary History(int id, string series, int episode) => new(
        Id: id,
        Title: "Space Quiz",
        Created: "2026-08-26",
        QuestionCount: 10,
        Categories: "Space",
        Format: "16:9",
        QuestionSeconds: 8,
        ShuffleAnswers: true,
        ProjectFolder: @"C:\Quizzes\Space021",
        SeriesName: series,
        EpisodeNumber: episode,
        YouTubeTitle: "Space Quiz #021",
        YouTubeDescription: "",
        Hashtags: "#Quiz",
        PinnedComment: "",
        PublishedOnYouTube: true,
        YouTubeUrl: "https://youtu.be/abc123XYZ_0");

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request);
    }
}
