using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record QuizPromoShortFacebookUpload(
    string VideoId,
    string Url,
    string UploadedAt);

public sealed record QuizPromoShortInstagramUpload(
    string MediaId,
    string Url,
    string UploadedAt);

public static class QuizPromoShortSocialPublicationStore
{
    private const string FacebookUploadKey = "facebook_upload";
    private const string InstagramUploadKey = "instagram_upload";

    public static QuizPromoShortFacebookUpload? LoadFacebook(string projectFolder)
    {
        try
        {
            var upload = LoadUpload(projectFolder, FacebookUploadKey);
            if (upload is null) return null;
            var videoId = upload["video_id"]?.GetValue<string>()?.Trim() ?? "";
            if (videoId.Length == 0) return null;
            return new QuizPromoShortFacebookUpload(
                videoId,
                upload["url"]?.GetValue<string>()?.Trim() ?? "",
                upload["uploaded_at"]?.GetValue<string>()?.Trim() ?? "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read promo Short Facebook upload status: {error.Message}");
            return null;
        }
    }

    public static QuizPromoShortInstagramUpload? LoadInstagram(string projectFolder)
    {
        try
        {
            var upload = LoadUpload(projectFolder, InstagramUploadKey);
            if (upload is null) return null;
            var mediaId = upload["media_id"]?.GetValue<string>()?.Trim() ?? "";
            if (mediaId.Length == 0) return null;
            return new QuizPromoShortInstagramUpload(
                mediaId,
                upload["url"]?.GetValue<string>()?.Trim() ?? "",
                upload["uploaded_at"]?.GetValue<string>()?.Trim() ?? "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read promo Short Instagram upload status: {error.Message}");
            return null;
        }
    }

    public static void RecordFacebook(
        string projectFolder,
        FacebookReelUploadResult upload,
        DateTimeOffset uploadedAt)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (string.IsNullOrWhiteSpace(upload.VideoId))
            throw new ArgumentException("Facebook did not return a Reel ID.", nameof(upload));
        RecordUpload(projectFolder, FacebookUploadKey, new JsonObject
        {
            ["video_id"] = upload.VideoId.Trim(),
            ["url"] = (upload.Url ?? "").Trim(),
            ["uploaded_at"] = uploadedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        });
    }

    public static void RecordInstagram(
        string projectFolder,
        InstagramReelUploadResult upload,
        DateTimeOffset uploadedAt)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (string.IsNullOrWhiteSpace(upload.MediaId))
            throw new ArgumentException("Instagram did not return a Reel media ID.", nameof(upload));
        RecordUpload(projectFolder, InstagramUploadKey, new JsonObject
        {
            ["media_id"] = upload.MediaId.Trim(),
            ["url"] = (upload.Url ?? "").Trim(),
            ["uploaded_at"] = uploadedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        });
    }

    public static bool AllUploaded(string projectFolder) =>
        QuizPromoShortPublicationStore.LoadYouTube(projectFolder) is not null &&
        LoadFacebook(projectFolder) is not null &&
        LoadInstagram(projectFolder) is not null;

    private static JsonObject? LoadUpload(string projectFolder, string key)
    {
        var path = QuizPromoShortPaths.Metadata(projectFolder);
        if (!File.Exists(path)) return null;
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        return root?[key] as JsonObject;
    }

    private static void RecordUpload(string projectFolder, string key, JsonObject upload)
    {
        var path = QuizPromoShortPaths.Metadata(projectFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        root[key] = upload;
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
