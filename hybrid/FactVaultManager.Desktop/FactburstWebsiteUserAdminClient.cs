using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteUserAdminSummary(
    int Total,
    int Active,
    int Suspended,
    int Verified,
    int Unverified);

public sealed record FactburstWebsiteUserSummary(
    int Id,
    string Username,
    string Email,
    bool EmailVerified,
    string Status,
    string SuspendedAt,
    string SuspensionReason,
    string CreatedAt,
    string LastLoginAt,
    string LastPlayedAt,
    int QuizzesCompleted,
    int Attempts,
    int TotalScore,
    int TotalPossible,
    int Percentage);

public sealed record FactburstWebsiteUserQuizActivity(
    int QuizId,
    string Slug,
    string Title,
    int BestScore,
    int Total,
    int Percentage,
    int Attempts,
    string FirstCompletedAt,
    string LastCompletedAt);

public sealed record FactburstWebsiteUserList(
    FactburstWebsiteUserAdminSummary Summary,
    IReadOnlyList<FactburstWebsiteUserSummary> Users);

public sealed record FactburstWebsiteUserDetail(
    FactburstWebsiteUserSummary User,
    IReadOnlyList<FactburstWebsiteUserQuizActivity> Quizzes);

public sealed class FactburstWebsiteUserAdminClient
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly HttpClient _client;

    public FactburstWebsiteUserAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<FactburstWebsiteUserList> FetchUsersAsync(
        string baseUrl,
        string apiKey,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        var url = root + "/api/site/users?limit=500";
        if (!string.IsNullOrWhiteSpace(search))
            url += "&search=" + Uri.EscapeDataString(search.Trim());

        using var request = CreateRequest(HttpMethod.Get, url, key);
        using var response = await _client.SendAsync(request, cancellationToken);
        var document = await ReadSuccessJsonAsync(response, "The website user list could not be loaded", cancellationToken);
        using (document)
        {
            var rootElement = document.RootElement;
            var summary = rootElement.TryGetProperty("summary", out var summaryElement)
                ? new FactburstWebsiteUserAdminSummary(
                    ReadInt(summaryElement, "total"),
                    ReadInt(summaryElement, "active"),
                    ReadInt(summaryElement, "suspended"),
                    ReadInt(summaryElement, "verified"),
                    ReadInt(summaryElement, "unverified"))
                : new FactburstWebsiteUserAdminSummary(0, 0, 0, 0, 0);

            var users = new List<FactburstWebsiteUserSummary>();
            if (rootElement.TryGetProperty("users", out var usersElement) && usersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in usersElement.EnumerateArray())
                    users.Add(ParseUser(item));
            }
            return new FactburstWebsiteUserList(summary, users);
        }
    }

    public async Task<FactburstWebsiteUserDetail> FetchUserAsync(
        string baseUrl,
        string apiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        using var request = CreateRequest(HttpMethod.Get, $"{root}/api/site/users/{RequireUserId(userId)}", key);
        using var response = await _client.SendAsync(request, cancellationToken);
        var document = await ReadSuccessJsonAsync(response, "The website user could not be loaded", cancellationToken);
        using (document)
        {
            var rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("user", out var userElement) || userElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The website user response did not contain a user record.");

            var quizzes = new List<FactburstWebsiteUserQuizActivity>();
            if (rootElement.TryGetProperty("quizzes", out var quizElement) && quizElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in quizElement.EnumerateArray())
                {
                    quizzes.Add(new FactburstWebsiteUserQuizActivity(
                        ReadInt(item, "quiz_id"),
                        ReadString(item, "slug"),
                        ReadString(item, "title"),
                        ReadInt(item, "best_score"),
                        ReadInt(item, "total"),
                        ReadInt(item, "percentage"),
                        ReadInt(item, "attempts"),
                        ReadString(item, "first_completed_at"),
                        ReadString(item, "last_completed_at")));
                }
            }
            return new FactburstWebsiteUserDetail(ParseUser(userElement), quizzes);
        }
    }

    public async Task<FactburstWebsiteUserDetail> SetStatusAsync(
        string baseUrl,
        string apiKey,
        int userId,
        string status,
        string reason = "",
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        var payload = JsonSerializer.Serialize(new { status = status.Trim().ToLowerInvariant(), reason = reason.Trim() });
        using var request = CreateRequest(HttpMethod.Patch, $"{root}/api/site/users/{RequireUserId(userId)}", key);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        var document = await ReadSuccessJsonAsync(response, "The website user status could not be updated", cancellationToken);
        using (document)
        {
            var rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("user", out var userElement) || userElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The website user response did not contain the updated user record.");

            var quizzes = new List<FactburstWebsiteUserQuizActivity>();
            if (rootElement.TryGetProperty("quizzes", out var quizElement) && quizElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in quizElement.EnumerateArray())
                {
                    quizzes.Add(new FactburstWebsiteUserQuizActivity(
                        ReadInt(item, "quiz_id"),
                        ReadString(item, "slug"),
                        ReadString(item, "title"),
                        ReadInt(item, "best_score"),
                        ReadInt(item, "total"),
                        ReadInt(item, "percentage"),
                        ReadInt(item, "attempts"),
                        ReadString(item, "first_completed_at"),
                        ReadString(item, "last_completed_at")));
                }
            }
            return new FactburstWebsiteUserDetail(ParseUser(userElement), quizzes);
        }
    }

    public async Task DeleteUserAsync(
        string baseUrl,
        string apiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        using var request = CreateRequest(HttpMethod.Delete, $"{root}/api/site/users/{RequireUserId(userId)}", key);
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessJsonAsync(response, "The website user could not be deleted", cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
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
        return JsonDocument.Parse(body);
    }

    private static FactburstWebsiteUserSummary ParseUser(JsonElement item) => new(
        ReadInt(item, "id"),
        ReadString(item, "username"),
        ReadString(item, "email"),
        ReadBool(item, "email_verified"),
        ReadString(item, "status").Length == 0 ? "active" : ReadString(item, "status"),
        ReadNullableString(item, "suspended_at"),
        ReadString(item, "suspension_reason"),
        ReadString(item, "created_at"),
        ReadString(item, "last_login_at"),
        ReadNullableString(item, "last_played_at"),
        ReadInt(item, "quizzes_completed"),
        ReadInt(item, "attempts"),
        ReadInt(item, "total_score"),
        ReadInt(item, "total_possible"),
        ReadInt(item, "percentage"));

    private static string RequireApiKey(string? value)
    {
        var key = (value ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        return key;
    }

    private static int RequireUserId(int userId) => userId > 0
        ? userId
        : throw new ArgumentOutOfRangeException(nameof(userId));

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static string ReadNullableString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int ReadInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return 0;
    }

    private static bool ReadBool(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed;
    }
}
