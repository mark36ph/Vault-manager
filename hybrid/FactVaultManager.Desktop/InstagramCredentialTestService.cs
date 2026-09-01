using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record InstagramAccountIdentity(string UserId, string Username, string AccountType);

public static class InstagramCredentialTestService
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static async Task<InstagramAccountIdentity> GetAccountIdentityAsync(
        this InstagramManagementService service,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        _ = service ?? throw new ArgumentNullException(nameof(service));
        var token = (accessToken ?? "").Trim();
        if (token.Length == 0)
            throw new InvalidOperationException("Add the Instagram user access token in Settings first.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{InstagramManagementService.GraphRoot}/me?fields=user_id%2Cusername%2Caccount_type");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SharedClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Instagram rejected the token (HTTP {(int)response.StatusCode}). Generate a new Instagram user access token if it has expired or lacks the required permissions.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var userId = ReadString(root, "user_id");
        if (userId.Length == 0) userId = ReadString(root, "id");
        if (userId.Length == 0)
            throw new InvalidOperationException("Instagram accepted the request but did not return the connected account ID.");

        return new InstagramAccountIdentity(
            userId,
            ReadString(root, "username"),
            ReadString(root, "account_type"));
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}
