using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public static class InstalledYouTubeAccountIdentityRecovery
{
    private const string MarkerName = "installed-youtube-account-identity-recovery-v1.json";

    private static readonly (string Key, string EnvironmentVariable)[] Fields =
    [
        ("approved_channel_id", "YOUTUBE_APPROVED_CHANNEL_ID"),
        ("approved_channel_name", "YOUTUBE_APPROVED_CHANNEL_NAME"),
    ];

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");
        try
        {
            _ = Run(appDataRoot, CandidateSettingsPaths(appDataRoot));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"YouTube account identity recovery could not complete: {error}");
        }
    }

    internal static int Run(string appDataRoot, IEnumerable<string> sourceSettingsPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceSettingsPaths);
        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var markerPath = Path.Combine(appDataRoot, MarkerName);
        var destination = ReadObject(destinationSettings);
        var previouslyRecovered = ReadRecoveredFields(markerPath);
        var recovered = new HashSet<string>(previouslyRecovered, StringComparer.OrdinalIgnoreCase);
        var sources = LoadSources(destinationSettings, sourceSettingsPaths);
        var youtube = destination["youtube"] as JsonObject ?? new JsonObject();
        destination["youtube"] = youtube;

        var changed = 0;
        foreach (var field in Fields)
        {
            var current = ReadString(youtube, field.Key);
            if (current.Length > 0 || previouslyRecovered.Contains(field.Key))
                continue;

            var value = sources
                .Select(source => ReadString(source["youtube"] as JsonObject, field.Key))
                .FirstOrDefault(candidate => candidate.Length > 0) ?? "";
            if (value.Length == 0)
                value = (Environment.GetEnvironmentVariable(field.EnvironmentVariable) ?? "").Trim();
            if (value.Length == 0)
                continue;

            youtube[field.Key] = value;
            recovered.Add(field.Key);
            changed++;
        }

        if (changed == 0)
            return 0;

        BackupSettings(destinationSettings, appDataRoot);
        WriteObject(destinationSettings, destination);
        WriteMarker(markerPath, recovered);
        return changed;
    }

    private static List<JsonObject> LoadSources(string destinationSettings, IEnumerable<string> paths)
    {
        var results = new List<JsonObject>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var full = TryFullPath(path);
            if (full.Length == 0 || PathsEqual(full, destinationSettings) || !seen.Add(full) || !File.Exists(full))
                continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(full)) is JsonObject root)
                    results.Add(root);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                Debug.WriteLine($"Could not inspect YouTube identity source '{full}': {error.Message}");
            }
        }
        return results;
    }

    private static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
    }

    private static string ReadString(JsonObject? section, string key)
    {
        try
        {
            return section?[key]?.GetValue<string>()?.Trim() ?? "";
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    private static HashSet<string> ReadRecoveredFields(string markerPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(markerPath))
            return result;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(markerPath))?["recovered"] is not JsonArray array)
                return result;
            foreach (var item in array)
            {
                var field = item?.GetValue<string>()?.Trim() ?? "";
                if (field.Length > 0)
                    result.Add(field);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not read YouTube identity recovery marker: {error.Message}");
        }
        return result;
    }

    private static void BackupSettings(string settingsPath, string appDataRoot)
    {
        if (!File.Exists(settingsPath))
            return;
        var root = Path.Combine(appDataRoot, "youtube-identity-recovery-backup");
        Directory.CreateDirectory(root);
        File.Copy(settingsPath, Path.Combine(root, $"settings-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
    }

    private static void WriteObject(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".youtube-identity-recovery.tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteMarker(string path, HashSet<string> recovered)
    {
        var array = new JsonArray();
        foreach (var field in recovered.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            array.Add(field);
        var marker = new JsonObject
        {
            ["version"] = 1,
            ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["recovered"] = array,
        };
        File.WriteAllText(path, marker.ToJsonString());
    }

    private static IEnumerable<string> CandidateSettingsPaths(string appDataRoot)
    {
        var migrationMarker = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (File.Exists(migrationMarker))
        {
            try
            {
                var sourceData = JsonNode.Parse(File.ReadAllText(migrationMarker))?["source_data"]?.GetValue<string>()?.Trim() ?? "";
                if (sourceData.Length > 0)
                    yield return Path.Combine(sourceData, "settings.json");
            }
            catch (JsonException)
            {
            }
        }

        var developmentRoot = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(developmentRoot))
        {
            var root = File.ReadAllText(developmentRoot).Trim();
            if (root.Length > 0)
                yield return Path.Combine(root, "data", "settings.json");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in CommonCheckoutRoots(profile))
            yield return Path.Combine(root, "data", "settings.json");
    }

    private static IEnumerable<string> CommonCheckoutRoots(string profileRoot)
    {
        if (string.IsNullOrWhiteSpace(profileRoot))
            yield break;
        yield return Path.Combine(profileRoot, "Vault-manager");
        yield return Path.Combine(profileRoot, "FactVaultManager");
        yield return Path.Combine(profileRoot, "source", "repos", "Vault-manager");
        yield return Path.Combine(profileRoot, "repos", "Vault-manager");
        yield return Path.Combine(profileRoot, "GitHub", "Vault-manager");
        yield return Path.Combine(profileRoot, "Desktop", "Vault-manager");
    }

    private static string TryFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try { return Path.GetFullPath(path.Trim()); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { return ""; }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TryFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            TryFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
