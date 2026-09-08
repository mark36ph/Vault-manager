using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public sealed record SocialAgentSettings(string BaseUrl, string ApiKey)
{
    public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && ApiKey.Length >= 16;
}

public static class SocialAgentSettingsStore
{
    private const string FileName = "factburst-social-agent.json";
    private const string SettingKey = "factburst-social-agent";
    public const string DefaultBaseUrl = "https://factburstquiz.com";

    public static string PathFor(string appSettingsPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(appSettingsPath ?? ""));
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The app settings path is invalid.", nameof(appSettingsPath));
        return Path.Combine(directory, FileName);
    }

    public static SocialAgentSettings Load(string appSettingsPath)
    {
        var path = PathFor(appSettingsPath);
        try
        {
            var json = DatabaseSettingsStore.LoadOrMigrateLegacy(appSettingsPath, SettingKey, path);
            if (string.IsNullOrWhiteSpace(json))
                return new SocialAgentSettings(DefaultBaseUrl, "");

            var root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            return new SocialAgentSettings(
                NormalizeBaseUrl(root["base_url"]?.GetValue<string>()),
                LocalSecretProtector.Unprotect(root["api_key"]?.GetValue<string>() ?? ""));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Social agent settings: {error.Message}");
            return new SocialAgentSettings(DefaultBaseUrl, "");
        }
    }

    public static void Save(string appSettingsPath, string baseUrl, string apiKey)
    {
        var rootUrl = NormalizeBaseUrl(baseUrl);
        var key = (apiKey ?? "").Trim();
        if (!Uri.TryCreate(rootUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Social Agent base URL must be an HTTPS URL.");
        if (key.Length < 16)
            throw new ArgumentException("Social Agent API key looks too short.");

        var payload = new JsonObject
        {
            ["base_url"] = rootUrl,
            ["api_key"] = LocalSecretProtector.Protect(key),
        };
        DatabaseSettingsStore.SaveJsonAndMirror(
            appSettingsPath,
            SettingKey,
            PathFor(appSettingsPath),
            payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string NormalizeBaseUrl(string? value)
    {
        var url = (value ?? "").Trim().TrimEnd('/');
        return url.Length == 0 ? DefaultBaseUrl : url;
    }
}
