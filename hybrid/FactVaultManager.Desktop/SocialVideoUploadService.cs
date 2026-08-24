using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeVideoUpload(
    string Title,
    string Description,
    string PrivacyStatus,
    bool NotifySubscribers = true,
    DateTimeOffset? PublishAt = null);

public sealed record YouTubeVideoUploadResult(string VideoId, string Url);

public sealed record FacebookReelUploadResult(string VideoId, string Url);

public static class SocialVideoUploadRules
{
    public const long MaximumThumbnailBytes = 2 * 1024 * 1024;
    public const string InstagramFullQuizCallToAction = "Watch the full quiz using the link in our bio.";
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v" };
    private static readonly HashSet<string> SupportedThumbnailExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

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

    public static string InstagramCaption(string? description)
    {
        var lines = (description ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var result = new List<string>(lines.Length);
        var addedCallToAction = false;
        foreach (var line in lines)
        {
            if (!ContainsFullYouTubeVideoLink(line))
            {
                result.Add(line);
                continue;
            }
            if (addedCallToAction) continue;
            result.Add(InstagramFullQuizCallToAction);
            addedCallToAction = true;
        }
        return string.Join(Environment.NewLine, result).Trim();
    }

    public static void ValidateUploadMetadata(
        string videoType,
        string? title,
        string? description,
        bool requireFullYouTubeVideoLink = true)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Enter the upload title first.");
        if (!requireFullYouTubeVideoLink ||
            !string.Equals(videoType, "Short", StringComparison.Ordinal)) return;
        if (!ContainsFullYouTubeVideoLink(description))
            throw new ArgumentException(
                "The Short description must include the HTTPS link to its full YouTube video (youtube.com/watch or youtu.be).");
    }

    public static bool ContainsFullYouTubeVideoLink(string? description)
    {
        foreach (var token in (description ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.Trim('(', ')', '[', ']', '<', '>', ',', '.', ';', '!', '"', '\'');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
            var host = uri.Host.TrimStart('.');
            if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Trim('/').Length > 0)
                return true;
            if ((string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(host, "www.youtube.com", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(uri.AbsolutePath.TrimEnd('/'), "/watch", StringComparison.OrdinalIgnoreCase) &&
                uri.Query.Contains("v=", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string? ValidateThumbnailFile(string? path)
    {
        var value = (path ?? "").Trim().Trim('"');
        if (value.Length == 0) return null;
        var fullPath = Path.GetFullPath(value);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected thumbnail image does not exist.", fullPath);
        if (!SupportedThumbnailExtensions.Contains(Path.GetExtension(fullPath)))
            throw new ArgumentException("Choose a JPG, JPEG, or PNG thumbnail image.");
        if (new FileInfo(fullPath).Length > MaximumThumbnailBytes)
            throw new ArgumentException("The thumbnail image must be 2 MB or smaller for YouTube.");
        return fullPath;
    }

    public static string? FindLikelyThumbnail(string projectFolder)
    {
        if (!Directory.Exists(projectFolder)) return null;
        var preferred = Path.Combine(projectFolder, "Thumbnail.png");
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedThumbnailExtensions.Contains(Path.GetExtension(path)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
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

    public static DateTimeOffset? ResolveScheduledPublishAt(
        bool schedule,
        DateTime? selectedDate,
        string? timeText,
        DateTimeOffset now,
        bool includesFacebook)
    {
        if (!schedule) return null;
        if (selectedDate is null)
            throw new ArgumentException("Choose the publication date.");
        if (!TimeOnly.TryParseExact(
                (timeText ?? "").Trim(),
                new[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            throw new ArgumentException("Enter the publication time as HH:mm, for example 18:30.");
        }

        var localDateTime = DateTime.SpecifyKind(selectedDate.Value.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localDateTime))
            throw new ArgumentException("That local publication time does not exist because the clocks change then. Choose another time.");
        var scheduled = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
        if (scheduled < now.AddMinutes(10))
            throw new ArgumentException("Schedule publication for at least 10 minutes from now.");
        if (includesFacebook && scheduled > now.AddDays(30))
            throw new ArgumentException("Facebook Reels can be scheduled no more than 30 days ahead.");
        return scheduled;
    }

    public static void ValidateFacebookDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 3 || seconds > 90)
            throw new ArgumentException("Facebook Reels must be between 3 and 90 seconds long.");
    }

    public static void ValidateInstagramDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 3 || seconds > 900)
            throw new ArgumentException("Instagram Reels must be between 3 seconds and 15 minutes long.");
    }
}

public sealed class YouTubeVideoUploadService
{
    private const string UploadEndpoint = "https://www.googleapis.com/upload/youtube/v3/videos";
    private const string ThumbnailEndpoint = "https://www.googleapis.com/upload/youtube/v3/thumbnails/set";
    private static readonly TimeSpan[] ThumbnailRetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
    };
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromHours(4) };
    private readonly HttpClient _client;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public YouTubeVideoUploadService(
        HttpClient? client = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client ?? SharedClient;
        _delay = delay ?? ((duration, cancellationToken) => Task.Delay(duration, cancellationToken));
    }

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
        if (upload.PublishAt is not null && upload.PrivacyStatus != "private")
            throw new ArgumentException("A scheduled YouTube upload must use private visibility until publication.");
        if (upload.PublishAt is { } publishAt && publishAt < DateTimeOffset.Now.AddMinutes(10))
            throw new ArgumentException("Schedule YouTube publication for at least 10 minutes from now.");

        var length = new FileInfo(path).Length;
        var mimeType = VideoMimeType(path);
        var status = new Dictionary<string, object?>
        {
            ["privacyStatus"] = upload.PrivacyStatus,
            ["selfDeclaredMadeForKids"] = false,
        };
        if (upload.PublishAt is { } scheduled)
            status["publishAt"] = scheduled.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var metadata = JsonSerializer.Serialize(new
        {
            snippet = new
            {
                title,
                description = upload.Description.Trim(),
                categoryId = "27",
            },
            status,
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

    public async Task SetThumbnailAsync(
        string accessToken,
        string videoId,
        string thumbnailPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Connect YouTube in Settings first.");
        if (string.IsNullOrWhiteSpace(videoId)) throw new ArgumentException("YouTube did not return a video ID.");
        var path = SocialVideoUploadRules.ValidateThumbnailFile(thumbnailPath)!;
        for (var attempt = 0; ; attempt++)
        {
            await using var stream = File.OpenRead(path);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(ImageMimeType(path));
            content.Headers.ContentLength = stream.Length;
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                ThumbnailEndpoint + "?videoId=" + Uri.EscapeDataString(videoId) + "&uploadType=media")
            {
                Content = content,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var response = await _client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return;

            var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (attempt >= ThumbnailRetryDelays.Length || !ThumbnailFailureMayBeTemporary(response, errorJson))
                throw YouTubeError(response, errorJson);

            await _delay(ThumbnailRetryDelays[attempt], cancellationToken);
        }
    }

    private static bool ThumbnailFailureMayBeTemporary(HttpResponseMessage response, string errorJson)
    {
        var status = (int)response.StatusCode;
        if (status is 404 or 408 or 409 or 425 or 429 || status >= 500) return true;
        if (status != 400) return false;
        return errorJson.Contains("processing", StringComparison.OrdinalIgnoreCase) ||
               errorJson.Contains("not ready", StringComparison.OrdinalIgnoreCase) ||
               errorJson.Contains("video not found", StringComparison.OrdinalIgnoreCase);
    }

    private static string VideoMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mov" => "video/quicktime",
        ".m4v" => "video/x-m4v",
        _ => "video/mp4",
    };

    private static string ImageMimeType(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

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
        DateTimeOffset? publishAt = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        var path = SocialVideoUploadRules.ValidateVideoFile(videoPath);
        if (publishAt is { } requestedSchedule)
        {
            if (requestedSchedule < DateTimeOffset.Now.AddMinutes(10))
                throw new ArgumentException("Schedule Facebook publication for at least 10 minutes from now.");
            if (requestedSchedule > DateTimeOffset.Now.AddDays(30))
                throw new ArgumentException("Facebook Reels can be scheduled no more than 30 days ahead.");
        }
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
            ["video_state"] = publishAt is null ? "PUBLISHED" : "SCHEDULED",
            ["description"] = description.Trim(),
        };
        if (publishAt is { } scheduled)
        {
            finishValues["scheduled_publish_time"] = scheduled.ToUniversalTime()
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        }
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

    public async Task SetThumbnailAsync(
        string pageAccessToken,
        string videoId,
        string thumbnailPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        if (string.IsNullOrWhiteSpace(videoId)) throw new ArgumentException("Facebook did not return a video ID.");
        var path = SocialVideoUploadRules.ValidateThumbnailFile(thumbnailPath)!;
        await using var stream = File.OpenRead(path);
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(pageAccessToken.Trim()), "access_token");
        content.Add(new StringContent("true"), "is_preferred");
        content.Add(file, "source", Path.GetFileName(path));
        using var response = await _client.PostAsync(
            $"{GraphRoot}/{Uri.EscapeDataString(videoId)}/thumbnails",
            content,
            cancellationToken);
        if (!response.IsSuccessStatusCode) throw await FacebookErrorAsync(response, cancellationToken);
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
