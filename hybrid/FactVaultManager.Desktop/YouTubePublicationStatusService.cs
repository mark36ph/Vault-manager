using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubePublicationStatus(
    string VideoId,
    string PrivacyStatus,
    DateTimeOffset? PublishAt);

public sealed class YouTubePublicationStatusService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubePublicationStatusService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<IReadOnlyDictionary<string, YouTubePublicationStatus>> FetchAsync(
        string accessToken,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Connect YouTube in Settings first.");
        ArgumentNullException.ThrowIfNull(videoIds);

        var ids = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = new Dictionary<string, YouTubePublicationStatus>(StringComparer.Ordinal);

        for (var offset = 0; offset < ids.Length; offset += 50)
        {
            var batch = ids.Skip(offset).Take(50).ToArray();
            if (batch.Length == 0) continue;

            var requestUri = "https://www.googleapis.com/youtube/v3/videos" +
                             "?part=status" +
                             "&fields=items(id%2Cstatus%2FprivacyStatus%2Cstatus%2FpublishAt)" +
                             $"&id={Uri.EscapeDataString(string.Join(',', batch))}" +
                             "&maxResults=50";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var response = await _client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(BuildApiError(response, json));

            foreach (var state in ParseResponse(json))
                results[state.VideoId] = state;
        }

        return results;
    }

    public static IReadOnlyList<YouTubePublicationStatus> ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<YouTubePublicationStatus>();
        foreach (var item in items.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (id.Length == 0 || !item.TryGetProperty("status", out var status)) continue;

            var privacy = ReadString(status, "privacyStatus").ToLowerInvariant();
            if (privacy is not ("private" or "unlisted" or "public")) continue;

            DateTimeOffset? publishAt = null;
            var publishAtText = ReadString(status, "publishAt");
            if (publishAtText.Length > 0 && DateTimeOffset.TryParse(
                    publishAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedPublishAt))
            {
                publishAt = parsedPublishAt;
            }

            results.Add(new YouTubePublicationStatus(id, privacy, publishAt));
        }

        return results;
    }

    private static string BuildApiError(HttpResponseMessage response, string json)
    {
        var detail = "YouTube could not refresh the publication status";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = ReadString(error, "message");
                if (message.Length > 0) detail = message;
            }
        }
        catch (JsonException) { }

        return $"{detail} (HTTP {(int)response.StatusCode}).";
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}
