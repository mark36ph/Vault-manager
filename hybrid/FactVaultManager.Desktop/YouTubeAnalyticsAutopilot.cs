using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeGrowthMetric(
    string VideoId,
    long Views,
    double EstimatedMinutesWatched,
    double AverageViewDurationSeconds,
    double AverageViewPercentage,
    long SubscribersGained,
    long SubscribersLost,
    long Likes,
    long Comments);

public sealed record YouTubeGrowthSnapshot(
    int HistoryId,
    string VideoId,
    string Category,
    DateTime CheckedAtUtc,
    double AgeDays,
    long Views,
    double ViewsPerDay,
    double EstimatedMinutesWatched,
    double AverageViewDurationSeconds,
    double AverageViewPercentage,
    long SubscribersGained,
    long SubscribersLost,
    long Likes,
    long Comments,
    double Score,
    string Label,
    string Reason,
    bool RescuePackagePrepared = false);

public sealed record YouTubeGrowthAssessment(double Score, string Label, string Reason);

public sealed class YouTubeAnalyticsAutopilotService
{
    private const string ReportsEndpoint = "https://youtubeanalytics.googleapis.com/v2/reports";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubeAnalyticsAutopilotService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<IReadOnlyDictionary<string, YouTubeGrowthMetric>> FetchAsync(
        string accessToken,
        IEnumerable<string> videoIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Connect YouTube in Settings first.", nameof(accessToken));

        var ids = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = new Dictionary<string, YouTubeGrowthMetric>(StringComparer.Ordinal);

        for (var offset = 0; offset < ids.Length; offset += 50)
        {
            var batch = ids.Skip(offset).Take(50).ToArray();
            if (batch.Length == 0) continue;

            var parameters = new Dictionary<string, string>
            {
                ["ids"] = "channel==MINE",
                ["startDate"] = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["metrics"] = "views,estimatedMinutesWatched,averageViewDuration,averageViewPercentage,subscribersGained,subscribersLost,likes,comments",
                ["dimensions"] = "video",
                ["filters"] = "video==" + string.Join(',', batch),
                ["maxResults"] = "50",
            };
            var uri = ReportsEndpoint + "?" + string.Join("&", parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var response = await _client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(BuildApiFailureMessage(response.StatusCode, json));

            foreach (var metric in ParseResponse(json))
                results[metric.VideoId] = metric;
        }

        return results;
    }

    public static string BuildApiFailureMessage(System.Net.HttpStatusCode statusCode, string json)
    {
        var googleMessage = "";
        var reason = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("message", out var message))
                        googleMessage = message.GetString()?.Trim() ?? "";
                    if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                    {
                        var first = errors.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("reason", out var reasonElement))
                            reason = reasonElement.GetString()?.Trim() ?? "";
                    }
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    googleMessage = error.GetString()?.Trim() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            // Keep the HTTP status fallback when Google returns a non-JSON error body.
        }

        var combined = reason + " " + googleMessage;
        var apiDisabled = statusCode == System.Net.HttpStatusCode.Forbidden &&
            (combined.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("is disabled", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("API has not been used", StringComparison.OrdinalIgnoreCase));
        if (apiDisabled)
        {
            return "The YouTube Analytics API is not enabled for the Google Cloud project used by this YouTube connection. " +
                   "Open Google Cloud Console → APIs & Services → Library, select the same project as the YouTube OAuth client, enable YouTube Analytics API, wait about a minute, then refresh here. You do not need to reconnect YouTube.";
        }

        var missingPermission = statusCode == System.Net.HttpStatusCode.Unauthorized ||
            combined.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("insufficient permission", StringComparison.OrdinalIgnoreCase);
        if (missingPermission)
        {
            return "YouTube is connected, but the saved Google token does not include Analytics read access. " +
                   "Open Settings → YouTube and reconnect once, approving the YouTube Analytics permission, then refresh here.";
        }

        if (statusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return "Google denied YouTube Analytics access (HTTP 403). " +
                   (googleMessage.Length > 0 ? "Google says: " + googleMessage + " " : "") +
                   "Check that the YouTube Analytics API is enabled in the OAuth client's Google Cloud project and that the connected Google account owns or manages this channel.";
        }

        return $"YouTube Analytics could not be refreshed (HTTP {(int)statusCode})." +
               (googleMessage.Length > 0 ? " Google says: " + googleMessage : "");
    }

    public static IReadOnlyList<YouTubeGrowthMetric> ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("columnHeaders", out var headers) || headers.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return [];

        var names = headers.EnumerateArray()
            .Select(header => header.TryGetProperty("name", out var name) ? name.GetString()?.Trim() ?? "" : "")
            .ToArray();
        var index = names
            .Select((name, position) => (name, position))
            .Where(item => item.name.Length > 0)
            .ToDictionary(item => item.name, item => item.position, StringComparer.OrdinalIgnoreCase);

        var results = new List<YouTubeGrowthMetric>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) continue;
            var values = row.EnumerateArray().ToArray();
            var videoId = ReadText(values, index, "video");
            if (videoId.Length == 0) continue;
            results.Add(new YouTubeGrowthMetric(
                videoId,
                ReadLong(values, index, "views"),
                ReadDouble(values, index, "estimatedMinutesWatched"),
                ReadDouble(values, index, "averageViewDuration"),
                ReadDouble(values, index, "averageViewPercentage"),
                ReadLong(values, index, "subscribersGained"),
                ReadLong(values, index, "subscribersLost"),
                ReadLong(values, index, "likes"),
                ReadLong(values, index, "comments")));
        }
        return results;
    }

    private static string ReadText(JsonElement[] row, IReadOnlyDictionary<string, int> index, string name) =>
        index.TryGetValue(name, out var position) && position >= 0 && position < row.Length
            ? row[position].ValueKind == JsonValueKind.String ? row[position].GetString()?.Trim() ?? "" : row[position].ToString()
            : "";

    private static long ReadLong(JsonElement[] row, IReadOnlyDictionary<string, int> index, string name)
    {
        if (!index.TryGetValue(name, out var position) || position < 0 || position >= row.Length) return 0;
        var value = row[position];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return Math.Max(0, number);
        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? Math.Max(0, number)
            : 0;
    }

    private static double ReadDouble(JsonElement[] row, IReadOnlyDictionary<string, int> index, string name)
    {
        if (!index.TryGetValue(name, out var position) || position < 0 || position >= row.Length) return 0;
        var value = row[position];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return Math.Max(0, number);
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? Math.Max(0, number)
            : 0;
    }
}

public static class YouTubeGrowthClassifier
{
    public static YouTubeGrowthAssessment Assess(YouTubeGrowthMetric metric, double ageDays, double medianViewsPerDay)
    {
        ageDays = Math.Max(0.25, ageDays);
        var observedDays = Math.Min(28, ageDays);
        var viewsPerDay = metric.Views / observedDays;
        var baseline = Math.Max(1, medianViewsPerDay);
        var velocityRatio = viewsPerDay / baseline;
        var retention = Math.Clamp(metric.AverageViewPercentage, 0, 100);
        var netSubscribers = Math.Max(0, metric.SubscribersGained - metric.SubscribersLost);
        var subscribersPerThousand = metric.Views <= 0 ? 0 : netSubscribers * 1000.0 / metric.Views;
        var engagement = metric.Views <= 0 ? 0 : (metric.Likes + metric.Comments) * 100.0 / metric.Views;

        var velocityScore = Math.Clamp(velocityRatio / 1.5, 0, 1) * 45;
        var retentionScore = Math.Clamp(retention / 55.0, 0, 1) * 35;
        var subscriberScore = Math.Clamp(subscribersPerThousand / 5.0, 0, 1) * 12;
        var engagementScore = Math.Clamp(engagement / 5.0, 0, 1) * 8;
        var score = Math.Round(velocityScore + retentionScore + subscriberScore + engagementScore, 1);

        if (ageDays < 2 || metric.Views < 20)
            return new YouTubeGrowthAssessment(score, "Learning", "Wait for at least 48 hours and enough views before changing the release strategy.");
        if ((velocityRatio >= 1.5 && retention >= 40) || (subscribersPerThousand >= 4 && retention >= 40))
            return new YouTubeGrowthAssessment(score, "Winner", "Strong view velocity with healthy watch quality. Make more quizzes from this category.");
        if (velocityRatio < 0.65 && retention >= 45)
            return new YouTubeGrowthAssessment(score, "Packaging rescue", "People who watch are staying, but discovery is below the channel baseline. Prepare a fresh title/thumbnail package.");
        if (velocityRatio < 0.65 && retention < 35)
            return new YouTubeGrowthAssessment(score, "Weak topic", "Both view velocity and watch quality are below the channel baseline. Reduce this category for now.");
        return new YouTubeGrowthAssessment(score, "Healthy", "Performance is within the normal channel range. Keep it in rotation.");
    }
}

public static class YouTubeGrowthCategoryPlanner
{
    public static IReadOnlyList<string> BuildPlan(
        IEnumerable<string> availableCategories,
        IReadOnlyList<YouTubeGrowthSnapshot> snapshots,
        IReadOnlyDictionary<string, int> existingCategoryCounts,
        int count)
    {
        if (count <= 0) return [];
        var categories = availableCategories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (categories.Length == 0) return Enumerable.Repeat("General Knowledge", count).ToArray();

        var recent = snapshots
            .Where(snapshot => snapshot.CheckedAtUtc >= DateTime.UtcNow.AddDays(-60))
            .Where(snapshot => categories.Contains(snapshot.Category, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var categoryScores = categories.ToDictionary(
            category => category,
            category => recent.Where(snapshot => string.Equals(snapshot.Category, category, StringComparison.OrdinalIgnoreCase))
                .Where(snapshot => !string.Equals(snapshot.Label, "Learning", StringComparison.OrdinalIgnoreCase))
                .Select(snapshot => (double?)snapshot.Score)
                .Average() ?? 50,
            StringComparer.OrdinalIgnoreCase);
        var analysedCounts = categories.ToDictionary(
            category => category,
            category => recent.Count(snapshot => string.Equals(snapshot.Category, category, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var provenSlots = count >= 3 ? Math.Max(1, (int)Math.Round(count * 0.60, MidpointRounding.AwayFromZero)) : count;
        var rotationSlots = count >= 4 ? Math.Max(1, (int)Math.Round(count * 0.25, MidpointRounding.AwayFromZero)) : 0;
        if (provenSlots + rotationSlots > count) rotationSlots = Math.Max(0, count - provenSlots);
        var experimentSlots = Math.Max(0, count - provenSlots - rotationSlots);

        var proven = categories
            .OrderByDescending(category => categoryScores[category])
            .ThenBy(category => existingCategoryCounts.GetValueOrDefault(category, 0))
            .ThenBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rotation = categories
            .OrderBy(category => existingCategoryCounts.GetValueOrDefault(category, 0))
            .ThenByDescending(category => categoryScores[category])
            .ThenBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var experiments = categories
            .OrderBy(category => analysedCounts[category])
            .ThenBy(category => existingCategoryCounts.GetValueOrDefault(category, 0))
            .ThenBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var plan = new List<string>(count);
        AddCycled(plan, proven, provenSlots);
        AddCycled(plan, rotation, rotationSlots);
        AddCycled(plan, experiments, experimentSlots);
        return plan.Take(count).ToArray();
    }

    private static void AddCycled(List<string> target, IReadOnlyList<string> source, int count)
    {
        if (source.Count == 0 || count <= 0) return;
        for (var index = 0; index < count; index++)
            target.Add(source[index % source.Count]);
    }
}

public static class YouTubeGrowthSnapshotStore
{
    public static IReadOnlyList<YouTubeGrowthSnapshot> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<YouTubeGrowthSnapshot>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(string path, IEnumerable<YouTubeGrowthSnapshot> snapshots)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
