using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record FactburstTrackerSettings(string BaseUrl, string ApiKey)
{
    public bool IsConfigured => FactburstLinkTrackerClient.IsConfigured(BaseUrl, ApiKey);
}

public static class FactburstTrackerSettingsStore
{
    private const string FileName = "factburst-link-tracker.json";
    private const string LegacyWorkersBaseUrl = "https://go.factburstquiz.workers.dev";
    public const string DefaultBaseUrl = "https://go.factburstquiz.com";

    public static string PathFor(string appSettingsPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(appSettingsPath ?? ""));
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The app settings path is invalid.", nameof(appSettingsPath));
        return Path.Combine(directory, FileName);
    }

    public static FactburstTrackerSettings Load(string appSettingsPath)
    {
        var path = PathFor(appSettingsPath);
        try
        {
            var json = DatabaseSettingsStore.LoadOrMigrateLegacy(
                appSettingsPath,
                DatabaseSettingsStore.TrackerSettingsKey,
                path);
            if (string.IsNullOrWhiteSpace(json))
                return new FactburstTrackerSettings(DefaultBaseUrl, "");

            var root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            return new FactburstTrackerSettings(
                PreferredBaseUrl(root["base_url"]?.GetValue<string>()),
                LocalSecretProtector.Unprotect(root["api_key"]?.GetValue<string>() ?? ""));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Factburst tracker settings: {error.Message}");
            return new FactburstTrackerSettings(DefaultBaseUrl, "");
        }
    }

    public static string PreferredBaseUrl(string? baseUrl)
    {
        var value = (baseUrl ?? "").Trim().TrimEnd('/');
        if (value.Length == 0 || string.Equals(value, LegacyWorkersBaseUrl, StringComparison.OrdinalIgnoreCase))
            return DefaultBaseUrl;
        return value;
    }

    public static void Save(string appSettingsPath, string baseUrl, string apiKey)
    {
        var rootUrl = FactburstLinkTrackerClient.NormalizeBaseUrl(PreferredBaseUrl(baseUrl));
        var key = (apiKey ?? "").Trim();
        if (key.Length < 16)
            throw new ArgumentException("Tracker API key looks too short. Paste the secret generated for TRACKER_API_KEY.");

        var payload = new JsonObject
        {
            ["base_url"] = rootUrl,
            ["api_key"] = LocalSecretProtector.Protect(key),
        };
        var json = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        DatabaseSettingsStore.SaveJsonAndMirror(
            appSettingsPath,
            DatabaseSettingsStore.TrackerSettingsKey,
            PathFor(appSettingsPath),
            json);
    }
}
