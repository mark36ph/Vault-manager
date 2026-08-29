using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteProvisionedUser(
    int UserId,
    string Username,
    string Email,
    bool EmailVerified);

public sealed class FactburstWebsiteUserProvisioningClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly HttpClient _client;

    public FactburstWebsiteUserProvisioningClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
        // The default client is shared for the lifetime of the app. Injected clients are owned by the caller.
    }

    public async Task<FactburstWebsiteProvisionedUser> CreateUserAsync(
        string trackerBaseUrl,
        string apiKey,
        string username,
        string email,
        string password,
        bool activateImmediately = true,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(trackerBaseUrl);
        var key = RequireApiKey(apiKey);
        var cleanUsername = (username ?? "").Trim();
        var cleanEmail = (email ?? "").Trim();
        var cleanPassword = password ?? "";
        if (cleanUsername.Length == 0) throw new ArgumentException("Enter a username.", nameof(username));
        if (cleanEmail.Length == 0) throw new ArgumentException("Enter an email address.", nameof(email));
        if (cleanPassword.Length < 10) throw new ArgumentException("Use a password with at least 10 characters.", nameof(password));

        var provisionPayload = JsonSerializer.Serialize(new
        {
            username = cleanUsername,
            email = cleanEmail,
            email_verified = activateImmediately,
        });
        using var provisionRequest = CreateAdminRequest(HttpMethod.Post, root + "/api/site/users/provision", key);
        provisionRequest.Content = new StringContent(provisionPayload, Encoding.UTF8, "application/json");
        using var provisionResponse = await _client.SendAsync(provisionRequest, cancellationToken);
        using var provisionDocument = await ReadSuccessJsonAsync(
            provisionResponse,
            "The admin signup authorization could not be created",
            cancellationToken);

        var provision = provisionDocument.RootElement;
        var signupToken = ReadString(provision, "signup_token");
        var signupUrl = ReadString(provision, "signup_url");
        var origin = ReadString(provision, "origin");
        if (signupToken.Length == 0 || signupUrl.Length == 0 || origin.Length == 0)
            throw new InvalidOperationException("The admin signup authorization response was incomplete.");

        var signupPayload = JsonSerializer.Serialize(new
        {
            username = cleanUsername,
            email = cleanEmail,
            password = cleanPassword,
            admin_signup_token = signupToken,
        });
        using var signupRequest = new HttpRequestMessage(HttpMethod.Post, signupUrl);
        signupRequest.Headers.TryAddWithoutValidation("Origin", origin);
        signupRequest.Content = new StringContent(signupPayload, Encoding.UTF8, "application/json");
        using var signupResponse = await _client.SendAsync(signupRequest, cancellationToken);
        using var signupDocument = await ReadSuccessJsonAsync(
            signupResponse,
            "The website account could not be created",
            cancellationToken);

        var signupRoot = signupDocument.RootElement;
        if (!signupRoot.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The website account response did not contain the created user.");

        return new FactburstWebsiteProvisionedUser(
            ReadInt(user, "id"),
            ReadString(user, "username"),
            ReadString(user, "email"),
            ReadBool(user, "email_verified"));
    }

    public async Task ActivateUserAsync(
        string trackerBaseUrl,
        string apiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(trackerBaseUrl);
        var key = RequireApiKey(apiKey);
        using var request = CreateAdminRequest(HttpMethod.Post, $"{root}/api/site/users/{userId}/activate", key);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessJsonAsync(response, "The website user could not be activated", cancellationToken);
    }

    private static HttpRequestMessage CreateAdminRequest(HttpMethod method, string url, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static async Task<JsonDocument> ReadSuccessJsonAsync(
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
                using var errorDocument = JsonDocument.Parse(body);
                detail = ReadString(errorDocument.RootElement, "error");
            }
            catch (JsonException)
            {
            }

            throw new InvalidOperationException(
                $"{fallback} (HTTP {(int)response.StatusCode})" + (detail.Length == 0 ? "." : $": {detail}"));
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"{fallback}: the server returned invalid JSON.", error);
        }
    }

    private static string RequireApiKey(string? value)
    {
        var key = (value ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");
        return key;
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

    private static bool ReadBool(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed;
    }
}
