using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed record NativeAssetCandidate(
    string Provider,
    string Id,
    string Url,
    string Kind,
    string Title,
    int Width,
    int Height,
    double Duration,
    double Score,
    string Credit,
    string License,
    string SourcePage);

public sealed class NativeProviderIntegrationException : RuntimeException
{
    public NativeProviderIntegrationException(string message) : base(message) { }
}

public interface INativeAssetProvider
{
    string Name { get; }
    Task<IReadOnlyList<NativeAssetCandidate>> SearchAsync(string query, string kind, int limit, CancellationToken cancellationToken = default);
}

public abstract class NativeAssetProviderBase
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "the", "of", "for", "to", "in", "on", "with", "from", "by", "at",
        "close", "up", "photo", "photography", "portrait", "vertical", "realistic",
    };

    protected static string Required(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{name} is required");
        return text;
    }

    protected static bool CandidateIsRelevant(string query, string candidateText)
    {
        var queryWords = SearchKeywords(query);
        if (queryWords.Count == 0)
            return true;

        var candidateWords = SearchKeywords(candidateText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!queryWords.Any(candidateWords.Contains))
            return false;

        var anchors = queryWords.Take(2).ToList();
        return anchors.Count == 0 || anchors.Any(candidateWords.Contains);
    }

    private static List<string> SearchKeywords(string value)
    {
        var result = new List<string>();
        foreach (Match match in Regex.Matches((value ?? "").ToLowerInvariant(), "[a-zA-Z0-9]+"))
        {
            var word = match.Value;
            if (word.Length < 3 || StopWords.Contains(word) || result.Contains(word, StringComparer.OrdinalIgnoreCase))
                continue;
            result.Add(word);
        }
        return result;
    }

    protected static async Task<JsonDocument> GetJsonAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new NativeProviderIntegrationException(
                $"HTTP {(int)response.StatusCode}\nURL: {request.RequestUri}\nResponse:\n{body}");
        }

        try
        {
            var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new NativeProviderIntegrationException("provider response must be a JSON object");
            }
            return document;
        }
        catch (JsonException error)
        {
            throw new NativeProviderIntegrationException(error.Message);
        }
    }

    protected static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FactVaultManager/1.0 (+desktop media downloader)");
        return client;
    }

    protected static string QueryString(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
}

public sealed class NativePexelsAssetProvider : NativeAssetProviderBase, INativeAssetProvider, IDisposable
{
    private readonly string _apiKey;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public string Name => "pexels";

    public NativePexelsAssetProvider(string apiKey, HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "Pexels API key");
        _client = client ?? CreateClient(TimeSpan.FromSeconds(45));
        _ownsClient = client is null;
    }

    public async Task<IReadOnlyList<NativeAssetCandidate>> SearchAsync(string query, string kind, int limit, CancellationToken cancellationToken = default)
    {
        query = Required(query, "query");
        if (kind is not ("image" or "video"))
            return Array.Empty<NativeAssetCandidate>();

        var endpoint = kind == "image"
            ? "https://api.pexels.com/v1/search"
            : "https://api.pexels.com/v1/videos/search";
        var queryString = QueryString(new Dictionary<string, string>
        {
            ["query"] = query,
            ["per_page"] = Math.Clamp(limit, 1, 80).ToString(),
            ["orientation"] = "portrait",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue(_apiKey);
        using var document = await GetJsonAsync(_client, request, cancellationToken);
        var root = document.RootElement;
        var property = kind == "image" ? "photos" : "videos";
        if (!root.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<NativeAssetCandidate>();

        var results = new List<NativeAssetCandidate>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            string mediaUrl;
            int width;
            int height;
            double duration;
            string credit;

            if (kind == "image")
            {
                mediaUrl = "";
                if (item.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.Object)
                {
                    mediaUrl = ReadString(src, "portrait");
                    if (string.IsNullOrWhiteSpace(mediaUrl)) mediaUrl = ReadString(src, "large2x");
                    if (string.IsNullOrWhiteSpace(mediaUrl)) mediaUrl = ReadString(src, "original");
                }
                width = ReadInt(item, "width");
                height = ReadInt(item, "height");
                duration = 0;
                credit = ReadString(item, "photographer");
            }
            else
            {
                var selected = default(JsonElement?);
                var bestPixels = -1L;
                if (item.TryGetProperty("video_files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var file in files.EnumerateArray())
                    {
                        if (file.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(ReadString(file, "link")))
                            continue;
                        var pixels = (long)ReadInt(file, "width") * ReadInt(file, "height");
                        if (pixels > bestPixels)
                        {
                            bestPixels = pixels;
                            selected = file;
                        }
                    }
                }

                mediaUrl = selected is JsonElement fileElement ? ReadString(fileElement, "link") : "";
                width = selected is JsonElement selectedWidth ? ReadInt(selectedWidth, "width") : ReadInt(item, "width");
                height = selected is JsonElement selectedHeight ? ReadInt(selectedHeight, "height") : ReadInt(item, "height");
                duration = ReadDouble(item, "duration");
                credit = item.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
                    ? ReadString(user, "name")
                    : "";
            }

            var title = ReadString(item, "alt");
            var sourcePage = ReadString(item, "url");
            var candidateText = $"{title} {sourcePage}";
            if (!CandidateIsRelevant(query, candidateText) || string.IsNullOrWhiteSpace(mediaUrl))
                continue;

            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) id = mediaUrl;
            if (string.IsNullOrWhiteSpace(title)) title = sourcePage;
            results.Add(new NativeAssetCandidate(
                Name, id, mediaUrl, kind, title, width, height, duration,
                ReadDouble(item, "liked"), credit, "Pexels License", sourcePage));
        }
        return results;
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.TryGetInt32(out var result)) return result;
        return int.TryParse(value.ToString(), out result) ? result : 0;
    }

    private static double ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.TryGetDouble(out var result)) return result;
        return double.TryParse(value.ToString(), out result) ? result : 0;
    }
}

public sealed class NativePixabayAssetProvider : NativeAssetProviderBase, INativeAssetProvider, IDisposable
{
    private readonly string _apiKey;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public string Name => "pixabay";

    public NativePixabayAssetProvider(string apiKey, HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "Pixabay API key");
        _client = client ?? CreateClient(TimeSpan.FromSeconds(45));
        _ownsClient = client is null;
    }

    public async Task<IReadOnlyList<NativeAssetCandidate>> SearchAsync(string query, string kind, int limit, CancellationToken cancellationToken = default)
    {
        query = Required(query, "query");
        if (kind is not ("image" or "video"))
            return Array.Empty<NativeAssetCandidate>();

        var endpoint = kind == "image" ? "https://pixabay.com/api/" : "https://pixabay.com/api/videos/";
        var queryString = QueryString(new Dictionary<string, string>
        {
            ["key"] = _apiKey,
            ["q"] = query[..Math.Min(query.Length, 100)],
            ["per_page"] = Math.Clamp(limit, 3, 200).ToString(),
            ["safesearch"] = "true",
            ["orientation"] = "vertical",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{queryString}");
        using var document = await GetJsonAsync(_client, request, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return Array.Empty<NativeAssetCandidate>();

        var results = new List<NativeAssetCandidate>();
        foreach (var item in hits.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            string mediaUrl;
            int width;
            int height;
            double duration;

            if (kind == "image")
            {
                mediaUrl = ReadString(item, "largeImageURL");
                if (string.IsNullOrWhiteSpace(mediaUrl)) mediaUrl = ReadString(item, "webformatURL");
                width = ReadInt(item, "imageWidth");
                if (width == 0) width = ReadInt(item, "webformatWidth");
                height = ReadInt(item, "imageHeight");
                if (height == 0) height = ReadInt(item, "webformatHeight");
                duration = 0;
            }
            else
            {
                var selected = default(JsonElement?);
                var bestPixels = -1L;
                if (item.TryGetProperty("videos", out var videos) && videos.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in videos.EnumerateObject())
                    {
                        var video = property.Value;
                        if (video.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(ReadString(video, "url")))
                            continue;
                        var pixels = (long)ReadInt(video, "width") * ReadInt(video, "height");
                        if (pixels > bestPixels)
                        {
                            bestPixels = pixels;
                            selected = video;
                        }
                    }
                }

                mediaUrl = selected is JsonElement selectedVideo ? ReadString(selectedVideo, "url") : "";
                width = selected is JsonElement selectedWidth ? ReadInt(selectedWidth, "width") : 0;
                height = selected is JsonElement selectedHeight ? ReadInt(selectedHeight, "height") : 0;
                duration = ReadDouble(item, "duration");
            }

            var tags = ReadString(item, "tags");
            var sourcePage = ReadString(item, "pageURL");
            if (!CandidateIsRelevant(query, $"{tags} {sourcePage}") || string.IsNullOrWhiteSpace(mediaUrl))
                continue;

            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) id = mediaUrl;
            results.Add(new NativeAssetCandidate(
                Name, id, mediaUrl, kind, string.IsNullOrWhiteSpace(tags) ? query : tags,
                width, height, duration,
                ReadDouble(item, "likes") + ReadDouble(item, "downloads") / 1000.0,
                ReadString(item, "user"), "Pixabay Content License", sourcePage));
        }
        return results;
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.TryGetInt32(out var result)) return result;
        return int.TryParse(value.ToString(), out result) ? result : 0;
    }

    private static double ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.TryGetDouble(out var result)) return result;
        return double.TryParse(value.ToString(), out result) ? result : 0;
    }
}
