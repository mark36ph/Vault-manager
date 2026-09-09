using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactVaultManager.Desktop;

public sealed class FactburstApiSettingsBackupClient : IDisposable
{
    public const string DefaultWebsiteBaseUrl = "https://factburstquiz.com";
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task BackupAsync(string trackerApiKey, AppSettingsModel settings, string websiteBaseUrl = DefaultWebsiteBaseUrl, CancellationToken cancellationToken = default)
    {
        var key = (trackerApiKey ?? "").Trim();
        if (key.Length < 16) throw new InvalidOperationException("Add the website tracker API key before backing up API settings.");
        var baseUrl = (websiteBaseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new InvalidOperationException("The Factburst website base URL is not valid.");

        var payload = new BackupSettings(
            "https://go.factburstquiz.com",
            key,
            settings.OpenAiKey,
            settings.OpenAiModel,
            settings.PexelsKey,
            settings.PixabayKey,
            settings.YouTubeApiKey,
            settings.YouTubeOAuthClientId,
            settings.YouTubeOAuthClientSecret,
            settings.YouTubeOAuthRefreshToken,
            settings.ApprovedYouTubeChannelId,
            settings.ApprovedYouTubeChannelName,
            settings.FacebookPageAccessToken,
            settings.ApprovedFacebookPageId,
            settings.ApprovedFacebookPageName,
            settings.InstagramAccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/admin/api-settings/backup");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(new { settings = payload }), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(ParseError(body, response.StatusCode));
    }

    public void Dispose() => _client.Dispose();

    private static string ParseError(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString()?.Trim() ?? $"API settings backup returned HTTP {(int)status}.";
        }
        catch (JsonException) { }
        return $"API settings backup returned HTTP {(int)status}.";
    }

    private sealed record BackupSettings(
        [property: JsonPropertyName("tracker_base_url")] string TrackerBaseUrl,
        [property: JsonPropertyName("tracker_api_key")] string TrackerApiKey,
        [property: JsonPropertyName("openai_api_key")] string OpenAiApiKey,
        [property: JsonPropertyName("openai_model")] string OpenAiModel,
        [property: JsonPropertyName("pexels_api_key")] string PexelsApiKey,
        [property: JsonPropertyName("pixabay_api_key")] string PixabayApiKey,
        [property: JsonPropertyName("youtube_api_key")] string YouTubeApiKey,
        [property: JsonPropertyName("youtube_oauth_client_id")] string YouTubeOAuthClientId,
        [property: JsonPropertyName("youtube_oauth_client_secret")] string YouTubeOAuthClientSecret,
        [property: JsonPropertyName("youtube_oauth_refresh_token")] string YouTubeOAuthRefreshToken,
        [property: JsonPropertyName("youtube_approved_channel_id")] string YouTubeApprovedChannelId,
        [property: JsonPropertyName("youtube_approved_channel_name")] string YouTubeApprovedChannelName,
        [property: JsonPropertyName("facebook_page_access_token")] string FacebookPageAccessToken,
        [property: JsonPropertyName("facebook_approved_page_id")] string FacebookApprovedPageId,
        [property: JsonPropertyName("facebook_approved_page_name")] string FacebookApprovedPageName,
        [property: JsonPropertyName("instagram_access_token")] string InstagramAccessToken);
}
