using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteMaintenanceSettings(bool Enabled, string Message, string UpdatedAt);
public sealed record FactburstWebsiteUserAccess(int Id, string Username, string Role, string Status);

public sealed class FactburstWebsiteAccessAdminClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstWebsiteAccessAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
    }

    public async Task<FactburstWebsiteMaintenanceSettings> GetMaintenanceAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, Endpoint(baseUrl, "/api/site/settings"), apiKey, null, cancellationToken);
        if (!document.RootElement.TryGetProperty("maintenance", out var maintenance))
            throw new InvalidOperationException("Website maintenance settings were not returned by the server.");
        return new FactburstWebsiteMaintenanceSettings(
            ReadBool(maintenance, "enabled"),
            ReadString(maintenance, "message"),
            ReadString(maintenance, "updated_at"));
    }

    public async Task<FactburstWebsiteMaintenanceSettings> SetMaintenanceAsync(
        string baseUrl,
        string apiKey,
        bool enabled,
        string message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { enabled, message = (message ?? "").Trim() });
        using var document = await SendAsync(HttpMethod.Patch, Endpoint(baseUrl, "/api/site/settings"), apiKey, payload, cancellationToken);
        var maintenance = document.RootElement.GetProperty("maintenance");
        return new FactburstWebsiteMaintenanceSettings(
            ReadBool(maintenance, "enabled"),
            ReadString(maintenance, "message"),
            ReadString(maintenance, "updated_at"));
    }

    public async Task<FactburstWebsiteUserAccess> GetUserAccessAsync(
        string baseUrl,
        string apiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        using var document = await SendAsync(HttpMethod.Get, Endpoint(baseUrl, $"/api/site/users/{userId}/access"), apiKey, null, cancellationToken);
        return ParseUser(document.RootElement.GetProperty("user"));
    }

    public async Task<FactburstWebsiteUserAccess> SetUserRoleAsync(
        string baseUrl,
        string apiKey,
        int userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        var normalized = (role ?? "").Trim().ToLowerInvariant();
        if (normalized is not ("user" or "moderator" or "admin"))
            throw new ArgumentException("Role must be User, Moderator or Admin.", nameof(role));
        var payload = JsonSerializer.Serialize(new { role = normalized });
        using var document = await SendAsync(HttpMethod.Patch, Endpoint(baseUrl, $"/api/site/users/{userId}/access"), apiKey, payload, cancellationToken);
        return ParseUser(document.RootElement.GetProperty("user"));
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string url,
        string apiKey,
        string? json,
        CancellationToken cancellationToken)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
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
                $"Website administration request failed (HTTP {(int)response.StatusCode})" +
                (detail.Length == 0 ? "." : $": {detail}"));
        }
        return JsonDocument.Parse(body);
    }

    private static FactburstWebsiteUserAccess ParseUser(JsonElement user) => new(
        ReadInt(user, "id"),
        ReadString(user, "username"),
        ReadString(user, "role") is { Length: > 0 } role ? role : "user",
        ReadString(user, "status"));

    private static string Endpoint(string baseUrl, string path) =>
        $"{FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl)}{path}";

    private static void ValidateUserId(int userId)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
    }

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int ReadInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return 0;
    }

    private static bool ReadBool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
}
