using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record QuizPromoRelatedVideoState(
    bool IsSet,
    string PromoVideoId,
    string LongVideoId,
    string MarkedAt);

public static class QuizPromoRelatedVideoStore
{
    public const string FileName = "youtube-related-video.json";

    public static string PathFor(string projectFolder) =>
        Path.Combine(QuizPromoShortPaths.Folder(projectFolder), FileName);

    public static QuizPromoRelatedVideoState? Load(string projectFolder)
    {
        try
        {
            var path = PathFor(projectFolder);
            if (!File.Exists(path)) return null;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null) return null;
            var isSet = root["set"]?.GetValue<bool>() ?? false;
            var promoVideoId = root["promo_video_id"]?.GetValue<string>()?.Trim() ?? "";
            var longVideoId = root["long_video_id"]?.GetValue<string>()?.Trim() ?? "";
            var markedAt = root["marked_at"]?.GetValue<string>()?.Trim() ?? "";
            return new QuizPromoRelatedVideoState(isSet, promoVideoId, longVideoId, markedAt);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read promo related-video state: {error.Message}");
            return null;
        }
    }

    public static bool IsSetFor(string projectFolder, string promoVideoId, string longVideoId)
    {
        var state = Load(projectFolder);
        return state is not null &&
               state.IsSet &&
               string.Equals(state.PromoVideoId, RequireVideoId(promoVideoId), StringComparison.Ordinal) &&
               string.Equals(state.LongVideoId, RequireVideoId(longVideoId), StringComparison.Ordinal);
    }

    public static void MarkSet(
        string projectFolder,
        string promoVideoId,
        string longVideoId,
        DateTimeOffset markedAt)
    {
        var promoId = RequireVideoId(promoVideoId);
        var longId = RequireVideoId(longVideoId);
        var path = PathFor(projectFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new JsonObject
        {
            ["set"] = true,
            ["promo_video_id"] = promoId,
            ["long_video_id"] = longId,
            ["marked_at"] = markedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };
        WriteAtomic(path, payload);
    }

    public static void MarkNeedsSetting(string projectFolder)
    {
        var path = PathFor(projectFolder);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Could not clear the saved Related video checklist state.", error);
        }
    }

    private static string RequireVideoId(string? value)
    {
        var id = (value ?? "").Trim();
        if (id.Length == 0)
            throw new ArgumentException("A YouTube video ID is required.");
        if (id.Any(character => char.IsWhiteSpace(character) || character is '/' or '\\' or '?' or '#' or '&'))
            throw new ArgumentException("The YouTube video ID is not valid.");
        return id;
    }

    private static void WriteAtomic(string path, JsonObject payload)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(
                temporary,
                payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}

public static class QuizPromoRelatedVideoLinks
{
    public static string StudioEditUrl(string promoVideoId)
    {
        var id = RequireVideoId(promoVideoId);
        return $"https://studio.youtube.com/video/{Uri.EscapeDataString(id)}/edit";
    }

    public static string WatchUrl(string longVideoId)
    {
        var id = RequireVideoId(longVideoId);
        return $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}";
    }

    private static string RequireVideoId(string? value)
    {
        var id = (value ?? "").Trim();
        if (id.Length == 0)
            throw new ArgumentException("A YouTube video ID is required.");
        if (id.Any(character => char.IsWhiteSpace(character) || character is '/' or '\\' or '?' or '#' or '&'))
            throw new ArgumentException("The YouTube video ID is not valid.");
        return id;
    }
}

public sealed record QuizPromoRelatedVideoTarget(
    int HistoryId,
    string Title,
    string ProjectFolder,
    string PromoVideoId,
    string LongVideoId,
    string LongVideoUrl,
    string CampaignSlug);

public static class QuizPromoRelatedVideoPlanner
{
    public static IReadOnlyList<QuizPromoRelatedVideoTarget> Build(IEnumerable<QuizHistorySummary> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);
        var result = new List<QuizPromoRelatedVideoTarget>();
        foreach (var history in histories)
        {
            if (!history.PublishedOnYouTube ||
                !string.Equals(history.VideoType, "Video", StringComparison.Ordinal))
                continue;

            var promo = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
            if (promo is null) continue;

            string longVideoId;
            try
            {
                longVideoId = YouTubeVideoReference.ParseVideoId(history.YouTubeUrl);
            }
            catch
            {
                longVideoId = "";
            }

            result.Add(new QuizPromoRelatedVideoTarget(
                history.Id,
                history.UploadTitleDisplay,
                history.ProjectFolder,
                promo.VideoId,
                longVideoId,
                history.YouTubeUrl,
                FactburstLinkTrackerClient.CampaignSlug(history)));
        }

        return result
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.HistoryId)
            .ToList();
    }
}
