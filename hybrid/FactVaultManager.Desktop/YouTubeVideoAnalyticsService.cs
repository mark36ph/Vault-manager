using System.Globalization;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeVideoAnalytics(
    string VideoId,
    long Views,
    long Likes,
    DateTime? PublishedAt);

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
                             "&fields=items(id%2Csnippet%2FpublishedAt%2Cstatistics%2FviewCount%2Cstatistics%2FlikeCount)" +
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
            if (item.TryGetProperty("statistics", out var statistics))
            {
                views = ReadMetric(statistics, "viewCount");
                likes = ReadMetric(statistics, "likeCount");
            }

            DateTime? publishedAt = null;
            if (item.TryGetProperty("snippet", out var snippet) &&
                snippet.TryGetProperty("publishedAt", out var publishedElement) &&
                DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed))
                publishedAt = parsed.UtcDateTime;

            results.Add(new YouTubeVideoAnalytics(id, views, likes, publishedAt));
        }

        return results;
    }

    private static long ReadMetric(JsonElement statistics, string propertyName) =>
        statistics.TryGetProperty(propertyName, out var element) &&
        long.TryParse(element.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 0;
}
