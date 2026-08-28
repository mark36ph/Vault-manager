using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class FactburstFullAutopilotState
{
    public DateTime ActivatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<int> FacebookFirstCommentWatchIds { get; set; } = [];
    public List<int> YouTubePostReleaseWatchIds { get; set; } = [];
    public List<YouTubeWinnerFollowUp> WinnerFollowUps { get; set; } = [];
    public List<YouTubeWinnerPromoBundle> WinnerPromoBundles { get; set; } = [];
    public List<YouTubeReplyDraft> ReplyDrafts { get; set; } = [];
    public List<YouTubePostReleaseAuditRecord> PostReleaseAudits { get; set; } = [];
}

public static class FactburstFullAutopilotStateStore
{
    public const string FileName = "full-autopilot.json";

    public static FactburstFullAutopilotState Load(string settingsPath, DateTime? nowUtc = null)
    {
        var path = PathFor(settingsPath);
        try
        {
            if (File.Exists(path))
            {
                var state = JsonSerializer.Deserialize<FactburstFullAutopilotState>(File.ReadAllText(path), Options());
                if (state is not null)
                {
                    state.FacebookFirstCommentWatchIds ??= [];
                    state.YouTubePostReleaseWatchIds ??= [];
                    state.WinnerFollowUps ??= [];
                    state.WinnerPromoBundles ??= [];
                    state.ReplyDrafts ??= [];
                    state.PostReleaseAudits ??= [];
                    return state;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine("Could not load Full Autopilot state: " + error.Message);
        }

        return new FactburstFullAutopilotState { ActivatedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime() };
    }

    public static void Save(string settingsPath, FactburstFullAutopilotState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = PathFor(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options()));
        File.Move(temporary, path, overwrite: true);
    }

    public static string PathFor(string settingsPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, FileName);

    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
}

public static class FullAutopilotReleasePlanner
{
    public static readonly TimeSpan ActivationGrace = TimeSpan.FromHours(2);

    public static bool ShouldWatchFacebookFirstComment(
        QuizHistorySummary history,
        DateTime activatedAtUtc,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!history.PublishedOnFacebook ||
            string.IsNullOrWhiteSpace(history.FacebookUrl) ||
            string.IsNullOrWhiteSpace(history.PinnedComment) ||
            !string.IsNullOrWhiteSpace(history.FacebookFirstCommentId))
            return false;

        return IsNewScheduledRelease(history.FacebookScheduledFor, activatedAtUtc, now);
    }

    public static bool ShouldWatchYouTubePostRelease(
        QuizHistorySummary history,
        DateTime activatedAtUtc,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!history.PublishedOnYouTube ||
            !string.Equals(history.VideoType, "Video", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(history.YouTubeUrl) ||
            YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl) is null)
            return false;

        return IsNewScheduledRelease(history.YouTubeScheduledFor, activatedAtUtc, now);
    }

    public static bool IsNewScheduledRelease(string? scheduledText, DateTime activatedAtUtc, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(
                (scheduledText ?? "").Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var scheduled))
            return false;

        var activationCutoff = new DateTimeOffset(activatedAtUtc.ToUniversalTime()).Subtract(ActivationGrace);
        return scheduled > now || scheduled >= activationCutoff;
    }
}

public sealed class YouTubeWinnerFollowUp
{
    public int HistoryId { get; set; }
    public string VideoId { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime DetectedAtUtc { get; set; }
    public bool Consumed { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}

public static class YouTubeWinnerFollowUpPlanner
{
    public static int EnqueueNewWinners(
        FactburstFullAutopilotState state,
        IEnumerable<YouTubeGrowthSnapshot> snapshots,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshots);
        var added = 0;
        var existing = state.WinnerFollowUps.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);
        foreach (var snapshot in snapshots
                     .Where(item => string.Equals(item.Label, "Winner", StringComparison.OrdinalIgnoreCase))
                     .Where(item => item.CheckedAtUtc >= state.ActivatedAtUtc.Subtract(FullAutopilotReleasePlanner.ActivationGrace))
                     .OrderBy(item => item.CheckedAtUtc))
        {
            if (string.IsNullOrWhiteSpace(snapshot.VideoId) || existing.Contains(snapshot.VideoId))
                continue;
            state.WinnerFollowUps.Add(new YouTubeWinnerFollowUp
            {
                HistoryId = snapshot.HistoryId,
                VideoId = snapshot.VideoId,
                Category = snapshot.Category.Trim(),
                DetectedAtUtc = nowUtc.ToUniversalTime(),
            });
            existing.Add(snapshot.VideoId);
            added++;
        }
        return added;
    }

    public static YouTubeWinnerFollowUp? ConsumeNext(FactburstFullAutopilotState state, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        var item = state.WinnerFollowUps
            .Where(value => !value.Consumed && !string.IsNullOrWhiteSpace(value.Category))
            .OrderBy(value => value.DetectedAtUtc)
            .FirstOrDefault();
        if (item is null) return null;
        item.Consumed = true;
        item.ConsumedAtUtc = nowUtc.ToUniversalTime();
        return item;
    }
}

public sealed class YouTubeWinnerPromoBundle
{
    public int HistoryId { get; set; }
    public string SourceVideoId { get; set; } = "";
    public DateTime DetectedAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public bool Completed { get; set; }
    public string LastError { get; set; } = "";
    public List<YouTubeWinnerPromoVariant> Variants { get; set; } = [];
}

public sealed class YouTubeWinnerPromoVariant
{
    public int Number { get; set; }
    public int QuestionId { get; set; }
    public string SceneTitle { get; set; } = "";
    public string VideoPath { get; set; } = "";
    public DateTimeOffset PublishAt { get; set; }
    public string YouTubeVideoId { get; set; } = "";
    public string YouTubeUrl { get; set; } = "";
}

public static class YouTubeWinnerPromoSchedulePlanner
{
    private static readonly int[] DayOffsets = [1, 3, 6];

    public static IReadOnlyList<DateTimeOffset> Create(DateTimeOffset now, int count)
    {
        count = Math.Clamp(count, 0, DayOffsets.Length);
        var results = new List<DateTimeOffset>(count);
        for (var index = 0; index < count; index++)
        {
            var date = now.LocalDateTime.Date.AddDays(DayOffsets[index]).AddHours(18);
            var offset = TimeZoneInfo.Local.GetUtcOffset(date);
            var scheduled = new DateTimeOffset(date, offset);
            if (scheduled <= now.AddMinutes(15))
                scheduled = now.AddMinutes(30);
            results.Add(scheduled);
        }
        return results;
    }
}

public sealed class YouTubeReplyDraft
{
    public string CommentId { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string Author { get; set; } = "";
    public string CommentText { get; set; } = "";
    public string Draft { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public static class YouTubeReplyDraftPlanner
{
    public static string Draft(string? comment)
    {
        var text = (comment ?? "").Trim();
        if (text.Length == 0)
            return "Thanks for playing! What score did you get?";
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "wrong", "incorrect", "mistake", "error", "typo", "not right"))
            return "Thanks for flagging that. I’ll check it.";
        if (ContainsAny(lower, "love", "great", "awesome", "fun", "enjoyed", "amazing", "good quiz", "nice quiz"))
            return "Thanks! Glad you enjoyed it. What score did you get?";
        if (text.Contains('?'))
            return "Thanks for the question — I’ll take a look. What score did you get on the quiz?";
        if (ContainsAny(lower, "10/10", "9/10", "8/10", "7/10", "6/10", "5/10", "4/10", "3/10", "2/10", "1/10", "0/10"))
            return "Nice score — thanks for playing!";
        return "Thanks for playing! What score did you get?";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));
}

public sealed class YouTubePostReleaseAuditRecord
{
    public int HistoryId { get; set; }
    public string VideoId { get; set; } = "";
    public DateTime CheckedAtUtc { get; set; }
    public bool IsPublic { get; set; }
    public bool TitleMatches { get; set; }
    public bool ThumbnailPresent { get; set; }
    public bool PlaylistReady { get; set; }
    public bool TrackerReady { get; set; }
    public bool WebsiteReady { get; set; }
    public bool FirstCommentReady { get; set; }
    public int Repairs { get; set; }
    public string Attention { get; set; } = "";

    public bool AutomationComplete =>
        IsPublic && PlaylistReady && TrackerReady && WebsiteReady && FirstCommentReady;
}

public sealed record YouTubePostReleaseVideoState(
    string VideoId,
    string Title,
    string PrivacyStatus,
    bool ThumbnailPresent);

public sealed class YouTubePostReleaseAuditService
{
    private const string ApiRoot = "https://www.googleapis.com/youtube/v3";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubePostReleaseAuditService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<IReadOnlyDictionary<string, YouTubePostReleaseVideoState>> FetchAsync(
        string accessToken,
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Connect YouTube in Settings first.");
        ArgumentNullException.ThrowIfNull(videoIds);

        var ids = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = new Dictionary<string, YouTubePostReleaseVideoState>(StringComparer.Ordinal);
        for (var offset = 0; offset < ids.Length; offset += 50)
        {
            var batch = ids.Skip(offset).Take(50).ToArray();
            if (batch.Length == 0) continue;
            var url = ApiRoot + "/videos?part=snippet%2Cstatus&maxResults=50&id=" +
                      Uri.EscapeDataString(string.Join(',', batch));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var response = await _client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"YouTube post-release audit failed (HTTP {(int)response.StatusCode}).");
            foreach (var item in Parse(json))
                results[item.VideoId] = item;
        }
        return results;
    }

    public static IReadOnlyList<YouTubePostReleaseVideoState> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];
        var results = new List<YouTubePostReleaseVideoState>();
        foreach (var item in items.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (id.Length == 0) continue;
            var snippet = item.TryGetProperty("snippet", out var snippetValue) ? snippetValue : default;
            var status = item.TryGetProperty("status", out var statusValue) ? statusValue : default;
            var thumbnails = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("thumbnails", out var thumbnailsValue)
                ? thumbnailsValue
                : default;
            var hasThumbnail = thumbnails.ValueKind == JsonValueKind.Object && thumbnails.EnumerateObject().Any();
            results.Add(new YouTubePostReleaseVideoState(
                id,
                ReadString(snippet, "title"),
                ReadString(status, "privacyStatus").ToLowerInvariant(),
                hasThumbnail));
        }
        return results;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}
