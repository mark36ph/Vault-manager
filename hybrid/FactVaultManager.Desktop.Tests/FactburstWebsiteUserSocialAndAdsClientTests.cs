using System.Net;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteUserSocialAndAdsClientTests
{
    [Fact]
    public async Task Friends_client_parses_relationship_groups_and_uses_bearer_key()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """
                {
                  "friends": [
                    { "friendship_id": 3, "user_id": 8, "username": "Space Ace", "user_status": "active", "created_at": "2026-08-29T10:00:00Z", "responded_at": "2026-08-29T11:00:00Z" }
                  ],
                  "incoming": [
                    { "friendship_id": 4, "user_id": 9, "username": "History Buff", "user_status": "active", "created_at": "2026-08-29T12:00:00Z", "responded_at": null }
                  ],
                  "outgoing": []
                }
                """);
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteUserFriendsClient(http);

        var result = await client.FetchAsync("https://go.factburstquiz.com", "1234567890abcdef", 7);

        Assert.Equal("Space Ace", Assert.Single(result.Friends).Username);
        Assert.Equal("History Buff", Assert.Single(result.Incoming).Username);
        Assert.Empty(result.Outgoing);
        Assert.Equal("Bearer", captured?.Headers.Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", captured?.Headers.Authorization?.Parameter);
        Assert.True((captured?.RequestUri?.AbsoluteUri ?? "").EndsWith("/api/site/users/7/friends", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ads_client_fetches_and_saves_optional_side_ad_settings()
    {
        var calls = new List<(HttpMethod Method, string Url, string Scheme)>();
        var handler = new StubHandler(request =>
        {
            calls.Add((request.Method, request.RequestUri?.AbsoluteUri ?? "", request.Headers.Authorization?.Scheme ?? ""));
            var body = request.Method == HttpMethod.Get
                ? "{\"enabled\":false,\"client\":\"ca-pub-1234567890123456\",\"left_slot\":\"1234567890\",\"right_slot\":\"\"}"
                : "{\"enabled\":true,\"client\":\"ca-pub-1234567890123456\",\"left_slot\":\"1234567890\",\"right_slot\":\"0987654321\"}";
            return Json(HttpStatusCode.OK, body);
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteAdsAdminClient(http);

        var current = await client.FetchAsync("https://go.factburstquiz.com", "1234567890abcdef");
        var saved = await client.SaveAsync(
            "https://go.factburstquiz.com",
            "1234567890abcdef",
            new FactburstWebsiteAdsSettings(true, current.Client, current.LeftSlot, "0987654321"));

        Assert.False(current.Enabled);
        Assert.Equal("1234567890", current.LeftSlot);
        Assert.True(saved.Enabled);
        Assert.Equal("0987654321", saved.RightSlot);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal("Bearer", call.Scheme));
        Assert.Equal(HttpMethod.Patch, calls[1].Method);
        Assert.True(calls[1].Url.EndsWith("/api/site/ads", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
