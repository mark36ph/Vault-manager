using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeManagedChannel(string Id, string Title);

public sealed record YouTubeCommentItem(
    string Id,
    string ThreadId,
    string VideoId,
    string Author,
    string Text,
    DateTime? PublishedAt,
    long LikeCount,
    int ReplyCount,
    string ModerationStatus,
    string VideoTitle = "",
    string AuthorProfileUrl = "",
    string AuthorChannelId = "",
    bool IsOwnComment = false)
{
    public string StatusDisplay => ModerationStatus switch
    {
        "published" => "Active",
        "heldForReview" => "Needs approval",
        "likelySpam" => "Needs approval (spam)",
        "rejected" => "Rejected",
        _ => "Unknown",
    };
}

public static class YouTubeCommentInbox
{
    public static bool CanMoveToHeldForReview(YouTubeCommentItem comment) =>
        string.Equals(comment.ModerationStatus, "likelySpam", StringComparison.Ordinal);

    public static IReadOnlyList<YouTubeCommentItem> Filter(
        IEnumerable<YouTubeCommentItem> comments,
        bool needsReply,
        ISet<string>? handledCommentIds = null) =>
        comments
            .Where(comment => !needsReply || !comment.IsOwnComment)
            .Where(comment => !needsReply ||
                comment.ReplyCount == 0 &&
                !(handledCommentIds?.Contains(comment.Id) ?? false))
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();

    public static IReadOnlyList<YouTubeCommentItem> ApplyModeration(
        IEnumerable<YouTubeCommentItem> comments,
        string commentId,
        string newStatus,
        string visibleStatus)
    {
        YouTubeManagementService.ValidateModerationStatus(newStatus, allowSpam: false);
        YouTubeManagementService.ValidateModerationStatus(visibleStatus, allowSpam: true);
        return comments
            .Select(comment => string.Equals(comment.Id, commentId, StringComparison.Ordinal)
                ? comment with { ModerationStatus = newStatus }
                : comment)
            .Where(comment => string.Equals(comment.ModerationStatus, visibleStatus, StringComparison.Ordinal))
            .OrderByDescending(comment => comment.PublishedAt)
            .ToList();
    }
}

public sealed record YouTubePlaylistItem(string Id, string Title, string Description, string Privacy, long VideoCount);

public sealed record YouTubePlaylistVideo(string PlaylistItemId, string VideoId, string Title, int Position);

public static class YouTubeCategoryPlaylistPlanner
{
    public static string PlaylistTitle(string category)
    {
        category = (category ?? "").Trim();
        if (category.Length == 0) throw new ArgumentException("Category is required.", nameof(category));
        return category + " Quizzes";
    }

    public static IReadOnlyList<string> MissingCategories(
        IEnumerable<string> categories,
        IEnumerable<YouTubePlaylistItem> playlists)
    {
        var existingTitles = playlists
            .Select(playlist => playlist.Title.Trim())
            .Where(title => title.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(category =>
                !existingTitles.Contains(PlaylistTitle(category)) &&
                !existingTitles.Contains(category + " Quiz") &&
                !existingTitles.Contains(category))
            .ToList();
    }
}

public sealed class YouTubeManagementService
{
    private const string ApiRoot = "https://www.googleapis.com/youtube/v3";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubeManagementService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<YouTubeManagedChannel> GetMyChannelAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "/channels?part=id%2Csnippet&mine=true", accessToken, null, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var channel = document.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
        var id = ReadString(channel, "id");
        if (id.Length == 0) throw new InvalidOperationException("Google did not return a YouTube channel for this account.");
        return new YouTubeManagedChannel(id, ReadString(channel.GetProperty("snippet"), "title"));
    }

    public async Task<IReadOnlyList<YouTubeCommentItem>> ListCommentsAsync(
        string accessToken,
        string channelId,
        string moderationStatus,
        CancellationToken cancellationToken = default)
    {
        ValidateModerationStatus(moderationStatus, allowSpam: true);
        var url = "/commentThreads?part=snippet&allThreadsRelatedToChannelId=" + Uri.EscapeDataString(channelId)
            + "&moderationStatus=" + Uri.EscapeDataString(moderationStatus)
            + "&order=time&textFormat=plainText&maxResults=100";
        using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var comments = ApplyRequestedModerationStatus(
            ParseComments(document.RootElement),
            moderationStatus);
        var titles = await ListVideoTitlesAsync(accessToken, comments.Select(comment => comment.VideoId), cancellationToken);
        return MarkOwnComments(AttachVideoTitles(comments, titles), channelId);
    }

    private async Task<IReadOnlyDictionary<string, string>> ListVideoTitlesAsync(
        string accessToken,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        var ids = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var offset = 0; offset < ids.Length; offset += 50)
        {
            var batch = ids.Skip(offset).Take(50);
            var url = "/videos?part=snippet&id=" + Uri.EscapeDataString(string.Join(',', batch)) + "&maxResults=50";
            using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            foreach (var pair in ParseVideoTitles(document.RootElement))
                results[pair.Key] = pair.Value;
        }
        return results;
    }

    public async Task ReplyAsync(string accessToken, string parentCommentId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentCommentId)) throw new ArgumentException("Select a comment first.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter a reply first.");
        var json = JsonSerializer.Serialize(new { snippet = new { parentId = parentCommentId, textOriginal = text.Trim() } });
        using var response = await SendAsync(HttpMethod.Post, "/comments?part=snippet", accessToken, json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<string> PostTopLevelCommentAsync(
        string accessToken,
        string videoId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId)) throw new ArgumentException("The YouTube video ID is missing.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter a first comment first.");
        var channel = await GetMyChannelAsync(accessToken, cancellationToken);
        var json = JsonSerializer.Serialize(new
        {
            snippet = new
            {
                channelId = channel.Id,
                videoId = videoId.Trim(),
                topLevelComment = new { snippet = new { textOriginal = text.Trim() } },
            },
        });
        using var response = await SendAsync(HttpMethod.Post, "/commentThreads?part=snippet", accessToken, json, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var snippet = document.RootElement.GetProperty("snippet");
        var commentId = ReadString(snippet.GetProperty("topLevelComment"), "id");
        if (commentId.Length == 0)
            throw new InvalidOperationException("YouTube created the first comment but did not return its ID.");
        return commentId;
    }

    public async Task SetModerationStatusAsync(
        string accessToken,
        string commentId,
        string moderationStatus,
        bool banAuthor = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commentId)) throw new ArgumentException("Select a comment first.");
        ValidateModerationStatus(moderationStatus, allowSpam: false);
        var url = "/comments/setModerationStatus?id=" + Uri.EscapeDataString(commentId)
            + "&moderationStatus=" + Uri.EscapeDataString(moderationStatus)
            + (banAuthor ? "&banAuthor=true" : "");
        using var response = await SendAsync(HttpMethod.Post, url, accessToken, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<bool> IsCommentInModerationQueueAsync(
        string accessToken,
        string channelId,
        string moderationStatus,
        string commentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentException("The YouTube channel ID is missing.");
        if (string.IsNullOrWhiteSpace(commentId)) throw new ArgumentException("The YouTube comment ID is missing.");
        if (moderationStatus is not ("published" or "heldForReview" or "likelySpam"))
            throw new ArgumentException("This YouTube moderation queue cannot be checked.");

        var pageToken = "";
        do
        {
            var url = "/commentThreads?part=snippet&allThreadsRelatedToChannelId=" + Uri.EscapeDataString(channelId)
                + "&moderationStatus=" + Uri.EscapeDataString(moderationStatus)
                + "&order=time&textFormat=plainText&maxResults=100"
                + (pageToken.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            if (ParseComments(document.RootElement).Any(comment =>
                    string.Equals(comment.Id, commentId, StringComparison.Ordinal)))
                return true;
            pageToken = ReadString(document.RootElement, "nextPageToken");
        } while (pageToken.Length > 0);

        return false;
    }

    public async Task<IReadOnlyList<YouTubePlaylistItem>> ListPlaylistsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var results = new List<YouTubePlaylistItem>();
        var pageToken = "";
        do
        {
            var url = "/playlists?part=snippet%2Cstatus%2CcontentDetails&mine=true&maxResults=50"
                + (pageToken.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            results.AddRange(ParsePlaylists(document.RootElement));
            pageToken = ReadString(document.RootElement, "nextPageToken");
        } while (pageToken.Length > 0);

        return results;
    }

    public async Task<YouTubePlaylistItem> CreatePlaylistAsync(
        string accessToken,
        string title,
        string description,
        string privacy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Enter a playlist title.");
        privacy = privacy.Trim().ToLowerInvariant();
        if (privacy is not ("public" or "unlisted" or "private")) throw new ArgumentException("Choose public, unlisted, or private.");
        var json = JsonSerializer.Serialize(new
        {
            snippet = new { title = title.Trim(), description = description.Trim() },
            status = new { privacyStatus = privacy },
        });
        using var response = await SendAsync(HttpMethod.Post, "/playlists?part=snippet%2Cstatus%2CcontentDetails", accessToken, json, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        return ParsePlaylist(document.RootElement);
    }

    public async Task UpdatePlaylistPrivacyAsync(
        string accessToken,
        YouTubePlaylistItem playlist,
        string privacy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playlist.Id)) throw new ArgumentException("Select a playlist first.");
        privacy = privacy.Trim().ToLowerInvariant();
        if (privacy is not ("public" or "private")) throw new ArgumentException("Choose public or private.");

        var json = JsonSerializer.Serialize(new
        {
            id = playlist.Id,
            snippet = new { title = playlist.Title, description = playlist.Description },
            status = new { privacyStatus = privacy },
        });
        using var response = await SendAsync(
            HttpMethod.Put,
            "/playlists?part=snippet%2Cstatus",
            accessToken,
            json,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<YouTubePlaylistVideo>> ListPlaylistVideosAsync(
        string accessToken,
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<YouTubePlaylistVideo>();
        var pageToken = "";
        do
        {
            var url = "/playlistItems?part=snippet%2CcontentDetails&playlistId=" + Uri.EscapeDataString(playlistId)
                + "&maxResults=50" + (pageToken.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            results.AddRange(ParsePlaylistVideos(document.RootElement));
            pageToken = ReadString(document.RootElement, "nextPageToken");
        } while (pageToken.Length > 0);
        return results;
    }

    public async Task AddVideoToPlaylistAsync(
        string accessToken,
        string playlistId,
        string videoId,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new
        {
            snippet = new { playlistId, resourceId = new { kind = "youtube#video", videoId } },
        });
        using var response = await SendAsync(HttpMethod.Post, "/playlistItems?part=snippet", accessToken, json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemovePlaylistVideoAsync(string accessToken, string playlistItemId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, "/playlistItems?id=" + Uri.EscapeDataString(playlistItemId), accessToken, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public static IReadOnlyList<YouTubeCommentItem> ParseComments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseComments(document.RootElement);
    }

    public static IReadOnlyList<YouTubePlaylistItem> ParsePlaylists(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParsePlaylists(document.RootElement);
    }

    public static IReadOnlyList<YouTubePlaylistVideo> ParsePlaylistVideos(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParsePlaylistVideos(document.RootElement);
    }

    public static IReadOnlyDictionary<string, string> ParseVideoTitles(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseVideoTitles(document.RootElement);
    }

    public static IReadOnlyList<YouTubeCommentItem> AttachVideoTitles(
        IEnumerable<YouTubeCommentItem> comments,
        IReadOnlyDictionary<string, string> titles) =>
        comments.Select(comment => comment with
        {
            VideoTitle = titles.TryGetValue(comment.VideoId, out var title) && title.Length > 0
                ? title
                : comment.VideoId,
        }).ToList();

    public static IReadOnlyList<YouTubeCommentItem> ApplyRequestedModerationStatus(
        IEnumerable<YouTubeCommentItem> comments,
        string requestedStatus)
    {
        ValidateModerationStatus(requestedStatus, allowSpam: true);
        return comments.Select(comment => string.IsNullOrWhiteSpace(comment.ModerationStatus)
            ? comment with { ModerationStatus = requestedStatus }
            : comment).ToList();
    }

    public static string BuildCommentUrl(string videoId, string commentId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) throw new ArgumentException("The YouTube video ID is missing.");
        if (string.IsNullOrWhiteSpace(commentId)) throw new ArgumentException("The YouTube comment ID is missing.");
        return "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(videoId.Trim())
            + "&lc=" + Uri.EscapeDataString(commentId.Trim());
    }

    public static IReadOnlyList<YouTubeCommentItem> MarkOwnComments(
        IEnumerable<YouTubeCommentItem> comments,
        string channelId) =>
        comments.Select(comment => comment with
        {
            IsOwnComment = channelId.Length > 0 &&
                string.Equals(comment.AuthorChannelId, channelId, StringComparison.Ordinal),
        }).ToList();

    public static void ValidateModerationStatus(string status, bool allowSpam)
    {
        var valid = status is "published" or "heldForReview" or "rejected" || allowSpam && status == "likelySpam";
        if (!valid) throw new ArgumentException("The YouTube moderation status is not supported.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string accessToken,
        string? json,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Connect YouTube in Settings first.");
        var request = new HttpRequestMessage(method, ApiRoot + relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.SendAsync(request, cancellationToken);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw ApiError(response, json);
        return JsonDocument.Parse(json);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        throw ApiError(response, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static InvalidOperationException ApiError(HttpResponseMessage response, string json)
    {
        var message = "YouTube request failed";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
                message = ReadString(error, "message") is { Length: > 0 } detail ? detail : message;
        }
        catch (JsonException) { }
        return new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static IReadOnlyList<YouTubeCommentItem> ParseComments(JsonElement root)
    {
        var results = new List<YouTubeCommentItem>();
        if (!root.TryGetProperty("items", out var items)) return results;
        foreach (var thread in items.EnumerateArray())
        {
            var threadSnippet = thread.GetProperty("snippet");
            var topComment = threadSnippet.GetProperty("topLevelComment");
            var snippet = topComment.GetProperty("snippet");
            var authorProfileUrl = ReadAuthorProfileUrl(snippet);
            var authorChannelId = snippet.TryGetProperty("authorChannelId", out var authorChannel) &&
                                  authorChannel.ValueKind == JsonValueKind.Object
                ? ReadString(authorChannel, "value")
                : "";
            results.Add(new YouTubeCommentItem(
                ReadString(topComment, "id"),
                ReadString(thread, "id"),
                ReadString(snippet, "videoId"),
                ReadString(snippet, "authorDisplayName"),
                ReadString(snippet, "textDisplay"),
                ReadDate(snippet, "publishedAt"),
                ReadLong(snippet, "likeCount"),
                (int)ReadLong(threadSnippet, "totalReplyCount"),
                ReadString(snippet, "moderationStatus"),
                "",
                authorProfileUrl,
                authorChannelId));
        }
        return results;
    }

    private static string ReadAuthorProfileUrl(JsonElement snippet)
    {
        var url = ReadString(snippet, "authorChannelUrl");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.TrimEnd('.');
            if (string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                return builder.Uri.AbsoluteUri;
            }
        }

        if (snippet.TryGetProperty("authorChannelId", out var channel) &&
            channel.ValueKind == JsonValueKind.Object)
        {
            var channelId = ReadString(channel, "value");
            if (channelId.Length > 0)
                return "https://www.youtube.com/channel/" + Uri.EscapeDataString(channelId);
        }
        return "";
    }

    private static IReadOnlyDictionary<string, string> ParseVideoTitles(JsonElement root)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("items", out var items)) return results;
        foreach (var item in items.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var title = item.TryGetProperty("snippet", out var snippet) ? ReadString(snippet, "title") : "";
            if (id.Length > 0) results[id] = title;
        }
        return results;
    }

    private static IReadOnlyList<YouTubePlaylistItem> ParsePlaylists(JsonElement root)
    {
        var results = new List<YouTubePlaylistItem>();
        if (!root.TryGetProperty("items", out var items)) return results;
        foreach (var item in items.EnumerateArray())
            results.Add(ParsePlaylist(item));
        return results;
    }

    private static YouTubePlaylistItem ParsePlaylist(JsonElement item)
    {
        var snippet = item.GetProperty("snippet");
        return new YouTubePlaylistItem(
            ReadString(item, "id"),
            ReadString(snippet, "title"),
            ReadString(snippet, "description"),
            item.TryGetProperty("status", out var status) ? ReadString(status, "privacyStatus") : "",
            item.TryGetProperty("contentDetails", out var details) ? ReadLong(details, "itemCount") : 0);
    }

    private static IReadOnlyList<YouTubePlaylistVideo> ParsePlaylistVideos(JsonElement root)
    {
        var results = new List<YouTubePlaylistVideo>();
        if (!root.TryGetProperty("items", out var items)) return results;
        foreach (var item in items.EnumerateArray())
        {
            var snippet = item.GetProperty("snippet");
            var resource = snippet.GetProperty("resourceId");
            results.Add(new YouTubePlaylistVideo(
                ReadString(item, "id"),
                ReadString(resource, "videoId"),
                ReadString(snippet, "title"),
                (int)ReadLong(snippet, "position")));
        }
        return results;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value.GetString()?.Trim() ?? "" : "";

    private static long ReadLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && DateTime.TryParse(value.GetString(), out var date) ? date : null;

}
