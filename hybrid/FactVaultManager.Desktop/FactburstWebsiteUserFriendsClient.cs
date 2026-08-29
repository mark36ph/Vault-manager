using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteUserFriend(
    int FriendshipId,
    int UserId,
    string Username,
    string UserStatus,
    string CreatedAt,
    string RespondedAt);

public sealed record FactburstWebsiteUserFriends(
    IReadOnlyList<FactburstWebsiteUserFriend> Friends,
    IReadOnlyList<FactburstWebsiteUserFriend> Incoming,
    IReadOnlyList<FactburstWebsiteUserFriend> Outgoing);

public sealed class FactburstWebsiteUserFriendsClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstWebsiteUserFriendsClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
    }

    public async Task<FactburstWebsiteUserFriends> FetchAsync(
        string baseUrl,
        string apiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{root}/api/site/users/{userId}/friends");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
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
                $"Website friendships could not be loaded (HTTP {(int)response.StatusCode})" + (detail.Length == 0 ? "." : $": {detail}"));
        }

        using var document = JsonDocument.Parse(body);
        return new FactburstWebsiteUserFriends(
            ParseArray(document.RootElement, "friends"),
            ParseArray(document.RootElement, "incoming"),
            ParseArray(document.RootElement, "outgoing"));
    }

    private static IReadOnlyList<FactburstWebsiteUserFriend> ParseArray(JsonElement root, string name)
    {
        var rows = new List<FactburstWebsiteUserFriend>();
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return rows;
        foreach (var item in array.EnumerateArray())
        {
            rows.Add(new FactburstWebsiteUserFriend(
                ReadInt(item, "friendship_id"),
                ReadInt(item, "user_id"),
                ReadString(item, "username"),
                ReadString(item, "user_status"),
                ReadString(item, "created_at"),
                ReadString(item, "responded_at")));
        }
        return rows;
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
}
