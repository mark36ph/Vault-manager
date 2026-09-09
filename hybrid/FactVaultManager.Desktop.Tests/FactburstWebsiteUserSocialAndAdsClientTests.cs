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
        var handler = new StubHandler(request => { captured = request; return Json(HttpStatusCode.OK, """
            { "friends": [{ "friendship_id": 3, "user_id": 8, "username": "Space Ace", "user_status": "active", "created_at": "2026-08-29T10:00:00Z", "responded_at": "2026-08-29T11:00:00Z" }],
              "incoming": [{ "friendship_id": 4, "user_id": 9, "username": "History Buff", "user_status": "active", "created_at": "2026-08-29T12:00:00Z", "responded_at": null }], "outgoing": [] }
            """); });
        using var http = new HttpClient(handler);
        using var client = new FactVaultManager.Desktop.FactburstWebsiteUserFriendsClient(http);
        var result = await client.FetchAsync("https://go.factburstquiz.com", "1234567890abcdef", 7);
        Assert.Equal("Space Ace", Assert.Single(result.Friends).Username);
        Assert.Equal("History Buff", Assert.Single(result.Incoming).Username);
        Assert.Empty(result.Outgoing);
        Assert.Equal("Bearer", captured?.Headers.Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", captured?.Headers.Authorization?.Parameter);
        Assert.EndsWith("/api/site/users/7/friends", captured?.RequestUri?.AbsoluteUri ?? "", StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request)); }
}
