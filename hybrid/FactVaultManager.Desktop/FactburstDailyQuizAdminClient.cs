using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstDailyQuizOption(int Id, string Slug, string Title, string Category, string Status, string PublishAt);
public sealed record FactburstDailyQuizAssignment(string DayKey, int QuizId, string Slug, string Title, string Category);

public sealed class FactburstDailyQuizAdminClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;
    public FactburstDailyQuizAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;
    public void Dispose() { }

    public async Task<(IReadOnlyList<FactburstDailyQuizOption> Quizzes, IReadOnlyList<FactburstDailyQuizAssignment> Schedule)> FetchAsync(string baseUrl, string apiKey, string from, string to, CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        using var quizRequest = CreateRequest(HttpMethod.Get, root + "/api/site/quizzes", key);
        using var quizResponse = await _client.SendAsync(quizRequest, cancellationToken);
        using var quizDocument = await ReadSuccessJsonAsync(quizResponse, "The website quizzes could not be loaded", cancellationToken);
        var quizzes = new List<FactburstDailyQuizOption>();
        if (quizDocument.RootElement.TryGetProperty("quizzes", out var quizArray) && quizArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var quiz in quizArray.EnumerateArray())
                quizzes.Add(new FactburstDailyQuizOption(ReadInt(quiz, "id"), ReadString(quiz, "slug"), ReadString(quiz, "title"), ReadString(quiz, "category"), ReadString(quiz, "status"), ReadString(quiz, "publish_at")));
        }

        var query = $"?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
        using var scheduleRequest = CreateRequest(HttpMethod.Get, root + "/api/site/daily" + query, key);
        using var scheduleResponse = await _client.SendAsync(scheduleRequest, cancellationToken);
        using var scheduleDocument = await ReadSuccessJsonAsync(scheduleResponse, "The Daily Quiz schedule could not be loaded", cancellationToken);
        var schedule = new List<FactburstDailyQuizAssignment>();
        if (scheduleDocument.RootElement.TryGetProperty("schedule", out var scheduleArray) && scheduleArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in scheduleArray.EnumerateArray())
                schedule.Add(new FactburstDailyQuizAssignment(ReadString(item, "day_key"), ReadInt(item, "quiz_id"), ReadString(item, "slug"), ReadString(item, "title"), ReadString(item, "category")));
        }
        return (quizzes, schedule);
    }

    public async Task SetAsync(string baseUrl, string apiKey, string dayKey, int quizId, CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        using var request = CreateRequest(HttpMethod.Post, root + "/api/site/daily", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new { day_key = dayKey, quiz_id = quizId }), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessJsonAsync(response, "The Daily Quiz assignment could not be saved", cancellationToken);
    }

    public async Task ClearAsync(string baseUrl, string apiKey, string dayKey, CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var key = RequireApiKey(apiKey);
        using var request = CreateRequest(HttpMethod.Delete, root + "/api/site/daily/" + Uri.EscapeDataString(dayKey), key);
        using var response = await _client.SendAsync(request, cancellationToken);
        using var document = await ReadSuccessJsonAsync(response, "The Daily Quiz assignment could not be cleared", cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static async Task<JsonDocument> ReadSuccessJsonAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            try { using var errorDocument = JsonDocument.Parse(body); detail = ReadString(errorDocument.RootElement, "error"); }
            catch (JsonException) { }
            throw new InvalidOperationException(fallback + (detail.Length == 0 ? "." : $": {detail}"));
        }
        return JsonDocument.Parse(body);
    }

    private static string RequireApiKey(string apiKey)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length < 16) throw new InvalidOperationException("Add the TRACKER_API_KEY in Settings → Website & Link Tracker first.");
        return key;
    }
    private static int ReadInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;
    private static string ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
}
