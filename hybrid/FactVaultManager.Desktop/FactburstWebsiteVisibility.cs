using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public static partial class FactburstWebsiteVisibility
{
    public static string DisplayState(string? status, string? publishAt, DateTimeOffset now)
    {
        if (string.Equals(status?.Trim(), "draft", StringComparison.OrdinalIgnoreCase))
            return "Offline";

        if (string.Equals(status?.Trim(), "published", StringComparison.OrdinalIgnoreCase))
        {
            if (DateTimeOffset.TryParse(
                    publishAt,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed) && parsed > now)
            {
                return "Upcoming";
            }

            return "Live";
        }

        return "Unknown";
    }

    public static bool IsOffline(string? status) =>
        string.Equals(status?.Trim(), "draft", StringComparison.OrdinalIgnoreCase);

    public static bool IsPublished(string? status) =>
        string.Equals(status?.Trim(), "published", StringComparison.OrdinalIgnoreCase);
}

public sealed class FactburstWebsiteVisibilityClient : IDisposable
{
    private static readonly Regex SlugPattern = new("^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.Compiled);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public FactburstWebsiteVisibilityClient()
        : this(new HttpClient(), ownsHttp: true)
    {
    }

    public FactburstWebsiteVisibilityClient(HttpClient httpClient)
        : this(httpClient, ownsHttp: false)
    {
    }

    private FactburstWebsiteVisibilityClient(HttpClient httpClient, bool ownsHttp)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttp = ownsHttp;
    }

    public Task SetLiveNowAsync(
        string baseUrl,
        string apiKey,
        string slug,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SetVisibilityAsync(baseUrl, apiKey, slug, "published", now.ToUniversalTime().ToString("O"), cancellationToken);

    public Task SetOfflineAsync(
        string baseUrl,
        string apiKey,
        string slug,
        string? preservedPublishAt,
        CancellationToken cancellationToken = default) =>
        SetVisibilityAsync(baseUrl, apiKey, slug, "draft", NormalizeExistingPublishAt(preservedPublishAt), cancellationToken);

    public Task FollowScheduleAsync(
        string baseUrl,
        string apiKey,
        string slug,
        DateTimeOffset publishAt,
        CancellationToken cancellationToken = default) =>
        SetVisibilityAsync(baseUrl, apiKey, slug, "published", publishAt.ToUniversalTime().ToString("O"), cancellationToken);

    public async Task SetVisibilityAsync(
        string baseUrl,
        string apiKey,
        string slug,
        string status,
        string? publishAt,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(baseUrl, slug);
        var key = (apiKey ?? "").Trim();
        if (key.Length < 16)
            throw new InvalidOperationException("The Website Link Tracker API key is missing or invalid.");

        var normalizedStatus = (status ?? "").Trim().ToLowerInvariant();
        if (normalizedStatus is not ("draft" or "published"))
            throw new ArgumentException("Website status must be draft or published.", nameof(status));

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["status"] = normalizedStatus,
            ["publish_at"] = publishAt,
        });

        using var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        detail = ExtractError(detail);
        throw new HttpRequestException(
            $"Website visibility update failed ({(int)response.StatusCode} {response.ReasonPhrase})." +
            (detail.Length > 0 ? " " + detail : ""),
            null,
            response.StatusCode);
    }

    internal static Uri BuildEndpoint(string baseUrl, string slug)
    {
        var rawBase = (baseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(rawBase, UriKind.Absolute, out var root) ||
            !string.Equals(root.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Website Link Tracker URL must be a valid HTTPS address.");
        }

        var normalizedSlug = (slug ?? "").Trim().ToLowerInvariant();
        if (!SlugPattern.IsMatch(normalizedSlug))
            throw new ArgumentException("The website quiz slug is invalid.", nameof(slug));

        return new Uri(root, $"/api/site/quizzes/{Uri.EscapeDataString(normalizedSlug)}");
    }

    private static string? NormalizeExistingPublishAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTimeOffset.TryParse(value, out var parsed)) return null;
        return parsed.ToUniversalTime().ToString("O");
    }

    private static string ExtractError(string? responseBody)
    {
        var text = (responseBody ?? "").Trim();
        if (text.Length == 0) return "";
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("error", out var error))
                return (error.GetString() ?? "").Trim();
        }
        catch (JsonException)
        {
        }
        return text.Length > 400 ? text[..400] : text;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
