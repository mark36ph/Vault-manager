using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record FactburstWebsiteQuestionReport(
    int Id,
    int QuestionPosition,
    string Reason,
    string Detail,
    string Status,
    string CreatedAt,
    string Reporter,
    string QuizSlug,
    string QuizTitle,
    string QuestionText);

public sealed record FactburstWebsiteQuestionReportSummary(int Open, int Resolved, int Dismissed);
public sealed record FactburstWebsiteQuestionReportResult(
    IReadOnlyList<FactburstWebsiteQuestionReport> Reports,
    FactburstWebsiteQuestionReportSummary Summary);

public sealed class FactburstWebsiteQuestionReportsAdminClient : IDisposable
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _client;

    public FactburstWebsiteQuestionReportsAdminClient(HttpClient? client = null) => _client = client ?? SharedClient;

    public void Dispose()
    {
    }

    public async Task<FactburstWebsiteQuestionReportResult> FetchAsync(
        string baseUrl,
        string apiKey,
        string status = "open",
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = NormalizeStatus(status, allowAll: true);
        using var document = await SendAsync(
            HttpMethod.Get,
            Endpoint(baseUrl, $"/api/site/question-reports?status={Uri.EscapeDataString(normalizedStatus)}"),
            apiKey,
            null,
            cancellationToken);

        var reports = new List<FactburstWebsiteQuestionReport>();
        if (document.RootElement.TryGetProperty("reports", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                reports.Add(new FactburstWebsiteQuestionReport(
                    ReadInt(item, "id"),
                    ReadInt(item, "question_position"),
                    ReadString(item, "reason"),
                    ReadString(item, "detail"),
                    ReadString(item, "status"),
                    ReadString(item, "created_at"),
                    ReadString(item, "reporter"),
                    ReadString(item, "quiz_slug"),
                    ReadString(item, "quiz_title"),
                    ReadString(item, "question_text")));
            }
        }

        var summary = document.RootElement.TryGetProperty("summary", out var summaryElement)
            ? new FactburstWebsiteQuestionReportSummary(
                ReadInt(summaryElement, "open"),
                ReadInt(summaryElement, "resolved"),
                ReadInt(summaryElement, "dismissed"))
            : new FactburstWebsiteQuestionReportSummary(0, 0, 0);
        return new FactburstWebsiteQuestionReportResult(reports, summary);
    }

    public async Task SetStatusAsync(
        string baseUrl,
        string apiKey,
        int reportId,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (reportId <= 0) throw new ArgumentOutOfRangeException(nameof(reportId));
        var normalizedStatus = NormalizeStatus(status, allowAll: false);
        var payload = JsonSerializer.Serialize(new { status = normalizedStatus });
        using var _ = await SendAsync(
            HttpMethod.Patch,
            Endpoint(baseUrl, $"/api/site/question-reports/{reportId}"),
            apiKey,
            payload,
            cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string url,
        string apiKey,
        string? json,
        CancellationToken cancellationToken)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add the tracker API key in Settings → Website first.");

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            try
            {
                using var error = JsonDocument.Parse(body);
                detail = ReadString(error.RootElement, "error");
            }
            catch (JsonException)
            {
            }
            throw new InvalidOperationException(
                $"Question reports request failed (HTTP {(int)response.StatusCode})" +
                (detail.Length == 0 ? "." : $": {detail}"));
        }
        return JsonDocument.Parse(body);
    }

    private static string NormalizeStatus(string status, bool allowAll)
    {
        var normalized = (status ?? "open").Trim().ToLowerInvariant();
        if (allowAll && normalized == "all") return normalized;
        if (normalized is "open" or "resolved" or "dismissed") return normalized;
        throw new ArgumentException("Status must be Open, Resolved, Dismissed or All.", nameof(status));
    }

    private static string Endpoint(string baseUrl, string path) =>
        $"{FactburstLinkTrackerClient.NormalizeBaseUrl(baseUrl)}{path}";

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int ReadInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return 0;
    }
}
