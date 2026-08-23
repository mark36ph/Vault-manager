using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeVideoUpload(
    string Title,
    string Description,
    string PrivacyStatus,
    bool NotifySubscribers = true);

public sealed record YouTubeVideoUploadResult(string VideoId, string Url);

public sealed record FacebookReelUploadResult(string VideoId, string Url);

public static class SocialVideoUploadRules
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v" };

    public static bool CanUploadToFacebook(QuizHistorySummary history) =>
        string.Equals(history.VideoType, "Short", StringComparison.Ordinal);

    public static string ValidateVideoFile(string? path)
    {
        var value = (path ?? "").Trim().Trim('"');
        if (value.Length == 0) throw new ArgumentException("Choose the completed video file first.");
        var fullPath = Path.GetFullPath(value);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected video file does not exist.", fullPath);
        if (!SupportedExtensions.Contains(Path.GetExtension(fullPath)))
            throw new ArgumentException("Choose an MP4, MOV, or M4V video file.");
        return fullPath;
    }

    public static string UploadDescription(QuizHistorySummary history)
    {
        var description = history.YouTubeDescription.Trim();
        var hashtags = history.Hashtags.Trim();
        if (hashtags.Length == 0 || description.Contains(hashtags, StringComparison.OrdinalIgnoreCase))
            return description;
        return description.Length == 0 ? hashtags : description + Environment.NewLine + Environment.NewLine + hashtags;
    }

    public static string? FindLikelyRenderedVideo(string projectFolder)
    {
        if (!Directory.Exists(projectFolder)) return null;
        return Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static void ValidatePrivacy(string privacy)
    {
        if (privacy is not ("private" or "unlisted" or "public"))
            throw new ArgumentException("Choose private, unlisted, or public for YouTube.");
    }

    public static void ValidateFacebookDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 3 || seconds > 90)
            throw new ArgumentException("Facebook Reels must be between 3 and 90 seconds long.");
    }
}

public sealed class YouTubeVideoUploadService
{
    private const string UploadEndpoint = "https://www.googleapis.com/upload/youtube/v3/videos";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromHours(4) };
    private readonly HttpClient _client;

    public YouTubeVideoUploadService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<YouTubeVideoUploadResult> UploadAsync(
        string accessToken,
        string videoPath,
        YouTubeVideoUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Connect YouTube in Settings first.");
        var path = SocialVideoUploadRules.ValidateVideoFile(videoPath);
        var title = upload.Title.Trim();
        if (title.Length == 0) throw new ArgumentException("Enter the YouTube title first.");
        if (title.Length > 100) throw new ArgumentException("The YouTube title cannot exceed 100 characters.");
        SocialVideoUploadRules.ValidatePrivacy(upload.PrivacyStatus);

        var length = new FileInfo(path).Length;
        var mimeType = VideoMimeType(path);
        var metadata = JsonSerializer.Serialize(new
        {
            snippet = new
            {
                title,
                description = upload.Description.Trim(),
                categoryId = "27",
            },
            status = new
            {
                privacyStatus = upload.PrivacyStatus,
                selfDeclaredMadeForKids = false,
            },
        });
        var initiateUrl = UploadEndpoint +
                          "?uploadType=resumable&part=snippet%2Cstatus&notifySubscribers=" +
                          (upload.NotifySubscribers ? "true" : "false");
        using var initiate = new HttpRequestMessage(HttpMethod.Post, initiateUrl)
        {
            Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
        };
        initiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        initiate.Headers.TryAddWithoutValidation("X-Upload-Content-Length", length.ToString());
        initiate.Headers.TryAddWithoutValidation("X-Upload-Content-Type", mimeType);
        using var initiationResponse = await _client.SendAsync(initiate, cancellationToken);
        if (!initiationResponse.IsSuccessStatusCode)
            throw await YouTubeErrorAsync(initiationResponse, cancellationToken);
        if (initiationResponse.Headers.Location is not Uri uploadUri || uploadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("YouTube did not return a resumable upload address.");

        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Headers.ContentLength = length;
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw YouTubeError(response, json);
        using var document = JsonDocument.Parse(json);
        var videoId = ReadString(document.RootElement, "id");
        if (videoId.Length == 0) throw new InvalidOperationException("YouTube completed the upload but did not return a video ID.");
        return new YouTubeVideoUploadResult(videoId, $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}");
    }

    private static string VideoMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mov" => "video/quicktime",
        ".m4v" => "video/x-m4v",
        _ => "video/mp4",
    };

    private static async Task<InvalidOperationException> YouTubeErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        YouTubeError(response, await response.Content.ReadAsStringAsync(cancellationToken));

    private static InvalidOperationException YouTubeError(HttpResponseMessage response, string json)
    {
        var message = "YouTube upload failed";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var detail)) message = detail.GetString()?.Trim() ?? message;
            }
        }
        catch (JsonException) { }
        return new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}

public sealed class FacebookReelUploadService
{
    private const string GraphRoot = "https://graph.facebook.com/v26.0";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromHours(4) };
    private readonly HttpClient _client;

    public FacebookReelUploadService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<FacebookReelUploadResult> UploadAsync(
        string pageAccessToken,
        string videoPath,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        var path = SocialVideoUploadRules.ValidateVideoFile(videoPath);
        var token = pageAccessToken.Trim();
        var pageId = await GetPageIdAsync(token, cancellationToken);

        using var startResponse = await _client.PostAsync(
            $"{GraphRoot}/{Uri.EscapeDataString(pageId)}/video_reels",
            Form(token, new Dictionary<string, string> { ["upload_phase"] = "start" }),
            cancellationToken);
        using var start = await ReadDocumentAsync(startResponse, cancellationToken);
        var videoId = ReadString(start.RootElement, "video_id");
        var uploadUrl = ReadString(start.RootElement, "upload_url");
        if (videoId.Length == 0 || !Uri.TryCreate(uploadUrl, UriKind.Absolute, out var uploadUri) ||
            uploadUri.Scheme != Uri.UriSchemeHttps || !IsFacebookHost(uploadUri.Host))
            throw new InvalidOperationException("Facebook did not return a valid Reel upload session.");

        var fileLength = new FileInfo(path).Length;
        await using (var stream = File.OpenRead(path))
        using (var content = new StreamContent(stream))
        using (var request = new HttpRequestMessage(HttpMethod.Post, uploadUri) { Content = content })
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = fileLength;
            request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + token);
            request.Headers.TryAddWithoutValidation("offset", "0");
            request.Headers.TryAddWithoutValidation("file_size", fileLength.ToString());
            using var uploadResponse = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!uploadResponse.IsSuccessStatusCode)
                throw await FacebookErrorAsync(uploadResponse, cancellationToken);
        }

        var finishValues = new Dictionary<string, string>
        {
            ["upload_phase"] = "finish",
            ["video_id"] = videoId,
            ["video_state"] = "PUBLISHED",
            ["description"] = description.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(title)) finishValues["title"] = title.Trim();
        using var finishResponse = await _client.PostAsync(
            $"{GraphRoot}/{Uri.EscapeDataString(pageId)}/video_reels",
            Form(token, finishValues),
            cancellationToken);
        using var finish = await ReadDocumentAsync(finishResponse, cancellationToken);
        return new FacebookReelUploadResult(
            videoId,
            FacebookReelAnalyticsService.ResolveReelUrl(videoId));
    }

    private async Task<string> GetPageIdAsync(string token, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(
            $"{GraphRoot}/me?fields=id&access_token={Uri.EscapeDataString(token)}",
            cancellationToken);
        using var page = await ReadDocumentAsync(response, cancellationToken);
        var pageId = ReadString(page.RootElement, "id");
        if (pageId.Length == 0 || !pageId.All(char.IsDigit))
            throw new InvalidOperationException("The saved Facebook token does not identify a Page.");
        return pageId;
    }

    private static FormUrlEncodedContent Form(string token, IReadOnlyDictionary<string, string> values)
    {
        var form = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        form["access_token"] = token;
        return new FormUrlEncodedContent(form);
    }

    private static bool IsFacebookHost(string host)
    {
        var value = host.TrimEnd('.');
        return string.Equals(value, "facebook.com", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return JsonDocument.Parse(json);
        throw FacebookError(response, json);
    }

    private static async Task<InvalidOperationException> FacebookErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        FacebookError(response, await response.Content.ReadAsStringAsync(cancellationToken));

    private static InvalidOperationException FacebookError(HttpResponseMessage response, string json)
    {
        var message = "Facebook Reel upload failed";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var detail = ReadString(error, "message");
                if (detail.Length > 0) message = detail;
                var code = error.TryGetProperty("code", out var codeValue) && codeValue.TryGetInt32(out var number)
                    ? number
                    : 0;
                if (code is 10 or 100 or 200)
                    message += " Add pages_manage_posts to the Meta app and generate a new Page access token.";
            }
        }
        catch (JsonException) { }
        return new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}
