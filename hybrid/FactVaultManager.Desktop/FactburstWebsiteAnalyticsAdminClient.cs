using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteAnalyticsDailyRow(string Day, string EventName, long Count);

public sealed record FactburstWebsiteAnalyticsSummary(
    int Days,
    string From,
    string To,
    IReadOnlyDictionary<string, long> Events,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Quizzes,
    IReadOnlyDictionary<string, string> QuizTitles,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Sources,
    IReadOnlyList<FactburstWebsiteAnalyticsDailyRow> Daily);

public sealed class FactburstWebsiteAnalyticsAdminClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstWebsiteAnalyticsAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
    }

    public async Task<FactburstWebsiteAnalyticsSummary> FetchAsync(
        string baseUrl,
        string apiKey,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var root = FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl);
        var safeDays = Math.Clamp(days, 1, 180);
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Link Tracker first.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{root}/api/site/analytics?days={safeDays}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ReadError(body);
            throw new InvalidOperationException(
                $"Website analytics could not be loaded (HTTP {(int)response.StatusCode})" +
                (detail.Length == 0 ? "." : $": {detail}"));
        }

        using var document = JsonDocument.Parse(body);
        return Parse(document.RootElement, safeDays);
    }

    private static FactburstWebsiteAnalyticsSummary Parse(JsonElement root, int fallbackDays)
    {
        var events = ReadCountDictionary(root, "events");
        var quizzes = ReadNestedCountDictionary(root, "quizzes");
        var titles = ReadStringDictionary(root, "quiz_titles");
        var sources = ReadNestedCountDictionary(root, "sources");
        var daily = new List<FactburstWebsiteAnalyticsDailyRow>();
        if (root.TryGetProperty("daily", out var dailyElement) && dailyElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dailyElement.EnumerateArray())
            {
                daily.Add(new FactburstWebsiteAnalyticsDailyRow(
                    ReadString(item, "day"),
                    ReadString(item, "event_name"),
                    ReadLong(item, "count")));
            }
        }

        return new FactburstWebsiteAnalyticsSummary(
            ReadInt(root, "days", fallbackDays),
            ReadString(root, "from"),
            ReadString(root, "to"),
            events,
            quizzes,
            titles,
            sources,
            daily);
    }

    private static IReadOnlyDictionary<string, long> ReadCountDictionary(JsonElement root, string name)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var property in element.EnumerateObject())
            result[property.Name] = ReadLong(property.Value);
        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> ReadNestedCountDictionary(
        JsonElement root,
        string name)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var property in element.EnumerateObject())
        {
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var count in property.Value.EnumerateObject())
                    values[count.Name] = ReadLong(count.Value);
            }
            result[property.Name] = values;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(JsonElement root, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var property in element.EnumerateObject())
            result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()?.Trim() ?? property.Name
                : property.Name;
        return result;
    }

    private static string ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ReadString(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? ReadLong(value) : 0;

    private static long ReadLong(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return number;
        return 0;
    }
}
