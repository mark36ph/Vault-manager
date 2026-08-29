using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteAdsSettings(
    bool Enabled,
    string Client,
    string LeftSlot,
    string RightSlot);

public sealed class FactburstWebsiteAdsAdminClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstWebsiteAdsAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
    }

    public async Task<FactburstWebsiteAdsSettings> FetchAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        using var request = CreateRequest(HttpMethod.Get, root + "/api/site/ads", apiKey);
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessAsync(response, "Website ad settings could not be loaded", cancellationToken);
        return Parse(document.RootElement);
    }

    public async Task<FactburstWebsiteAdsSettings> SaveAsync(
        string baseUrl,
        string apiKey,
        FactburstWebsiteAdsSettings settings,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        using var request = CreateRequest(HttpMethod.Patch, root + "/api/site/ads", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            enabled = settings.Enabled,
            client = settings.Client.Trim(),
            left_slot = settings.LeftSlot.Trim(),
            right_slot = settings.RightSlot.Trim(),
        }), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessAsync(response, "Website ad settings could not be saved", cancellationToken);
        return Parse(document.RootElement);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static FactburstWebsiteAdsSettings Parse(JsonElement root) => new(
        ReadBool(root, "enabled"),
        ReadString(root, "client"),
        ReadString(root, "left_slot"),
        ReadString(root, "right_slot"));

    private static async Task<JsonDocument> ReadSuccessAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
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
                $"{fallback} (HTTP {(int)response.StatusCode})" + (detail.Length == 0 ? "." : $": {detail}"));
        }
        return JsonDocument.Parse(body);
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed));
}
