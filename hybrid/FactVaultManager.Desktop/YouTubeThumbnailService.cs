using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public static class YouTubeVideoReference
{
    public static string ParseVideoId(string? value)
    {
        var text = (value ?? "").Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The saved YouTube URL is not a valid HTTPS video link.", nameof(value));

        var host = uri.Host.TrimEnd('.');
        var candidate = "";
        if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            candidate = PathSegment(uri.AbsolutePath, 0);
        }
        else if (string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var first = PathSegment(uri.AbsolutePath, 0);
            if (string.Equals(first, "watch", StringComparison.OrdinalIgnoreCase))
                candidate = QueryValue(uri.Query, "v");
            else if (first is "shorts" or "embed" or "live")
                candidate = PathSegment(uri.AbsolutePath, 1);
        }

        candidate = Uri.UnescapeDataString(candidate).Trim();
        if (!IsValidVideoId(candidate))
            throw new ArgumentException("The saved YouTube URL does not contain a valid video ID.", nameof(value));
        return candidate;
    }

    private static string PathSegment(string path, int index)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return index >= 0 && index < parts.Length ? parts[index] : "";
    }

    private static string QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase))
                continue;
            return equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..]);
        }
        return "";
    }

    private static bool IsValidVideoId(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}

public sealed class YouTubeThumbnailService
{
    public const long MaximumThumbnailBytes = 2L * 1024 * 1024;
    private const string UploadEndpoint = "https://www.googleapis.com/upload/youtube/v3/thumbnails/set";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubeThumbnailService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task SetAsync(
        string accessToken,
        string videoId,
        string thumbnailPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Connect YouTube in Settings first.");
        videoId = (videoId ?? "").Trim();
        if (videoId.Length == 0)
            throw new ArgumentException("The YouTube video ID is missing.", nameof(videoId));

        var path = Path.GetFullPath((thumbnailPath ?? "").Trim());
        if (!File.Exists(path))
            throw new FileNotFoundException("Thumbnail.png was not found. Regenerate the thumbnail first.", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var mediaType = extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new InvalidDataException("YouTube thumbnails must be PNG or JPEG images."),
        };
        var length = new FileInfo(path).Length;
        if (length <= 0)
            throw new InvalidDataException("The thumbnail image is empty.");
        if (length > MaximumThumbnailBytes)
            throw new InvalidDataException("The thumbnail image is larger than YouTube's 2 MB limit.");

        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            UploadEndpoint + "?videoId=" + Uri.EscapeDataString(videoId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        request.Content = content;

        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = "YouTube thumbnail update failed";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var detail) &&
                detail.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
                message = detail.GetString()!.Trim();
        }
        catch (JsonException)
        {
            // Keep the readable fallback message for non-JSON errors.
        }
        throw new InvalidOperationException($"{message} (HTTP {(int)response.StatusCode}).");
    }
}
