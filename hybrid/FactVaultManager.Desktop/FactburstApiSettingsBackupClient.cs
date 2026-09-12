using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactVaultManager.Desktop;

public sealed class FactburstApiSettingsBackupClient : IDisposable
{
    public const string DefaultWebsiteBaseUrl = "https://factburstquiz.com";
    public const string DirectWorkerBaseUrl = "https://factburst-quiz-site.factburstquiz.workers.dev";
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task BackupAsync(string trackerApiKey, AppSettingsModel settings, string socialStatsApiKey = "", string websiteBaseUrl = DefaultWebsiteBaseUrl, CancellationToken cancellationToken = default)
    {
        var key = (trackerApiKey ?? "").Trim();
        if (key.Length < 16) throw new InvalidOperationException("Add the website tracker API key before backing up API settings.");
        var baseUrl = ValidateBaseUrl(websiteBaseUrl);
        var payload = new BackupSettings("https://go.factburstquiz.com", key, settings.OpenAiKey, settings.OpenAiModel, settings.YouTubeApiKey, settings.YouTubeOAuthClientId, settings.YouTubeOAuthClientSecret, settings.YouTubeOAuthRefreshToken, settings.ApprovedYouTubeChannelId, settings.ApprovedYouTubeChannelName, settings.FacebookPageAccessToken, settings.ApprovedFacebookPageId, settings.ApprovedFacebookPageName, settings.InstagramAccessToken, socialStatsApiKey);
        var json = JsonSerializer.Serialize(new { settings = payload });
        var response = await SendBackupRequestAsync(baseUrl, key, json, cancellationToken);
        if ((int)response.StatusCode == 404 && !string.Equals(baseUrl, DirectWorkerBaseUrl, StringComparison.OrdinalIgnoreCase)) { response.Dispose(); response = await SendBackupRequestAsync(DirectWorkerBaseUrl, key, json, cancellationToken); }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException(ParseError(body, response.StatusCode));
        }
    }

    public async Task<FactburstApiSettingsRestoreResult> RestoreAsync(string siteAdminKey, string websiteBaseUrl = DefaultWebsiteBaseUrl, CancellationToken cancellationToken = default)
    {
        var key = (siteAdminKey ?? "").Trim();
        if (key.Length < 16) throw new InvalidOperationException("Enter the Cloudflare site administrator key to restore API settings.");
        var baseUrl = ValidateBaseUrl(websiteBaseUrl);
        var response = await SendRestoreRequestAsync(baseUrl, key, cancellationToken);
        if ((int)response.StatusCode == 404 && !string.Equals(baseUrl, DirectWorkerBaseUrl, StringComparison.OrdinalIgnoreCase)) { response.Dispose(); response = await SendRestoreRequestAsync(DirectWorkerBaseUrl, key, cancellationToken); }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException(ParseError(body, response.StatusCode));
            var result = JsonSerializer.Deserialize<RestoreResponse>(body);
            if (result?.Settings is null) throw new InvalidOperationException("Cloudflare did not return a valid API settings backup.");
            return new FactburstApiSettingsRestoreResult(result.Settings.ToModel(), result.Settings.TrackerBaseUrl, result.Settings.TrackerApiKey, result.Settings.SocialStatsApiKey);
        }
    }

    private async Task<HttpResponseMessage> SendBackupRequestAsync(string baseUrl, string key, string json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/admin/api-settings/backup");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRestoreRequestAsync(string baseUrl, string key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/admin/api-settings/restore");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _client.SendAsync(request, cancellationToken);
    }

    private static string ValidateBaseUrl(string? websiteBaseUrl)
    {
        var baseUrl = (websiteBaseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new InvalidOperationException("The Factburst website base URL is not valid.");
        return baseUrl;
    }

    public void Dispose() => _client.Dispose();

    private static string ParseError(string body, System.Net.HttpStatusCode status)
    {
        try { using var document = JsonDocument.Parse(body); if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) return error.GetString()?.Trim() ?? $"API settings request returned HTTP {(int)status}."; }
        catch (JsonException) { }
        return $"API settings request returned HTTP {(int)status}.";
    }

    private sealed record BackupSettings(
        [property: JsonPropertyName("tracker_base_url")] string TrackerBaseUrl,
        [property: JsonPropertyName("tracker_api_key")] string TrackerApiKey,
        [property: JsonPropertyName("openai_api_key")] string OpenAiApiKey,
        [property: JsonPropertyName("openai_model")] string OpenAiModel,
        [property: JsonPropertyName("youtube_api_key")] string YouTubeApiKey,
        [property: JsonPropertyName("youtube_oauth_client_id")] string YouTubeOAuthClientId,
        [property: JsonPropertyName("youtube_oauth_client_secret")] string YouTubeOAuthClientSecret,
        [property: JsonPropertyName("youtube_oauth_refresh_token")] string YouTubeOAuthRefreshToken,
        [property: JsonPropertyName("youtube_approved_channel_id")] string YouTubeApprovedChannelId,
        [property: JsonPropertyName("youtube_approved_channel_name")] string YouTubeApprovedChannelName,
        [property: JsonPropertyName("facebook_page_access_token")] string FacebookPageAccessToken,
        [property: JsonPropertyName("facebook_approved_page_id")] string FacebookPageId,
        [property: JsonPropertyName("facebook_approved_page_name")] string FacebookPageName,
        [property: JsonPropertyName("instagram_access_token")] string InstagramAccessToken,
        [property: JsonPropertyName("social_stats_api_key")] string SocialStatsApiKey);

    private sealed record RestoreResponse(
        [property: JsonPropertyName("configured")] bool Configured,
        [property: JsonPropertyName("backed_up_at")] string? BackedUpAt,
        [property: JsonPropertyName("settings")] RestoredSettings? Settings);

    private sealed record RestoredSettings(
        [property: JsonPropertyName("tracker_base_url")] string TrackerBaseUrl,
        [property: JsonPropertyName("tracker_api_key")] string TrackerApiKey,
        [property: JsonPropertyName("openai_api_key")] string OpenAiApiKey,
        [property: JsonPropertyName("openai_model")] string OpenAiModel,
        [property: JsonPropertyName("youtube_api_key")] string YouTubeApiKey,
        [property: JsonPropertyName("youtube_oauth_client_id")] string YouTubeOAuthClientId,
        [property: JsonPropertyName("youtube_oauth_client_secret")] string YouTubeOAuthClientSecret,
        [property: JsonPropertyName("youtube_oauth_refresh_token")] string YouTubeOAuthRefreshToken,
        [property: JsonPropertyName("youtube_approved_channel_id")] string YouTubeApprovedChannelId,
        [property: JsonPropertyName("youtube_approved_channel_name")] string YouTubeApprovedChannelName,
        [property: JsonPropertyName("facebook_page_access_token")] string FacebookPageAccessToken,
        [property: JsonPropertyName("facebook_approved_page_id")] string FacebookPageId,
        [property: JsonPropertyName("facebook_approved_page_name")] string FacebookPageName,
        [property: JsonPropertyName("instagram_access_token")] string InstagramAccessToken,
        [property: JsonPropertyName("social_stats_api_key")] string SocialStatsApiKey)
    {
        public AppSettingsModel ToModel() => new()
        {
            OpenAiKey = OpenAiApiKey, OpenAiModel = OpenAiModel,
            YouTubeApiKey = YouTubeApiKey, YouTubeOAuthClientId = YouTubeOAuthClientId, YouTubeOAuthClientSecret = YouTubeOAuthClientSecret, YouTubeOAuthRefreshToken = YouTubeOAuthRefreshToken, ApprovedYouTubeChannelId = YouTubeApprovedChannelId, ApprovedYouTubeChannelName = YouTubeApprovedChannelName,
            FacebookPageAccessToken = FacebookPageAccessToken, ApprovedFacebookPageId = FacebookPageId, ApprovedFacebookPageName = FacebookPageName,
            InstagramAccessToken = InstagramAccessToken,
        };
    }
}

public sealed record FactburstApiSettingsRestoreResult(AppSettingsModel Settings, string TrackerBaseUrl, string TrackerApiKey, string SocialStatsApiKey);
