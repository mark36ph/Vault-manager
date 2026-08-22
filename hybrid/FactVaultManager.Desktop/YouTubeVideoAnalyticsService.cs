using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeVideoAnalytics(
    string VideoId,
    long Views,
    long Likes,
    DateTime? PublishedAt,
    long Comments = 0,
    string ChannelId = "",
    string Title = "");

public sealed record YouTubeChannelAnalytics(
    string ChannelId,
    string Title,
    long Views,
    long Videos,
    long? Subscribers);

public sealed class YouTubeVideoAnalyticsService
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private readonly HttpClient _client;

    public YouTubeVideoAnalyticsService(HttpClient? client = null) =>
        _client = client ?? SharedClient;

    public static string? TryGetVideoId(string? youtubeUrl)
    {
        if (!Uri.TryCreate((youtubeUrl ?? "").Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;

        var host = uri.Host.TrimEnd('.');
        string? candidate = null;
        if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "www.youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            candidate = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        else if (string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 &&
                (string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase)))
            {
                candidate = segments[1];
            }
            else
            {
                foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Split('=', 2);
                    if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "v", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = Uri.UnescapeDataString(pair[1]);
                        break;
                    }
                }
            }
        }

        candidate = candidate?.Trim();
        return candidate is { Length: >= 6 and <= 32 } &&
               candidate.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            ? candidate
            : null;
    }

    public async Task<IReadOnlyDictionary<string, YouTubeVideoAnalytics>> FetchAsync(
        string apiKey,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Add a YouTube Data API key in Settings first.", nameof(apiKey));

        var ids = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = new Dictionary<string, YouTubeVideoAnalytics>(StringComparer.Ordinal);

        for (var offset = 0; offset < ids.Length; offset += 50)
        {
            var batch = ids.Skip(offset).Take(50);
            var requestUri = "https://www.googleapis.com/youtube/v3/videos" +
                             "?part=snippet%2Cstatistics" +
                             "&fields=items(id%2Csnippet%2FchannelId%2Csnippet%2FpublishedAt%2Csnippet%2Ftitle%2Cstatistics%2FcommentCount%2Cstatistics%2FviewCount%2Cstatistics%2FlikeCount)" +
                             $"&id={Uri.EscapeDataString(string.Join(',', batch))}" +
                             $"&key={Uri.EscapeDataString(apiKey.Trim())}";

            using var response = await _client.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"YouTube could not update analytics (HTTP {(int)response.StatusCode}). Check the API key and that YouTube Data API v3 is enabled.");

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (var analytics in ParseResponse(json))
                results[analytics.VideoId] = analytics;
        }

        return results;
    }

    public async Task<YouTubeChannelAnalytics?> FetchChannelAsync(
        string apiKey,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Add a YouTube Data API key in Settings first.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        var requestUri = "https://www.googleapis.com/youtube/v3/channels" +
                         "?part=snippet%2Cstatistics" +
                         "&fields=items(id%2Csnippet%2Ftitle%2Cstatistics%2FhiddenSubscriberCount%2Cstatistics%2FsubscriberCount%2Cstatistics%2FvideoCount%2Cstatistics%2FviewCount)" +
                         $"&id={Uri.EscapeDataString(channelId.Trim())}" +
                         $"&key={Uri.EscapeDataString(apiKey.Trim())}";

        using var response = await _client.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"YouTube could not update channel analytics (HTTP {(int)response.StatusCode}). Check the API key and that YouTube Data API v3 is enabled.");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseChannelResponse(json);
    }

    public static IReadOnlyList<YouTubeVideoAnalytics> ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<YouTubeVideoAnalytics>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement)) continue;
            var id = idElement.GetString()?.Trim() ?? "";
            if (id.Length == 0) continue;

            long views = 0;
            long likes = 0;
            long comments = 0;
            if (item.TryGetProperty("statistics", out var statistics))
            {
                views = ReadMetric(statistics, "viewCount");
                likes = ReadMetric(statistics, "likeCount");
                comments = ReadMetric(statistics, "commentCount");
            }

            DateTime? publishedAt = null;
            var channelId = "";
            var title = "";
            if (item.TryGetProperty("snippet", out var snippet) &&
                snippet.ValueKind == JsonValueKind.Object)
            {
                if (snippet.TryGetProperty("publishedAt", out var publishedElement) &&
                    DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed))
                    publishedAt = parsed.UtcDateTime;
                if (snippet.TryGetProperty("channelId", out var channelElement))
                    channelId = channelElement.GetString()?.Trim() ?? "";
                if (snippet.TryGetProperty("title", out var titleElement))
                    title = titleElement.GetString()?.Trim() ?? "";
            }

            results.Add(new YouTubeVideoAnalytics(id, views, likes, publishedAt, comments, channelId, title));
        }

        return results;
    }

    public static YouTubeChannelAnalytics? ParseChannelResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return null;

        var item = items[0];
        var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString()?.Trim() ?? "" : "";
        var title = item.TryGetProperty("snippet", out var snippet) &&
                    snippet.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()?.Trim() ?? ""
            : "";
        if (!item.TryGetProperty("statistics", out var statistics))
            return new YouTubeChannelAnalytics(id, title, 0, 0, null);

        var hiddenSubscribers = statistics.TryGetProperty("hiddenSubscriberCount", out var hiddenElement) &&
                                hiddenElement.ValueKind == JsonValueKind.True;
        return new YouTubeChannelAnalytics(
            id,
            title,
            ReadMetric(statistics, "viewCount"),
            ReadMetric(statistics, "videoCount"),
            hiddenSubscribers ? null : ReadOptionalMetric(statistics, "subscriberCount"));
    }

    private static long ReadMetric(JsonElement statistics, string propertyName) =>
        statistics.TryGetProperty(propertyName, out var element) &&
        long.TryParse(element.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 0;

    private static long? ReadOptionalMetric(JsonElement statistics, string propertyName) =>
        statistics.TryGetProperty(propertyName, out var element) &&
        long.TryParse(element.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
}
