using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

public static class InstalledYouTubeOAuthClientIdRecovery
{
    private const string MarkerName = "installed-youtube-oauth-client-id-recovery-v1.json";

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
            // Recovery is best-effort and must never prevent app startup.
            Debug.WriteLine($"YouTube OAuth client ID recovery could not complete: {error}");
        }
    }

    internal static bool Run(string appDataRoot, IEnumerable<string> sourceSettingsPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceSettingsPaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var markerPath = Path.Combine(appDataRoot, MarkerName);
        var destination = ReadSettingsOrEmpty(destinationSettings);
        var installedClientId = ReadClientId(destination);

        // Never overwrite a client ID that is already configured in the installed app.
        if (installedClientId.Length > 0)
            return false;

        // If this migration previously imported the client ID and the user later cleared
        // it, respect that choice instead of resurrecting the old development value.
        if (File.Exists(markerPath))
            return false;

        var sourceClientId = FindSourceClientId(destinationSettings, sourceSettingsPaths);
        if (sourceClientId.Length == 0)
            sourceClientId = (Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_ID") ?? "").Trim();
        if (sourceClientId.Length == 0)
            return false;

        var youtube = destination["youtube"] as JsonObject;
        if (youtube is null)
        {
            youtube = new JsonObject();
            destination["youtube"] = youtube;
        }
        youtube["oauth_client_id"] = sourceClientId;

        BackupDestinationSettings(destinationSettings, appDataRoot);
        WriteSettings(destinationSettings, destination);
        WriteMarker(markerPath, sourceClientId);
        return true;
    }

    private static string FindSourceClientId(
        string destinationSettings,
        IEnumerable<string> sourceSettingsPaths)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var candidate in sourceSettingsPaths)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (PathsEqual(fullPath, destinationSettings) || !seen.Add(fullPath) || !File.Exists(fullPath))
                continue;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(fullPath)) is not JsonObject source)
                    continue;

                var clientId = ReadClientId(source);
                if (clientId.Length > 0)
                    return clientId;
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                Debug.WriteLine($"Could not inspect YouTube OAuth settings source '{fullPath}': {error.Message}");
            }
        }

        return "";
    }

    private static string ReadClientId(JsonObject root)
    {
        try
        {
            return root["youtube"]?["oauth_client_id"]?.GetValue<string>()?.Trim() ?? "";
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    private static JsonObject ReadSettingsOrEmpty(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();

        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
    }

    private static void BackupDestinationSettings(string destinationSettings, string appDataRoot)
    {
        if (!File.Exists(destinationSettings))
            return;

        var backupRoot = Path.Combine(appDataRoot, "oauth-client-id-recovery-backup");
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(
            backupRoot,
            $"settings-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Copy(destinationSettings, backup, overwrite: false);
    }

    private static void WriteSettings(string destinationSettings, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);
        var temporary = destinationSettings + ".oauth-client-id-recovery.tmp";
        File.WriteAllText(
            temporary,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, destinationSettings, overwrite: true);
    }

    private static void WriteMarker(string markerPath, string clientId)
    {
        // Store only metadata and a short non-secret suffix for diagnostics. The full
        // OAuth client ID remains in settings.json where the application already stores it.
        var suffix = clientId.Length <= 12 ? clientId : clientId[^12..];
        var marker = new JsonObject
        {
            ["version"] = 1,
            ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["client_id_suffix"] = suffix,
        };

        var temporary = markerPath + ".tmp";
        File.WriteAllText(temporary, marker.ToJsonString());
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static IEnumerable<string> CandidateSettingsPaths(string appDataRoot)
    {
        var developmentRootMarker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(developmentRootMarker))
        {
            var root = File.ReadAllText(developmentRootMarker).Trim();
            if (root.Length > 0)
                yield return Path.Combine(root, "data", "settings.json");
        }

        var migrationMarker = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (File.Exists(migrationMarker))
        {
            JsonObject? marker = null;
            try
            {
                marker = JsonNode.Parse(File.ReadAllText(migrationMarker)) as JsonObject;
            }
            catch (JsonException)
            {
            }

            var sourceData = marker?["source_data"]?.GetValue<string>()?.Trim() ?? "";
            if (sourceData.Length > 0)
                yield return Path.Combine(sourceData, "settings.json");
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (IsDevelopmentRepositoryRoot(directory.FullName))
                    yield return Path.Combine(directory.FullName, "data", "settings.json");
                directory = directory.Parent;
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in CommonCheckoutRoots(profile))
            yield return Path.Combine(root, "data", "settings.json");

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in NamedDocumentRoots(documents))
            yield return Path.Combine(root, "data", "settings.json");

        var migrationBackup = Path.Combine(appDataRoot, "migration-backup");
        if (Directory.Exists(migrationBackup))
        {
            IEnumerable<string> backups;
            try
            {
                backups = Directory.EnumerateDirectories(migrationBackup, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                backups = Array.Empty<string>();
            }

            foreach (var backup in backups)
                yield return Path.Combine(backup, "settings.json");
        }
    }

    private static IEnumerable<string> NamedDocumentRoots(string documents)
    {
        if (string.IsNullOrWhiteSpace(documents))
            yield break;

        yield return Path.Combine(documents, "FactVaultManager");
        yield return Path.Combine(documents, "Fact Vault Manager");
        yield return Path.Combine(documents, "Vault-manager");
        yield return Path.Combine(documents, "GitHub", "Vault-manager");
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

    private static bool IsDevelopmentRepositoryRoot(string root) =>
        File.Exists(Path.Combine(root, "hybrid", "FactVaultManager.Desktop", "FactVaultManager.Desktop.csproj"));

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
