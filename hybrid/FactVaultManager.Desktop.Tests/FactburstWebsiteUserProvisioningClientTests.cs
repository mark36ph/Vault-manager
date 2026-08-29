using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteUserProvisioningClientTests
{
    [Fact]
    public async Task CreateUser_mints_admin_token_then_uses_admin_signup_endpoint()
    {
        var requests = new List<(HttpMethod Method, string Url, AuthenticationHeaderValue? Authorization, string Origin, string Body)>();
        var call = 0;
        var handler = new StubHandler(request =>
        {
            var body = request.Content is null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((
                request.Method,
                request.RequestUri?.ToString() ?? "",
                request.Headers.Authorization,
                request.Headers.TryGetValues("Origin", out var origins) ? origins.Single() : "",
                body));

            call++;
            if (call == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        """
                        {
                          "signup_token": "abcdefghijklmnopqrstuvwxyz0123456789TOKEN",
                          "signup_url": "https://factburst-quiz-site.factburstquiz.workers.dev/api/account/admin-signup",
                          "origin": "https://factburst-quiz-site.factburstquiz.workers.dev",
                          "expires_at": "2026-08-29T20:00:00Z",
                          "email_verified": true
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """
                    {
                      "created": true,
                      "user": {
                        "id": 42,
                        "username": "Factburst",
                        "email": "official@example.com",
                        "email_verified": true,
                        "email_verified_at": "2026-08-29T19:50:00Z"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteUserProvisioningClient(http);

        var created = await client.CreateUserAsync(
            "https://go.factburstquiz.com",
            "1234567890abcdef",
            "Factburst",
            "official@example.com",
            "a-long-admin-password",
            activateImmediately: true);

        Assert.Equal(42, created.UserId);
        Assert.Equal("Factburst", created.Username);
        Assert.True(created.EmailVerified);
        Assert.Equal(2, requests.Count);

        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.EndsWith("/api/site/users/provision", requests[0].Url, StringComparison.Ordinal);
        Assert.Equal("Bearer", requests[0].Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", requests[0].Authorization?.Parameter);
        Assert.Contains("\"username\":\"Factburst\"", requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"email_verified\":true", requests[0].Body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.EndsWith("/api/account/admin-signup", requests[1].Url, StringComparison.Ordinal);
        Assert.Equal("https://factburst-quiz-site.factburstquiz.workers.dev", requests[1].Origin);
        Assert.Contains("abcdefghijklmnopqrstuvwxyz0123456789TOKEN", requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"username\":\"Factburst\"", requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateUser_posts_to_force_activate_route_with_bearer_key()
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
                      "activated": true,
                      "user": {
                        "id": 7,
                        "username": "Space Ace",
                        "email_verified": true,
                        "status": "active"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteUserProvisioningClient(http);

        await client.ActivateUserAsync("https://go.factburstquiz.com", "1234567890abcdef", 7);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/api/site/users/7/activate", captured.RequestUri?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", captured.Headers.Authorization?.Parameter);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
