using System.Net.Http;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FacebookReelAnalytics(
    string VideoId,
    string Description,
    string PermalinkUrl,
    DateTime? PublishedAt,
    long Views,
    long Reactions,
    long Comments,
    long Shares);

public sealed class FacebookReelAnalyticsService
{
    private const string GraphRoot = "https://graph.facebook.com/v26.0";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public FacebookReelAnalyticsService(HttpClient? client = null) => _client = client ?? SharedClient;

    public static string? TryGetReelId(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.TrimEnd('.');
        if (!(string.Equals(host, "facebook.com", StringComparison.OrdinalIgnoreCase) ||
              host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)))
            return null;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < parts.Length; index++)
        {
            if (parts[index].Equals("reel", StringComparison.OrdinalIgnoreCase) ||
                parts[index].Equals("reels", StringComparison.OrdinalIgnoreCase) ||
                parts[index].Equals("videos", StringComparison.OrdinalIgnoreCase))
                return parts[index + 1].All(char.IsDigit) ? parts[index + 1] : null;
        }
        return null;
    }

    public async Task<FacebookReelAnalytics> FetchAsync(
        string pageAccessToken,
        string videoId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        if (string.IsNullOrWhiteSpace(videoId) || !videoId.All(char.IsDigit))
            throw new ArgumentException("The Facebook Reel link does not contain a numeric video ID.");

        var fields = "id,description,permalink_url,created_time," +
                     "reactions.limit(0).summary(true),comments.limit(0).summary(true),sharedposts.limit(0).summary(true)";
        var detailsUrl = $"{GraphRoot}/{Uri.EscapeDataString(videoId)}?fields={Uri.EscapeDataString(fields)}" +
                         $"&access_token={Uri.EscapeDataString(pageAccessToken.Trim())}";
        using var detailsResponse = await _client.GetAsync(detailsUrl, cancellationToken);
        using var details = await ReadDocumentAsync(detailsResponse, cancellationToken);

        var insightsUrl = $"{GraphRoot}/{Uri.EscapeDataString(videoId)}/video_insights" +
                          $"?metric=total_video_views&access_token={Uri.EscapeDataString(pageAccessToken.Trim())}";
        using var insightsResponse = await _client.GetAsync(insightsUrl, cancellationToken);
        using var insights = await ReadDocumentAsync(insightsResponse, cancellationToken);

        return Parse(details.RootElement, insights.RootElement);
    }

    public static FacebookReelAnalytics Parse(string detailsJson, string insightsJson)
    {
        using var details = JsonDocument.Parse(detailsJson);
        using var insights = JsonDocument.Parse(insightsJson);
        return Parse(details.RootElement, insights.RootElement);
    }

    private static FacebookReelAnalytics Parse(JsonElement details, JsonElement insights)
    {
        return new FacebookReelAnalytics(
            ReadString(details, "id"),
            ReadString(details, "description"),
            ReadString(details, "permalink_url"),
            ReadDate(details, "created_time"),
            ReadInsight(insights, "total_video_views"),
            ReadSummaryCount(details, "reactions"),
            ReadSummaryCount(details, "comments"),
            ReadSummaryCount(details, "sharedposts"));
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return JsonDocument.Parse(json);
        var message = "Facebook request failed";
        try
        {
            using var error = JsonDocument.Parse(json);
            if (error.RootElement.TryGetProperty("error", out var value))
                message = ReadString(value, "message") is { Length: > 0 } detail ? detail : message;
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static long ReadSummaryCount(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var edge) ||
            !edge.TryGetProperty("summary", out var summary) ||
            !summary.TryGetProperty("total_count", out var count)) return 0;
        return count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out var value) ? value : 0;
    }

    private static long ReadInsight(JsonElement root, string name)
    {
        if (!root.TryGetProperty("data", out var data)) return 0;
        foreach (var metric in data.EnumerateArray())
        {
            if (!string.Equals(ReadString(metric, "name"), name, StringComparison.Ordinal)) continue;
            if (!metric.TryGetProperty("values", out var values)) return 0;
            var value = values.EnumerateArray().FirstOrDefault();
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("value", out var number)) return 0;
            if (number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out var result)) return result;
        }
        return 0;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && DateTime.TryParse(value.GetString(), out var date) ? date : null;
}
