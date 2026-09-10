using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record FactburstSocialReportingSettings(string ApiKey)
{
    public bool IsConfigured => ApiKey.Trim().Length >= 16;
}

public static class FactburstSocialReportingSettingsStore
{
    private const string FileName = "factburst-social-reporting.json";

    public static string PathFor(string appSettingsPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(appSettingsPath ?? ""));
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The app settings path is invalid.", nameof(appSettingsPath));
        return Path.Combine(directory, FileName);
    }

    public static FactburstSocialReportingSettings Load(string appSettingsPath)
    {
        var path = PathFor(appSettingsPath);
        try
        {
            var json = DatabaseSettingsStore.LoadOrMigrateLegacy(
                appSettingsPath,
                DatabaseSettingsStore.SocialStatsSettingsKey,
                path);
            if (string.IsNullOrWhiteSpace(json))
                return new FactburstSocialReportingSettings("");
            var root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            return new FactburstSocialReportingSettings(
                LocalSecretProtector.Unprotect(root["api_key"]?.GetValue<string>() ?? ""));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Factburst social reporting settings: {error.Message}");
            return new FactburstSocialReportingSettings("");
        }
    }

    public static void Save(string appSettingsPath, string apiKey)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length < 16)
            throw new ArgumentException("Social reporting API key looks too short.");
        var payload = new JsonObject { ["api_key"] = LocalSecretProtector.Protect(key) };
        DatabaseSettingsStore.SaveJsonAndMirror(
            appSettingsPath,
            DatabaseSettingsStore.SocialStatsSettingsKey,
            PathFor(appSettingsPath),
            payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
