using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record InstagramMediaItem(
    string MediaId,
    string MediaType,
    string Caption,
    string Permalink,
    DateTime? PublishedAt,
    long Views,
    long Reach,
    long Likes,
    long Comments,
    long Saved,
    long Shares,
    long TotalInteractions);

public sealed record InstagramAccountMedia(
    string UserId,
    string Username,
    string AccountType,
    long MediaCount,
    IReadOnlyList<InstagramMediaItem> Media);

public sealed record InstagramReelUploadResult(string MediaId, string Url);

public sealed class InstagramManagementService
{
    internal const string GraphRoot = "https://graph.instagram.com/v26.0";
    internal const string FacebookGraphRoot = "https://graph.facebook.com/v26.0";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly HttpClient _client;

    public InstagramManagementService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<InstagramAccountMedia> ListMediaAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken(accessToken);
        using var account = await GetJsonAsync(
            $"{GraphRoot}/me?fields=user_id%2Cusername%2Caccount_type%2Cmedia_count",
            token,
            cancellationToken);
        var userId = ReadString(account.RootElement, "user_id");
        if (userId.Length == 0) userId = ReadString(account.RootElement, "id");
        if (userId.Length == 0)
            throw new InvalidOperationException("Instagram did not return the connected account ID. Generate a new Instagram user access token.");

        var media = new List<InstagramMediaItem>();
        var fields = "id,caption,media_type,media_product_type,permalink,timestamp,like_count,comments_count";
        string? after = null;
        for (var page = 0; page < 4; page++)
        {
            var url = $"{GraphRoot}/{Uri.EscapeDataString(userId)}/media?fields={Uri.EscapeDataString(fields)}&limit=25";
            if (!string.IsNullOrWhiteSpace(after))
                url += $"&after={Uri.EscapeDataString(after)}";
            using var document = await GetJsonAsync(url, token, cancellationToken);
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = ReadString(item, "id");
                    if (id.Length == 0) continue;
                    var insight = await FetchInsightsAsync(id, token, cancellationToken);
                    media.Add(new InstagramMediaItem(
                        id,
                        ReadString(item, "media_product_type") is { Length: > 0 } product
                            ? product
                            : ReadString(item, "media_type"),
                        ReadString(item, "caption"),
                        ReadString(item, "permalink"),
                        ReadDate(item, "timestamp"),
                        insight.Views,
                        insight.Reach,
                        ReadLong(item, "like_count"),
                        ReadLong(item, "comments_count"),
                        insight.Saved,
                        insight.Shares,
                        insight.TotalInteractions));
                }
            }
            after = ReadAfterCursor(document.RootElement);
            if (string.IsNullOrWhiteSpace(after)) break;
        }

        return new InstagramAccountMedia(
            userId,
            ReadString(account.RootElement, "username"),
            ReadString(account.RootElement, "account_type"),
            ReadLong(account.RootElement, "media_count"),
            media);
    }

    public async Task<InstagramReelUploadResult> UploadReelAsync(
        string facebookPageAccessToken,
        string videoPath,
        string caption,
        bool shareToFeed = true,
        CancellationToken cancellationToken = default)
    {
        var token = RequireFacebookPageToken(facebookPageAccessToken);
        var path = SocialVideoUploadRules.ValidateVideoFile(videoPath);
        if (new FileInfo(path).Length > 1024L * 1024 * 1024)
            throw new ArgumentException("The Instagram Reel video must be 1 GB or smaller.");
        var text = (caption ?? "").Trim();
        if (text.Length > 2200)
            throw new ArgumentException("The Instagram caption cannot exceed 2,200 characters.");

        using var account = await GetJsonAsync(
            $"{FacebookGraphRoot}/me?fields=instagram_business_account",
            token,
            cancellationToken);
        var userId = account.RootElement.TryGetProperty("instagram_business_account", out var instagramAccount)
            ? ReadString(instagramAccount, "id")
            : "";
        if (userId.Length == 0)
            throw new InvalidOperationException("The saved Facebook Page is not linked to a professional Instagram account.");

        using var create = await PostJsonAsync(
            $"{FacebookGraphRoot}/{Uri.EscapeDataString(userId)}/media",
            token,
            new Dictionary<string, object>
            {
                ["media_type"] = "REELS",
                ["upload_type"] = "resumable",
                ["caption"] = text,
                ["share_to_feed"] = shareToFeed,
            },
            cancellationToken);
        var containerId = ReadString(create.RootElement, "id");
        if (containerId.Length == 0)
            throw new InvalidOperationException("Instagram did not create a Reel upload container.");
        var uploadUrl = ReadString(create.RootElement, "uri");

        await UploadVideoBytesAsync(token, containerId, uploadUrl, path, cancellationToken);
        await WaitUntilReadyAsync(FacebookGraphRoot, token, containerId, cancellationToken);

        using var publish = await PostFormAsync(
            $"{FacebookGraphRoot}/{Uri.EscapeDataString(userId)}/media_publish",
            token,
            new Dictionary<string, string> { ["creation_id"] = containerId },
            cancellationToken);
        var mediaId = ReadString(publish.RootElement, "id");
        if (mediaId.Length == 0)
            throw new InvalidOperationException("Instagram processed the upload but did not return a published media ID.");

        var url = "";
        try
        {
            using var details = await GetJsonAsync(
                $"{FacebookGraphRoot}/{Uri.EscapeDataString(mediaId)}?fields=permalink",
                token,
                cancellationToken);
            url = ReadString(details.RootElement, "permalink");
        }
        catch (InvalidOperationException)
        {
            // The upload succeeded; the permalink can be loaded on the next manager refresh.
        }
        return new InstagramReelUploadResult(mediaId, url);
    }

    private async Task UploadVideoBytesAsync(
        string token,
        string containerId,
        string uploadUrl,
        string path,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        var fallbackUrl = $"https://rupload.facebook.com/ig-api-upload/v26.0/{Uri.EscapeDataString(containerId)}";
        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(uploadUrl) ? fallbackUrl : uploadUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Instagram returned an invalid upload address.");
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "rupload.facebook.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Instagram did not provide a trusted upload address.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + token);
        request.Headers.TryAddWithoutValidation("offset", "0");
        request.Headers.TryAddWithoutValidation("file_size", file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content = new StreamContent(stream, 1024 * 1024);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase)
            ? "video/quicktime"
            : "video/mp4");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "Instagram video upload failed", cancellationToken);
    }

    private async Task WaitUntilReadyAsync(
        string graphRoot,
        string token,
        string containerId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            using var status = await GetJsonAsync(
                $"{graphRoot}/{Uri.EscapeDataString(containerId)}?fields=status_code%2Cstatus",
                token,
                cancellationToken);
            var code = ReadString(status.RootElement, "status_code");
            if (string.Equals(code, "FINISHED", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(code, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                var detail = ReadString(status.RootElement, "status");
                throw new InvalidOperationException(detail.Length > 0 ? detail : $"Instagram Reel processing ended with status {code}.");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        throw new TimeoutException("Instagram did not finish processing the Reel within 10 minutes.");
    }

    private async Task<(long Views, long Reach, long Saved, long Shares, long TotalInteractions)> FetchInsightsAsync(
        string mediaId,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync(
                $"{GraphRoot}/{Uri.EscapeDataString(mediaId)}/insights?metric=views%2Creach%2Csaved%2Cshares%2Ctotal_interactions",
                token,
                cancellationToken);
            return (
                ReadInsight(document.RootElement, "views"),
                ReadInsight(document.RootElement, "reach"),
                ReadInsight(document.RootElement, "saved"),
                ReadInsight(document.RootElement, "shares"),
                ReadInsight(document.RootElement, "total_interactions"));
        }
        catch (InvalidOperationException)
        {
            return (0, 0, 0, 0, 0);
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request, cancellationToken);
        return await ReadDocumentAsync(response, "Instagram request failed", cancellationToken);
    }

    private async Task<JsonDocument> PostFormAsync(
        string url,
        string token,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new FormUrlEncodedContent(values);
        using var response = await _client.SendAsync(request, cancellationToken);
        return await ReadDocumentAsync(response, "Instagram request failed", cancellationToken);
    }

    private async Task<JsonDocument> PostJsonAsync(
        string url,
        string token,
        IReadOnlyDictionary<string, object> values,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(values), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        return await ReadDocumentAsync(response, "Instagram request failed", cancellationToken);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return JsonDocument.Parse(json);
        var message = fallback;
        try
        {
            using var error = JsonDocument.Parse(json);
            if (error.RootElement.TryGetProperty("error", out var value))
            {
                var detail = ReadString(value, "message");
                if (detail.Length > 0) message = detail;
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        using var _ = await ReadDocumentAsync(response, fallback, cancellationToken);
    }

    private static string RequireToken(string? value)
    {
        var token = (value ?? "").Trim();
        if (token.Length == 0)
            throw new InvalidOperationException("Add the Instagram user access token in Settings first.");
        return token;
    }

    private static string RequireFacebookPageToken(string? value)
    {
        var token = (value ?? "").Trim();
        if (token.Length == 0)
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        return token;
    }

    private static string? ReadAfterCursor(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging) ||
            !paging.TryGetProperty("cursors", out var cursors) ||
            !cursors.TryGetProperty("after", out var after) ||
            after.ValueKind != JsonValueKind.String)
            return null;
        return after.GetString();
    }

    private static long ReadInsight(JsonElement root, string name)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return 0;
        foreach (var metric in data.EnumerateArray())
        {
            if (!string.Equals(ReadString(metric, "name"), name, StringComparison.Ordinal)) continue;
            if (metric.TryGetProperty("total_value", out var total) &&
                total.TryGetProperty("value", out var totalValue))
                return ReadNumber(totalValue);
            if (metric.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in values.EnumerateArray())
                    if (value.TryGetProperty("value", out var number)) return ReadNumber(number);
            }
        }
        return 0;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? ReadNumber(value) : 0;

    private static long ReadNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? Math.Max(0, number) : 0;

    private static DateTime? ReadDate(JsonElement element, string name) =>
        DateTime.TryParse(ReadString(element, name), null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
}
