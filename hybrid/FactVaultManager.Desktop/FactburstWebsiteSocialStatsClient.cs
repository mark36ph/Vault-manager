using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactVaultManager.Desktop;

public sealed record FactburstSocialStatsRecord(
    [property: JsonPropertyName("quiz_slug")] string QuizSlug,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("platform_id")] string PlatformId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("scheduled_for")] string? ScheduledFor,
    [property: JsonPropertyName("published_at")] string? PublishedAt,
    [property: JsonPropertyName("views")] long Views,
    [property: JsonPropertyName("likes")] long Likes,
    [property: JsonPropertyName("comments")] long Comments,
    [property: JsonPropertyName("shares")] long Shares,
    [property: JsonPropertyName("reactions")] long Reactions,
    [property: JsonPropertyName("captured_at")] string CapturedAt,
    [property: JsonPropertyName("error_message")] string ErrorMessage = "");

public sealed class FactburstWebsiteSocialStatsClient : IDisposable
{
    public const string DefaultWebsiteBaseUrl = "https://factburstquiz.com";
    private readonly HttpClient _client;

    public FactburstWebsiteSocialStatsClient(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task PushAsync(string apiKey, IEnumerable<FactburstSocialStatsRecord> records, CancellationToken cancellationToken = default)
    {
        var key = RequireApiKey(apiKey);
        var payload = records?.ToArray() ?? Array.Empty<FactburstSocialStatsRecord>();
        if (payload.Length == 0) return;
        if (payload.Length > 100) throw new ArgumentException("A maximum of 100 social stat records can be sent at once.", nameof(records));
        using var request = new HttpRequestMessage(HttpMethod.Post, DefaultWebsiteBaseUrl + "/api/social/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(new { stats = payload }), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(ParseError(body, response.StatusCode));
    }

    public static FactburstSocialStatsRecord FromHistory(QuizHistorySummary history, string platform, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(history);
        var normalizedPlatform = NormalizePlatform(platform);
        var status = normalizedPlatform switch
        {
            "youtube" => history.YouTubeIsScheduled ? "scheduled" : history.PublishedOnYouTube ? "published" : "unknown",
            "facebook" => history.FacebookIsScheduled ? "scheduled" : history.PublishedOnFacebook ? "published" : "unknown",
            "instagram" => history.PublishedOnInstagram ? "published" : "unknown",
            _ => "unknown",
        };
        var scheduled = normalizedPlatform switch
        {
            "youtube" => history.YouTubeScheduledFor,
            "facebook" => history.FacebookScheduledFor,
            _ => "",
        };
        var url = normalizedPlatform switch
        {
            "youtube" => history.YouTubeUrl,
            "facebook" => history.FacebookUrl,
            "instagram" => history.InstagramUrl,
            _ => "",
        };
        var id = normalizedPlatform == "youtube" ? YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl) ?? "" : "";
        return new FactburstSocialStatsRecord(
            FactburstLinkTrackerClient.CampaignSlug(history), normalizedPlatform, id, url.Trim(), status,
            ParseDate(scheduled),
            ParseDate(normalizedPlatform == "youtube" ? history.YouTubeUploadDate : normalizedPlatform == "facebook" ? history.FacebookUploadDate : history.InstagramUploadDate),
            Math.Max(0, normalizedPlatform == "youtube" ? history.YouTubeViews : normalizedPlatform == "facebook" ? history.FacebookViews : 0),
            Math.Max(0, normalizedPlatform == "youtube" ? history.YouTubeLikes : 0),
            Math.Max(0, normalizedPlatform == "facebook" ? history.FacebookComments : 0),
            Math.Max(0, normalizedPlatform == "facebook" ? history.FacebookShares : 0),
            Math.Max(0, normalizedPlatform == "facebook" ? history.FacebookReactions : 0),
            capturedAt.ToUniversalTime().ToString("O"), "");
    }

    public void Dispose() => _client.Dispose();

    private static string NormalizePlatform(string value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "yt" or "youtube-promo" => "youtube",
        "fb" => "facebook",
        "ig" => "instagram",
        "youtube" or "facebook" or "instagram" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("Platform must be YouTube, Facebook, or Instagram.", nameof(value)),
    };

    private static string? ParseDate(string? value)
    {
        var text = (value ?? "").Trim();
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUniversalTime().ToString("O") : null;
    }

    private static string RequireApiKey(string value)
    {
        var key = (value ?? "").Trim();
        if (key.Length < 16) throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        return key;
    }

    private static string ParseError(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString()?.Trim() ?? $"Website social stats returned HTTP {(int)status}.";
        }
        catch (JsonException) { }
        return $"Website social stats returned HTTP {(int)status}.";
    }
}
