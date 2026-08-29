using System.Net;
using System.Text.Json;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteVisibilityTests
{
    [Fact]
    public void DisplayState_distinguishes_live_upcoming_and_offline()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("Offline", FactburstWebsiteVisibility.DisplayState("draft", "2026-08-30T12:00:00Z", now));
        Assert.Equal("Upcoming", FactburstWebsiteVisibility.DisplayState("published", "2026-08-30T12:00:00Z", now));
        Assert.Equal("Live", FactburstWebsiteVisibility.DisplayState("published", "2026-08-28T12:00:00Z", now));
        Assert.Equal("Live", FactburstWebsiteVisibility.DisplayState("published", "", now));
    }

    [Fact]
    public async Task SetLiveNow_uses_authenticated_status_only_patch()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteVisibilityClient(http);
        var now = new DateTimeOffset(2026, 8, 29, 12, 34, 56, TimeSpan.Zero);

        await client.SetLiveNowAsync(
            "https://go.factburstquiz.com",
            "tracker-test-key-123456789",
            "space-quiz-12",
            now);

        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal("https://go.factburstquiz.com/api/site/quizzes/space-quiz-12", handler.Uri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("tracker-test-key-123456789", handler.AuthorizationParameter);

        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("published", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("2026-08-29T12:34:56.0000000+00:00", json.RootElement.GetProperty("publish_at").GetString());
    }

    [Fact]
    public async Task SetOffline_keeps_existing_release_time_in_patch()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteVisibilityClient(http);

        await client.SetOfflineAsync(
            "https://go.factburstquiz.com",
            "tracker-test-key-123456789",
            "history-quiz-4",
            "2026-09-05T18:00:00+01:00");

        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("draft", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 9, 5, 17, 0, 0, TimeSpan.Zero),
            DateTimeOffset.Parse(json.RootElement.GetProperty("publish_at").GetString()!));
    }

    [Fact]
    public async Task Visibility_client_rejects_non_https_endpoint()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteVisibilityClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetLiveNowAsync(
            "http://go.factburstquiz.com",
            "tracker-test-key-123456789",
            "space-quiz",
            DateTimeOffset.UtcNow));

        Assert.Null(handler.Method);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}"),
            };
        }
    }
}
