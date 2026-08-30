using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteModerationComment(
    int Id,
    string Username,
    string Body,
    string Status,
    int Reports,
    string ReportReasons,
    string QuizSlug,
    string QuizTitle,
    string CreatedAt,
    string EditedAt);

public sealed record FactburstWebsiteCommentModerationSummary(int Active, int Hidden, int Reported);
public sealed record FactburstWebsiteCommentModerationResult(
    IReadOnlyList<FactburstWebsiteModerationComment> Comments,
    FactburstWebsiteCommentModerationSummary Summary);

public sealed class FactburstWebsiteCommentsAdminClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstWebsiteCommentsAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
    }

    public async Task<FactburstWebsiteCommentModerationResult> FetchAsync(
        string baseUrl,
        string apiKey,
        string status = "reported",
        string search = "",
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = NormalizeStatus(status);
        var query = $"status={Uri.EscapeDataString(normalizedStatus)}";
        var trimmedSearch = (search ?? "").Trim();
        if (trimmedSearch.Length > 0)
            query += $"&q={Uri.EscapeDataString(trimmedSearch)}";

        using var document = await SendAsync(
            HttpMethod.Get,
            Endpoint(baseUrl, $"/api/site/comments?{query}"),
            apiKey,
            null,
            cancellationToken);

        var comments = new List<FactburstWebsiteModerationComment>();
        if (document.RootElement.TryGetProperty("comments", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                comments.Add(new FactburstWebsiteModerationComment(
                    ReadInt(item, "id"),
                    ReadString(item, "username"),
                    ReadString(item, "body"),
                    ReadString(item, "status"),
                    ReadInt(item, "reports"),
                    ReadString(item, "report_reasons"),
                    ReadString(item, "quiz_slug"),
                    ReadString(item, "quiz_title"),
                    ReadString(item, "created_at"),
                    ReadString(item, "edited_at")));
            }
        }

        var summary = document.RootElement.TryGetProperty("summary", out var summaryElement)
            ? new FactburstWebsiteCommentModerationSummary(
                ReadInt(summaryElement, "active"),
                ReadInt(summaryElement, "hidden"),
                ReadInt(summaryElement, "reported"))
            : new FactburstWebsiteCommentModerationSummary(0, 0, 0);
        return new FactburstWebsiteCommentModerationResult(comments, summary);
    }

    public async Task ApplyActionAsync(
        string baseUrl,
        string apiKey,
        int commentId,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (commentId <= 0) throw new ArgumentOutOfRangeException(nameof(commentId));
        var normalizedAction = NormalizeAction(action);
        var payload = JsonSerializer.Serialize(new { action = normalizedAction });
        using var _ = await SendAsync(
            HttpMethod.Patch,
            Endpoint(baseUrl, $"/api/site/comments/{commentId}"),
            apiKey,
            payload,
            cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string url,
        string apiKey,
        string? json,
        CancellationToken cancellationToken)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Website first.");

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            try
            {
                using var error = JsonDocument.Parse(body);
                detail = ReadString(error.RootElement, "error");
            }
            catch (JsonException)
            {
            }
            throw new InvalidOperationException(
                $"Comment moderation request failed (HTTP {(int)response.StatusCode})" +
                (detail.Length == 0 ? "." : $": {detail}"));
        }
        return JsonDocument.Parse(body);
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = (status ?? "reported").Trim().ToLowerInvariant();
        if (normalized is "reported" or "active" or "hidden" or "all") return normalized;
        throw new ArgumentException("Status must be Reported, Active, Hidden or All.", nameof(status));
    }

    private static string NormalizeAction(string action)
    {
        var normalized = (action ?? "").Trim().ToLowerInvariant();
        if (normalized is "hide" or "restore" or "dismiss_reports" or "delete") return normalized;
        throw new ArgumentException("Action must be Hide, Restore, Dismiss reports or Delete.", nameof(action));
    }

    private static string Endpoint(string baseUrl, string path) =>
        $"{FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl)}{path}";

    private static string ReadString(JsonElement item, string name) =>
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
}
