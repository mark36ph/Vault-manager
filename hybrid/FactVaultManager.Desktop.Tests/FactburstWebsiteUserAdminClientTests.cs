using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstWebsiteUserAdminClientTests
{
    [Fact]
    public async Task FetchUsers_parses_summary_activity_and_uses_bearer_key()
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
                      "summary": { "total": 2, "active": 1, "suspended": 1, "verified": 2, "unverified": 0 },
                      "users": [
                        {
                          "id": 7,
                          "username": "Quiz Master",
                          "email": "player@example.com",
                          "email_verified": true,
                          "status": "active",
                          "suspended_at": null,
                          "suspension_reason": "",
                          "created_at": "2026-08-29T10:00:00Z",
                          "last_login_at": "2026-08-29T11:00:00Z",
                          "last_played_at": "2026-08-29T12:00:00Z",
                          "quizzes_completed": 3,
                          "attempts": 5,
                          "total_score": 24,
                          "total_possible": 30,
                          "percentage": 80
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteUserAdminClient(http);

        var result = await client.FetchUsersAsync("https://go.factburstquiz.com", "1234567890abcdef", "Quiz Master");

        Assert.Equal(2, result.Summary.Total);
        Assert.Equal(1, result.Summary.Suspended);
        var user = Assert.Single(result.Users);
        Assert.Equal(7, user.Id);
        Assert.Equal("Quiz Master", user.Username);
        Assert.Equal(5, user.Attempts);
        Assert.Equal(80, user.Percentage);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", captured.Headers.Authorization?.Parameter);
        Assert.Contains("search=Quiz%20Master", captured.RequestUri?.Query ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchUser_parses_per_quiz_attempt_history()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "user": {
                    "id": 9,
                    "username": "Space Ace",
                    "email": "space@example.com",
                    "email_verified": true,
                    "status": "suspended",
                    "suspended_at": "2026-08-29T13:00:00Z",
                    "suspension_reason": "",
                    "created_at": "2026-08-28T10:00:00Z",
                    "last_login_at": "2026-08-29T09:00:00Z",
                    "last_played_at": "2026-08-29T12:00:00Z",
                    "quizzes_completed": 1,
                    "attempts": 4,
                    "total_score": 8,
                    "total_possible": 10,
                    "percentage": 80
                  },
                  "quizzes": [
                    {
                      "quiz_id": 12,
                      "slug": "space-001",
                      "title": "Space Quiz",
                      "best_score": 8,
                      "total": 10,
                      "percentage": 80,
                      "attempts": 4,
                      "first_completed_at": "2026-08-28T11:00:00Z",
                      "last_completed_at": "2026-08-29T12:00:00Z"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteUserAdminClient(http);

        var detail = await client.FetchUserAsync("https://go.factburstquiz.com", "1234567890abcdef", 9);

        Assert.Equal("suspended", detail.User.Status);
        var quiz = Assert.Single(detail.Quizzes);
        Assert.Equal("Space Quiz", quiz.Title);
        Assert.Equal(4, quiz.Attempts);
        Assert.Equal(8, quiz.BestScore);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
