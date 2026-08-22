using System.Net.Http;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FacebookCommentItem(
    string Id,
    string ReelId,
    string ReelTitle,
    string ReelUrl,
    string Author,
    string AuthorId,
    string Message,
    DateTime? CreatedAt,
    long LikeCount,
    int ReplyCount,
    bool IsLiked,
    bool IsHidden,
    bool IsPageComment)
{
    public string AuthorProfileUrl => AuthorId.Length > 0 && AuthorId.All(char.IsDigit)
        ? $"https://www.facebook.com/{AuthorId}"
        : "";
}

public static class FacebookCommentInbox
{
    public static IReadOnlyList<FacebookCommentItem> Filter(
        IEnumerable<FacebookCommentItem> comments,
        string selection,
        ISet<string>? handledCommentIds = null)
    {
        var query = comments.Where(comment => !comment.IsPageComment);
        query = selection switch
        {
            "Needs reply" => query.Where(comment =>
                !comment.IsHidden &&
                comment.ReplyCount == 0 &&
                !(handledCommentIds?.Contains(comment.Id) ?? false)),
            "Hidden" => query.Where(comment => comment.IsHidden),
            _ => query.Where(comment => !comment.IsHidden),
        };
        return query.OrderByDescending(comment => comment.CreatedAt).ToList();
    }
}

public sealed class FacebookCommentManagementService
{
    private const string GraphRoot = "https://graph.facebook.com/v26.0";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public FacebookCommentManagementService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<IReadOnlyList<FacebookCommentItem>> ListCommentsAsync(
        string pageAccessToken,
        FacebookPageVideos page,
        CancellationToken cancellationToken = default)
    {
        ValidateToken(pageAccessToken);
        var results = new List<FacebookCommentItem>();
        foreach (var reel in page.Videos)
            results.AddRange(await ListReelCommentsAsync(pageAccessToken, page.PageId, reel, cancellationToken));
        return results.OrderByDescending(comment => comment.CreatedAt).ToList();
    }

    public async Task ReplyAsync(
        string pageAccessToken,
        string commentId,
        string message,
        CancellationToken cancellationToken = default)
    {
        ValidateCommentId(commentId);
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Enter a reply first.");
        await SendFormAsync(HttpMethod.Post, commentId + "/comments", pageAccessToken,
            new Dictionary<string, string> { ["message"] = message.Trim() }, cancellationToken);
    }

    public Task SetLikedAsync(
        string pageAccessToken,
        string commentId,
        bool liked,
        CancellationToken cancellationToken = default)
    {
        ValidateCommentId(commentId);
        return SendFormAsync(liked ? HttpMethod.Post : HttpMethod.Delete,
            commentId + "/likes", pageAccessToken, null, cancellationToken);
    }

    public Task SetHiddenAsync(
        string pageAccessToken,
        string commentId,
        bool hidden,
        CancellationToken cancellationToken = default)
    {
        ValidateCommentId(commentId);
        return SendFormAsync(HttpMethod.Post, commentId, pageAccessToken,
            new Dictionary<string, string> { ["is_hidden"] = hidden ? "true" : "false" }, cancellationToken);
    }

    public Task DeleteAsync(
        string pageAccessToken,
        string commentId,
        CancellationToken cancellationToken = default)
    {
        ValidateCommentId(commentId);
        return SendFormAsync(HttpMethod.Delete, commentId, pageAccessToken, null, cancellationToken);
    }

    public static IReadOnlyList<FacebookCommentItem> ParseComments(
        string json,
        string pageId,
        FacebookPageVideo reel)
    {
        using var document = JsonDocument.Parse(json);
        return ParseComments(document.RootElement, pageId, reel);
    }

    public static string ReelTitle(FacebookPageVideo reel)
    {
        if (!string.IsNullOrWhiteSpace(reel.Title)) return reel.Title.Trim();
        var firstLine = reel.Description
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        if (firstLine.Length > 80) firstLine = firstLine[..77] + "...";
        return firstLine.Length > 0 ? firstLine : $"Facebook Reel {reel.VideoId}";
    }

    private async Task<IReadOnlyList<FacebookCommentItem>> ListReelCommentsAsync(
        string pageAccessToken,
        string pageId,
        FacebookPageVideo reel,
        CancellationToken cancellationToken)
    {
        var results = new List<FacebookCommentItem>();
        string? after = null;
        do
        {
            const string fields = "id,message,created_time,from,like_count,user_likes,is_hidden,comment_count";
            var url = $"{GraphRoot}/{Uri.EscapeDataString(reel.VideoId)}/comments" +
                      $"?fields={Uri.EscapeDataString(fields)}&filter=toplevel&order=reverse_chronological&limit=100" +
                      $"&access_token={Uri.EscapeDataString(pageAccessToken.Trim())}";
            if (!string.IsNullOrWhiteSpace(after)) url += $"&after={Uri.EscapeDataString(after)}";
            using var response = await _client.GetAsync(url, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            results.AddRange(ParseComments(document.RootElement, pageId, reel));
            after = ReadAfterCursor(document.RootElement);
        } while (!string.IsNullOrWhiteSpace(after));
        return results;
    }

    private async Task SendFormAsync(
        HttpMethod method,
        string path,
        string pageAccessToken,
        IReadOnlyDictionary<string, string>? values,
        CancellationToken cancellationToken)
    {
        ValidateToken(pageAccessToken);
        var form = new Dictionary<string, string>(values ?? new Dictionary<string, string>())
        {
            ["access_token"] = pageAccessToken.Trim(),
        };
        using var request = new HttpRequestMessage(method, $"{GraphRoot}/{path}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ApiErrorAsync(response, cancellationToken);
    }

    private static IReadOnlyList<FacebookCommentItem> ParseComments(
        JsonElement root,
        string pageId,
        FacebookPageVideo reel)
    {
        var results = new List<FacebookCommentItem>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return results;
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (id.Length == 0) continue;
            var author = item.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object
                ? ReadString(from, "name")
                : "Facebook user";
            var authorId = item.TryGetProperty("from", out from) && from.ValueKind == JsonValueKind.Object
                ? ReadString(from, "id")
                : "";
            results.Add(new FacebookCommentItem(
                id,
                reel.VideoId,
                ReelTitle(reel),
                FacebookReelAnalyticsService.ResolveReelUrl(reel.VideoId, reel.PermalinkUrl),
                author.Length > 0 ? author : "Facebook user",
                authorId,
                ReadString(item, "message"),
                ReadDate(item, "created_time"),
                ReadLong(item, "like_count"),
                (int)Math.Min(int.MaxValue, ReadLong(item, "comment_count")),
                ReadBool(item, "user_likes"),
                ReadBool(item, "is_hidden"),
                pageId.Length > 0 && string.Equals(authorId, pageId, StringComparison.Ordinal)));
        }
        return results;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return JsonDocument.Parse(json);
        throw ApiError(response, json);
    }

    private static async Task<InvalidOperationException> ApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        ApiError(response, await response.Content.ReadAsStringAsync(cancellationToken));

    private static InvalidOperationException ApiError(HttpResponseMessage response, string json)
    {
        var message = "Facebook comment request failed";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
                message = ReadString(error, "message") is { Length: > 0 } detail ? detail : message;
        }
        catch (JsonException) { }
        return new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static void ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
    }

    private static void ValidateCommentId(string commentId)
    {
        if (string.IsNullOrWhiteSpace(commentId)) throw new ArgumentException("Select a comment first.");
    }

    private static string? ReadAfterCursor(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging) ||
            !paging.TryGetProperty("cursors", out var cursors)) return null;
        var after = ReadString(cursors, "after");
        return after.Length == 0 ? null : after;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static long ReadLong(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static bool ReadBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        (value.ValueKind is JsonValueKind.True or JsonValueKind.False) && value.GetBoolean();

    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(value.GetString(), out var date)
            ? date
            : null;
}
