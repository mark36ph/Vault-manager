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

public sealed record FacebookPageVideo(
    string VideoId,
    string Title,
    string Description,
    string PermalinkUrl,
    DateTime? PublishedAt);

public sealed record FacebookPageVideos(
    string PageId,
    string PageName,
    IReadOnlyList<FacebookPageVideo> Videos);

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
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            var key = Uri.UnescapeDataString(pair[..separator]);
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            if (key.Equals("v", StringComparison.OrdinalIgnoreCase) && value.Length > 0 && value.All(char.IsDigit)) return value;
        }
        return null;
    }

    public static string ResolveReelUrl(string videoId, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = (candidate ?? "").Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(TryGetReelId(uri.AbsoluteUri), videoId, StringComparison.Ordinal))
                return uri.AbsoluteUri;
        }

        if (string.IsNullOrWhiteSpace(videoId) || !videoId.All(char.IsDigit))
            throw new ArgumentException("Facebook did not return a usable Reel link or numeric video ID.");
        return $"https://www.facebook.com/reel/{videoId}";
    }

    public async Task<FacebookPageVideos> ListPageVideosAsync(
        string pageAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");

        var token = Uri.EscapeDataString(pageAccessToken.Trim());
        using var pageResponse = await _client.GetAsync(
            $"{GraphRoot}/me?fields=id%2Cname&access_token={token}", cancellationToken);
        using var page = await ReadDocumentAsync(pageResponse, cancellationToken);
        var pageId = ReadString(page.RootElement, "id");
        if (pageId.Length == 0 || !pageId.All(char.IsDigit))
            throw new InvalidOperationException("The saved token did not identify a Facebook Page. Use the Page access token returned by /me/accounts.");

        var pageName = ReadString(page.RootElement, "name");
        List<FacebookPageVideo> videos;
        try
        {
            videos = await ListVideoEdgeAsync("me", token, "video_reels", cancellationToken);
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("nonexisting field (video_reels)", StringComparison.OrdinalIgnoreCase))
        {
            videos = [];
        }
        if (videos.Count == 0)
            videos = await ListVideoEdgeAsync("me", token, "videos", cancellationToken);

        return new FacebookPageVideos(pageId, pageName, videos);
    }

    private async Task<List<FacebookPageVideo>> ListVideoEdgeAsync(
        string objectId,
        string escapedToken,
        string edge,
        CancellationToken cancellationToken)
    {
        var videos = new List<FacebookPageVideo>();
        string? after = null;
        do
        {
            var fields = "id,title,description,permalink_url,created_time";
            var url = $"{GraphRoot}/{Uri.EscapeDataString(objectId)}/{edge}" +
                      $"?fields={Uri.EscapeDataString(fields)}&limit=100&access_token={escapedToken}";
            if (!string.IsNullOrWhiteSpace(after))
                url += $"&after={Uri.EscapeDataString(after)}";

            using var response = await _client.GetAsync(url, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = ReadString(item, "id");
                    if (id.Length == 0) continue;
                    videos.Add(new FacebookPageVideo(
                        id,
                        ReadString(item, "title"),
                        ReadString(item, "description"),
                        ReadString(item, "permalink_url"),
                        ReadDate(item, "created_time")));
                }
            }
            after = ReadAfterCursor(document.RootElement);
        } while (!string.IsNullOrWhiteSpace(after));
        return videos;
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

        var fields = "id,description,permalink_url,created_time";
        var detailsUrl = $"{GraphRoot}/{Uri.EscapeDataString(videoId)}?fields={Uri.EscapeDataString(fields)}" +
                         $"&access_token={Uri.EscapeDataString(pageAccessToken.Trim())}";
        using var detailsResponse = await _client.GetAsync(detailsUrl, cancellationToken);
        using var details = await ReadDocumentAsync(detailsResponse, cancellationToken);

        var likes = await FetchOptionalEdgeCountAsync(pageAccessToken, videoId, "likes", cancellationToken);
        var comments = await FetchOptionalEdgeCountAsync(pageAccessToken, videoId, "comments", cancellationToken);
        var shares = await FetchOptionalEdgeCountAsync(pageAccessToken, videoId, "sharedposts", cancellationToken);

        var insightsUrl = $"{GraphRoot}/{Uri.EscapeDataString(videoId)}/video_insights" +
                          $"?metric=blue_reels_play_count&access_token={Uri.EscapeDataString(pageAccessToken.Trim())}";
        using var insightsResponse = await _client.GetAsync(insightsUrl, cancellationToken);
        using var insights = await ReadDocumentAsync(insightsResponse, cancellationToken);

        return Parse(details.RootElement, insights.RootElement) with
        {
            Reactions = likes,
            Comments = comments,
            Shares = shares,
        };
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
            ReadInsight(insights, "blue_reels_play_count"),
            0,
            0,
            0);
    }

    private async Task<long> FetchOptionalEdgeCountAsync(
        string pageAccessToken,
        string videoId,
        string edge,
        CancellationToken cancellationToken)
    {
        var url = $"{GraphRoot}/{Uri.EscapeDataString(videoId)}/{edge}" +
                  $"?limit=0&summary=total_count&access_token={Uri.EscapeDataString(pageAccessToken.Trim())}";
        using var response = await _client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return 0;
        using var document = await ReadDocumentAsync(response, cancellationToken);
        return ReadEdgeSummaryCount(document.RootElement);
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

    internal static long ParseEdgeSummaryCount(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadEdgeSummaryCount(document.RootElement);
    }

    private static long ReadEdgeSummaryCount(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var summary) ||
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

    private static string? ReadAfterCursor(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging) ||
            !paging.TryGetProperty("cursors", out var cursors)) return null;
        var after = ReadString(cursors, "after");
        return after.Length == 0 ? null : after;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && DateTime.TryParse(value.GetString(), out var date) ? date : null;
}
