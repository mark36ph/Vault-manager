using System.Net;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteAnalyticsAdminClientTests
{
    [Fact]
    public async Task FetchAsync_parses_funnel_quizzes_sources_and_uses_tracker_key()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "days": 30,
                      "from": "2026-08-02",
                      "to": "2026-08-31",
                      "events": {
                        "home_view": 100,
                        "quiz_opened": 80,
                        "quiz_started": 70,
                        "quiz_completed": 56,
                        "score_shared": 12,
                        "youtube_clicked": 18
                      },
                      "quizzes": {
                        "space-quiz": {
                          "quiz_opened": 40,
                          "quiz_started": 35,
                          "quiz_completed": 30,
                          "score_shared": 8,
                          "youtube_clicked": 10
                        }
                      },
                      "quiz_titles": { "space-quiz": "Space Quiz" },
                      "sources": {
                        "home": { "quiz_opened": 50, "quiz_completed": 35 },
                        "external": { "quiz_opened": 30, "quiz_completed": 21 }
                      },
                      "daily": [
                        { "day": "2026-08-31", "event_name": "quiz_completed", "count": 9 }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteAnalyticsAdminClient(http);

        var result = await client.FetchAsync("https://go.factburstquiz.com", "1234567890abcdef", 30);

        Assert.Equal(30, result.Days);
        Assert.Equal(80, result.Events["quiz_opened"]);
        Assert.Equal(30, result.Quizzes["space-quiz"]["quiz_completed"]);
        Assert.Equal("Space Quiz", result.QuizTitles["space-quiz"]);
        Assert.Equal(50, result.Sources["home"]["quiz_opened"]);
        var daily = Assert.Single(result.Daily);
        Assert.Equal("quiz_completed", daily.EventName);
        Assert.Equal(9, daily.Count);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", captured.Headers.Authorization?.Parameter);
        Assert.Equal("https://go.factburstquiz.com/api/site/analytics?days=30", captured.RequestUri?.ToString());
    }

    [Fact]
    public async Task FetchAsync_clamps_requested_period_to_supported_range()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"days\":180,\"events\":{},\"quizzes\":{},\"quiz_titles\":{},\"sources\":{},\"daily\":[]}",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteAnalyticsAdminClient(http);

        var result = await client.FetchAsync("https://go.factburstquiz.com/", "1234567890abcdef", 999);

        Assert.Equal(180, result.Days);
        Assert.Contains("days=180", captured?.RequestUri?.Query ?? "", StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
