using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubePromoVideoMetadata(
    string Id,
    string ChannelId,
    string Title,
    string Description,
    string CategoryId,
    IReadOnlyList<string> Tags,
    string DefaultLanguage,
    string DefaultAudioLanguage);

public sealed class YouTubePromoMetadataService
{
    private const string ApiRoot = "https://www.googleapis.com/youtube/v3";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubePromoMetadataService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<YouTubePromoVideoMetadata> ReadAsync(
        string accessToken,
        string videoId,
        string expectedChannelId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken(accessToken);
        var id = RequireVideoId(videoId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoot}/videos?part=snippet&id={Uri.EscapeDataString(id)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadDocumentAsync(response, "YouTube could not read the promo Short metadata", cancellationToken);
        var item = document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().FirstOrDefault()
            : default;
        var returnedId = ReadString(item, "id");
        if (returnedId.Length == 0)
            throw new InvalidOperationException("The saved YouTube promo Short could not be found on the connected channel.");
        var snippet = item.TryGetProperty("snippet", out var snippetValue) ? snippetValue : default;
        var channelId = ReadString(snippet, "channelId");
        if (!string.IsNullOrWhiteSpace(expectedChannelId) &&
            !string.Equals(channelId, expectedChannelId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"YouTube promo {returnedId} belongs to channel {channelId}, not the approved channel {expectedChannelId.Trim()}.");
        }

        var title = ReadString(snippet, "title");
        var categoryId = ReadString(snippet, "categoryId");
        if (title.Length == 0 || categoryId.Length == 0)
            throw new InvalidOperationException("YouTube did not return the title and category required to preserve the promo Short metadata.");

        var tags = snippet.TryGetProperty("tags", out var tagValues) && tagValues.ValueKind == JsonValueKind.Array
            ? tagValues.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()?.Trim() ?? "")
                .Where(value => value.Length > 0)
                .ToList()
            : new List<string>();

        return new YouTubePromoVideoMetadata(
            returnedId,
            channelId,
            title,
            ReadString(snippet, "description"),
            categoryId,
            tags,
            ReadString(snippet, "defaultLanguage"),
            ReadString(snippet, "defaultAudioLanguage"));
    }

    public async Task UpdateDescriptionAsync(
        string accessToken,
        YouTubePromoVideoMetadata current,
        string description,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken(accessToken);
        ArgumentNullException.ThrowIfNull(current);
        if (current.Id.Trim().Length == 0 || current.Title.Trim().Length == 0 || current.CategoryId.Trim().Length == 0)
            throw new ArgumentException("The current YouTube snippet is incomplete.", nameof(current));

        var snippet = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = current.Title,
            ["description"] = (description ?? "").Trim(),
            ["categoryId"] = current.CategoryId,
        };
        if (current.Tags.Count > 0) snippet["tags"] = current.Tags;
        if (current.DefaultLanguage.Length > 0) snippet["defaultLanguage"] = current.DefaultLanguage;
        if (current.DefaultAudioLanguage.Length > 0) snippet["defaultAudioLanguage"] = current.DefaultAudioLanguage;

        var json = JsonSerializer.Serialize(new
        {
            id = current.Id,
            snippet,
        });
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiRoot}/videos?part=snippet")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "YouTube could not update the promo Short description", cancellationToken);
    }

    private static string RequireToken(string? value)
    {
        var token = (value ?? "").Trim();
        if (token.Length == 0) throw new InvalidOperationException("Connect YouTube in Settings first.");
        return token;
    }

    private static string RequireVideoId(string? value)
    {
        var id = (value ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("The YouTube promo Short ID is missing.");
        return id;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw ApiError(response, json, fallback);
        return JsonDocument.Parse(json);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        throw ApiError(response, await response.Content.ReadAsStringAsync(cancellationToken), fallback);
    }

    private static InvalidOperationException ApiError(HttpResponseMessage response, string json, string fallback)
    {
        var message = fallback;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var detail = ReadString(error, "message");
                if (detail.Length > 0) message = detail;
            }
        }
        catch (JsonException)
        {
        }
        return new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}

public sealed record FacebookPromoVideoMetadata(string Id, string Description);

public sealed class FacebookPromoMetadataService
{
    private const string GraphRoot = "https://graph.facebook.com/v26.0";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public FacebookPromoMetadataService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<FacebookPromoVideoMetadata> ReadAsync(
        string pageAccessToken,
        string videoId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken(pageAccessToken);
        var id = RequireVideoId(videoId);
        var url = $"{GraphRoot}/{Uri.EscapeDataString(id)}?fields=id%2Cdescription&access_token={Uri.EscapeDataString(token)}";
        using var response = await _client.GetAsync(url, cancellationToken);
        using var document = await ReadDocumentAsync(response, "Facebook could not read the promo Reel description", cancellationToken);
        var returnedId = ReadString(document.RootElement, "id");
        if (!string.Equals(returnedId, id, StringComparison.Ordinal))
            throw new InvalidOperationException("The saved Facebook promo Reel could not be verified on the connected Page.");
        return new FacebookPromoVideoMetadata(returnedId, ReadString(document.RootElement, "description"));
    }

    public async Task UpdateDescriptionAsync(
        string pageAccessToken,
        string videoId,
        string description,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken(pageAccessToken);
        var id = RequireVideoId(videoId);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["access_token"] = token,
            ["description"] = (description ?? "").Trim(),
        });
        using var response = await _client.PostAsync(
            $"{GraphRoot}/{Uri.EscapeDataString(id)}",
            content,
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ApiError(response, json, "Facebook could not update the promo Reel description");
        if (json.Trim().Length == 0) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("success", out var success) &&
                success.ValueKind is JsonValueKind.False)
                throw new InvalidOperationException("Facebook returned success=false while updating the promo Reel description.");
        }
        catch (JsonException)
        {
            // A successful Graph response does not need a JSON body for this operation.
        }
    }

    private static string RequireToken(string? value)
    {
        var token = (value ?? "").Trim();
        if (token.Length == 0) throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        return token;
    }

    private static string RequireVideoId(string? value)
    {
        var id = (value ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("The Facebook promo Reel ID is missing.");
        return id;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw ApiError(response, json, fallback);
        return JsonDocument.Parse(json);
    }

    private static InvalidOperationException ApiError(HttpResponseMessage response, string json, string fallback)
    {
        var message = fallback;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var detail = ReadString(error, "message");
                if (detail.Length > 0) message = detail;
            }
        }
        catch (JsonException)
        {
        }
        return new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}

public static class FactburstPromoBackfillDescription
{
    public static string Apply(string? currentDescription, string trackedUrl)
    {
        var current = (currentDescription ?? "").Trim();
        if (current.Contains((trackedUrl ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return current;
        return FactburstLinkTrackerClient.ReplaceFullQuizLink(current, trackedUrl);
    }
}

public sealed record FactburstPromoBackfillTarget(
    QuizHistorySummary History,
    QuizPromoShortYouTubeUpload? YouTube,
    QuizPromoShortFacebookUpload? Facebook,
    QuizPromoShortInstagramUpload? Instagram);

public static class FactburstPromoBackfillPlanner
{
    public static IReadOnlyList<FactburstPromoBackfillTarget> Build(IEnumerable<QuizHistorySummary> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);
        var results = new List<FactburstPromoBackfillTarget>();
        foreach (var history in histories)
        {
            if (!string.Equals(history.VideoType, "Video", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(history.YouTubeUrl) ||
                string.IsNullOrWhiteSpace(history.ProjectFolder))
                continue;

            var youtube = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
            var facebook = QuizPromoShortSocialPublicationStore.LoadFacebook(history.ProjectFolder);
            var instagram = QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder);
            if (youtube is null && facebook is null && instagram is null) continue;
            results.Add(new FactburstPromoBackfillTarget(history, youtube, facebook, instagram));
        }
        return results;
    }
}
