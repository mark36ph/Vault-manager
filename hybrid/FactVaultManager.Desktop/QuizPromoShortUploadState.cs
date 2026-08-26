using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record QuizPromoShortUploadSnapshot(
    JsonNode? YouTube,
    JsonNode? Facebook,
    JsonNode? Instagram)
{
    public bool HasAny => YouTube is not null || Facebook is not null || Instagram is not null;
}

public static class QuizPromoShortUploadState
{
    private const string YouTubeUploadKey = "youtube_upload";
    private const string FacebookUploadKey = "facebook_upload";
    private const string InstagramUploadKey = "instagram_upload";

    public static QuizPromoShortUploadSnapshot Capture(string projectFolder)
    {
        var path = QuizPromoShortPaths.Metadata(projectFolder);
        if (!File.Exists(path))
            return new QuizPromoShortUploadSnapshot(null, null, null);

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return new QuizPromoShortUploadSnapshot(
                root?[YouTubeUploadKey]?.DeepClone(),
                root?[FacebookUploadKey]?.DeepClone(),
                root?[InstagramUploadKey]?.DeepClone());
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not capture promo Short upload metadata: {error.Message}");
            return new QuizPromoShortUploadSnapshot(null, null, null);
        }
    }

    public static void Restore(string projectFolder, QuizPromoShortUploadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasAny)
            return;

        var path = QuizPromoShortPaths.Metadata(projectFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root;
        try
        {
            root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not read regenerated promo Short metadata before restoring uploads: {error.Message}");
            root = new JsonObject();
        }

        RestoreNode(root, YouTubeUploadKey, snapshot.YouTube);
        RestoreNode(root, FacebookUploadKey, snapshot.Facebook);
        RestoreNode(root, InstagramUploadKey, snapshot.Instagram);

        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    public static int UploadedCount(string projectFolder)
    {
        var count = 0;
        if (QuizPromoShortPublicationStore.LoadYouTube(projectFolder) is not null)
            count++;
        if (QuizPromoShortSocialPublicationStore.LoadFacebook(projectFolder) is not null)
            count++;
        if (QuizPromoShortSocialPublicationStore.LoadInstagram(projectFolder) is not null)
            count++;
        return count;
    }

    public static string Display(string projectFolder)
    {
        if (QuizPromoShortPaths.FindExisting(projectFolder) is null)
            return "Not created";

        var uploaded = UploadedCount(projectFolder);
        return uploaded == 0 ? "Ready" : $"Uploaded {uploaded}/3";
    }

    private static void RestoreNode(JsonObject root, string key, JsonNode? value)
    {
        if (value is not null)
            root[key] = value.DeepClone();
    }
}
