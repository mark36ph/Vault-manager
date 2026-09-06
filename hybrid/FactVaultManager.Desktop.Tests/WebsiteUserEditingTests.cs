using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteUserEditingTests
{
    [Fact]
    public async Task UpdateUser_uses_short_lived_admin_token_then_sends_account_fields()
    {
        var requests = new List<(HttpMethod Method, string Url, AuthenticationHeaderValue? Authorization, string Body)>();
        var handler = new StubHandler(request =>
        {
            var body = request.Content is null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method, request.RequestUri?.ToString() ?? "", request.Headers.Authorization, body));
            if (requests.Count == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"edit_token\":\"abcdefghijklmnopqrstuvwxyz0123456789EDITTOKEN\",\"edit_url\":\"https://factburst-quiz-site.factburstquiz.workers.dev/api/account/admin-edit\"}",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"updated\":true}", Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new FactburstWebsiteUserEditingClient(http);
        await client.UpdateUserAsync(
            "https://go.factburstquiz.com",
            "1234567890abcdef",
            42,
            "New Name",
            "new@example.com",
            "a-secure-new-password");

        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.EndsWith("/api/site/users/42/edit-token", requests[0].Url, StringComparison.Ordinal);
        Assert.Equal("Bearer", requests[0].Authorization?.Scheme);
        Assert.Equal("1234567890abcdef", requests[0].Authorization?.Parameter);

        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.EndsWith("/api/account/admin-edit", requests[1].Url, StringComparison.Ordinal);
        Assert.Contains("abcdefghijklmnopqrstuvwxyz0123456789EDITTOKEN", requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("New Name", requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("new@example.com", requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("a-secure-new-password", requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Build208_website_user_editing_has_username_email_password_controls_and_security_notes()
    {
        var dialog = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/WebsiteUserEditDialog.cs");
        var ui = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.WebsiteUserEditing.cs");
        var admin = ReadRepositoryFile("cloudflare/factburst-quiz-site/account-admin-edit.js");
        var token = ReadRepositoryFile("cloudflare/factburst-link-tracker/site-user-edit-token-admin.js");

        Assert.Contains("New password", dialog, StringComparison.Ordinal);
        Assert.Contains("Confirm new password", dialog, StringComparison.Ordinal);
        Assert.Contains("Edit account", ui, StringComparison.Ordinal);
        Assert.Contains("emailChanged", ui, StringComparison.Ordinal);
        Assert.Contains("passwordChanged", ui, StringComparison.Ordinal);
        Assert.Contains("site_admin_user_edit_tokens", token, StringComparison.Ordinal);
        Assert.Contains("derivePasswordHash", admin, StringComparison.Ordinal);
        Assert.Contains("PASSWORD_POLICY.iterations", admin, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM site_sessions", admin, StringComparison.Ordinal);
        Assert.Contains("email_verified_at = CASE WHEN ? THEN NULL", admin, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = AppContext.BaseDirectory;
        for (var attempt = 0; attempt < 8 && !string.IsNullOrWhiteSpace(directory); attempt++)
        {
            var candidate = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
