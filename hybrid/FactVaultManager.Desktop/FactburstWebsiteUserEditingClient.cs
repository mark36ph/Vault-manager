using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class FactburstWebsiteUserEditingClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly HttpClient _client;

    public FactburstWebsiteUserEditingClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
        // The default client is shared for the lifetime of the app. Injected clients are owned by the caller.
    }

    public async Task UpdateUserAsync(
        string trackerBaseUrl,
        string apiKey,
        int userId,
        string username,
        string email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(trackerBaseUrl);
        var key = RequireApiKey(apiKey);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{root}/api/site/users/{userId}/edit-token");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        tokenRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var tokenResponse = await _client.SendAsync(tokenRequest, cancellationToken);
        using var tokenDocument = await ReadSuccessJsonAsync(tokenResponse, "The account edit authorization could not be created", cancellationToken);
        var tokenRoot = tokenDocument.RootElement;
        var editToken = ReadString(tokenRoot, "edit_token");
        var editUrl = ReadString(tokenRoot, "edit_url");
        if (editToken.Length == 0 || editUrl.Length == 0)
            throw new InvalidOperationException("The account edit authorization response was incomplete.");

        var payload = new Dictionary<string, object?>
        {
            ["edit_token"] = editToken,
            ["username"] = (username ?? "").Trim(),
            ["email"] = (email ?? "").Trim(),
        };
        if (!string.IsNullOrEmpty(password))
            payload["password"] = password;

        var body = JsonSerializer.Serialize(payload);
        using var editRequest = new HttpRequestMessage(HttpMethod.Post, editUrl);
        editRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var editResponse = await _client.SendAsync(editRequest, cancellationToken);
        using var editDocument = await ReadSuccessJsonAsync(editResponse, "The website user could not be updated", cancellationToken);
    }

    private static async Task<JsonDocument> ReadSuccessJsonAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            try
            {
                using var errorDocument = JsonDocument.Parse(body);
                detail = ReadString(errorDocument.RootElement, "error");
            }
            catch (JsonException)
            {
            }
            throw new InvalidOperationException(
                $"{fallback} (HTTP {(int)response.StatusCode})" + (detail.Length == 0 ? "." : $": {detail}"));
        }
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"{fallback}: the server returned invalid JSON.", error);
        }
    }

    private static string RequireApiKey(string? value)
    {
        var key = (value ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        return key;
    }

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}
