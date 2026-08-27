using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed record FactburstTrackerCampaignLinks(
    string Slug,
    string FacebookUrl,
    string InstagramUrl,
    string YouTubePromoUrl);

public sealed record FactburstTrackerCampaignStats(
    string Slug,
    int? QuizId,
    string Title,
    long FacebookClicks,
    long InstagramClicks,
    long YouTubePromoClicks,
    long TotalClicks,
    long RawHits,
    bool FilteringEnabled,
    int DedupeHours);

public static class FactburstFunnelClassifier
{
    public static string Label(long trackedClicks, long longFormViews, bool promoCreated)
    {
        if (!promoCreated) return "Create promo";
        if (trackedClicks <= 0) return "Waiting for tracked clicks";
        if (trackedClicks >= 50 && longFormViews < trackedClicks) return "High promo traffic / check long-form";
        if (trackedClicks >= 20) return "Promo gaining traction";
        return "Early data";
    }
}

public sealed class FactburstLinkTrackerClient
{
    private const string FilteredTrackingMode = "filtered_unique_v2";
    private static readonly Regex UrlRegex = new(@"https://[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstLinkTrackerClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public static bool IsConfigured(string? baseUrl, string? apiKey) =>
        !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey);

    public static string NormalizeBaseUrl(string? value)
    {
        var text = (value ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Length == 0)
        {
            throw new ArgumentException("Tracker URL must be a complete HTTPS address, for example https://tracker.example.workers.dev.");
        }

        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Tracker URL must not contain a query string or fragment.");

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static string CampaignSlug(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var source = history.SeriesName.Trim();
        if (source.EndsWith(" Quiz", StringComparison.OrdinalIgnoreCase))
            source = source[..^5].Trim();
        if (source.Length == 0)
            source = history.AnalyticsCategory.Trim();
        if (source.Length == 0)
            source = "quiz";

        var normalized = Regex.Replace(source.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (normalized.Length == 0) normalized = "quiz";
        var suffix = history.EpisodeNumber > 0
            ? history.EpisodeNumber.ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            : Math.Max(1, history.Id).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var maximumBase = Math.Max(1, 80 - suffix.Length - 1);
        if (normalized.Length > maximumBase)
            normalized = normalized[..maximumBase].TrimEnd('-');
        return $"{normalized}-{suffix}";
    }

    public static FactburstTrackerCampaignLinks BuildLinks(string baseUrl, string slug)
    {
        var root = NormalizeBaseUrl(baseUrl);
        var safeSlug = ValidateSlug(slug);
        return new FactburstTrackerCampaignLinks(
            safeSlug,
            $"{root}/fb/{safeSlug}",
            $"{root}/ig/{safeSlug}",
            $"{root}/yt/{safeSlug}");
    }

    public static string ReplaceFullQuizLink(string? description, string trackedUrl)
    {
        var text = (description ?? "").Trim();
        var replacement = NormalizeTrackedUrl(trackedUrl);
        var replaced = false;
        var result = UrlRegex.Replace(text, match =>
        {
            if (replaced) return match.Value;
            var candidate = match.Value.TrimEnd('.', ',', ';', '!', '?', ')', ']', '}');
            var trailing = match.Value[candidate.Length..];
            if (YouTubeVideoAnalyticsService.TryGetVideoId(candidate) is null)
                return match.Value;
            replaced = true;
            return replacement + trailing;
        });

        if (replaced) return result;
        return text.Length == 0
            ? "Watch the full quiz: " + replacement
            : text + Environment.NewLine + Environment.NewLine + "Watch the full quiz: " + replacement;
    }

    public async Task<bool> HealthAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        var root = NormalizeBaseUrl(baseUrl);
        using var response = await _client.GetAsync(root + "/health", cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    }

    public async Task<FactburstTrackerCampaignLinks> CreateOrUpdateCampaignAsync(
        string baseUrl,
        string apiKey,
        string slug,
        int quizId,
        string title,
        string destinationUrl,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeBaseUrl(baseUrl);
        var safeSlug = ValidateSlug(slug);
        var key = RequireApiKey(apiKey);
        var destination = QuizYouTubePublication.NormalizeUrl(destinationUrl);
        if (destination.Length == 0)
            throw new ArgumentException("The full YouTube quiz link is required before creating a tracking campaign.");

        var payload = JsonSerializer.Serialize(new
        {
            slug = safeSlug,
            quiz_id = quizId > 0 ? (int?)quizId : null,
            title = (title ?? "").Trim(),
            destination_url = destination,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, root + "/api/campaigns");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "The Factburst tracker could not create the campaign", cancellationToken);
        return BuildLinks(root, safeSlug);
    }

    public async Task<IReadOnlyList<FactburstTrackerCampaignStats>> FetchStatsAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, root + "/api/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "The Factburst tracker could not load analytics", cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var rootElement = document.RootElement;
        if (!rootElement.TryGetProperty("campaigns", out var campaigns) || campaigns.ValueKind != JsonValueKind.Array)
            return Array.Empty<FactburstTrackerCampaignStats>();

        var filteringEnabled = string.Equals(
            ReadString(rootElement, "tracking_mode"),
            FilteredTrackingMode,
            StringComparison.OrdinalIgnoreCase);
        var dedupeHours = filteringEnabled ? Math.Max(1, ReadInt(rootElement, "dedupe_hours", 6)) : 0;

        var results = new List<FactburstTrackerCampaignStats>();
        foreach (var item in campaigns.EnumerateArray())
        {
            var slugValue = ReadString(item, "slug");
            if (slugValue.Length == 0) continue;

            var facebook = ReadLong(item, filteringEnabled ? "facebook_visitors" : "facebook_clicks");
            var instagram = ReadLong(item, filteringEnabled ? "instagram_visitors" : "instagram_clicks");
            var youtube = ReadLong(item, filteringEnabled ? "youtube_promo_visitors" : "youtube_promo_clicks");
            var total = ReadLong(item, filteringEnabled ? "unique_visitors" : "total_clicks");
            var rawHits = filteringEnabled ? ReadLong(item, "raw_hits") : total;

            results.Add(new FactburstTrackerCampaignStats(
                slugValue,
                ReadNullableInt(item, "quiz_id"),
                ReadString(item, "title"),
                facebook,
                instagram,
                youtube,
                total,
                rawHits,
                filteringEnabled,
                dedupeHours));
        }
        return results;
    }

    private static string ValidateSlug(string? value)
    {
        var slug = (value ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{0,79}$"))
            throw new ArgumentException("Tracker campaign slug may contain only lowercase letters, numbers and hyphens.");
        return slug;
    }

    private static string NormalizeTrackedUrl(string value)
    {
        if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Tracked link must be a complete HTTPS URL.");
        return uri.AbsoluteUri;
    }

    private static string RequireApiKey(string? value)
    {
        var key = (value ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        return key;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                detail = error.GetString()?.Trim() ?? "";
        }
        catch (JsonException)
        {
        }
        throw new InvalidOperationException(
            $"{fallback} (HTTP {(int)response.StatusCode})" + (detail.Length == 0 ? "." : $": {detail}"));
    }

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int? ReadNullableInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return null;
    }

    private static int ReadInt(JsonElement item, string name, int fallback)
    {
        if (!item.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return fallback;
    }

    private static long ReadLong(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return Math.Max(0, number);
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return Math.Max(0, number);
        return 0;
    }
}
