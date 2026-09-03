using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal static class InstalledConfigurationBackupRecovery
{
    private const string RecoveryMarkerName = "installed-configuration-backup-recovery-v1.json";
    private const string DatabaseBackupPrefix = "factvault-";

    public static void Run()
    {
        var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FactVaultManager");
        try { _ = Run(appDataRoot); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException or SqliteException)
        { Debug.WriteLine($"Installed configuration backup recovery could not complete: {error}"); }
    }

    internal static InstalledConfigurationBackupRecoveryResult Run(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        appDataRoot = Path.GetFullPath(appDataRoot);
        var dataDirectory = Path.Combine(appDataRoot, "data");
        if (!Directory.Exists(dataDirectory)) return new(false, false, "none");

        var markerPath = Path.Combine(appDataRoot, RecoveryMarkerName);
        if (File.Exists(markerPath)) return new(false, false, "marker");

        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        var destination = ReadSettingsOrEmpty(settingsPath);
        var changed = false;
        var recovered = false;
        var source = "none";

        foreach (var backup in FindCandidates(dataDirectory, Path.Combine(dataDirectory, "factvault.db")))
        {
            var mainJson = TryLoad(backup, DatabaseSettingsStore.MainSettingsKey);
            if (mainJson is not null)
            {
                var sourceMain = ParseObject(mainJson);
                var clientId = sourceMain is null ? "" : ReadString(sourceMain, "youtube", "oauth_client_id");
                if (ReadString(destination, "youtube", "oauth_client_id").Length == 0 && clientId.Length > 0)
                {
                    var youtube = destination["youtube"] as JsonObject ?? new JsonObject();
                    destination["youtube"] = youtube;
                    youtube["oauth_client_id"] = clientId;
                    changed = recovered = true;
                }
            }

            var trackerJson = TryLoad(backup, DatabaseSettingsStore.TrackerSettingsKey);
            if (trackerJson is not null)
            {
                var tracker = ParseObject(trackerJson);
                var encryptedKey = tracker is null ? "" : ReadString(tracker, "api_key");
                if (encryptedKey.Length > 0)
                {
                    try
                    {
                        var clear = LocalSecretProtector.Unprotect(encryptedKey).Trim();
                        if (clear.Length >= 16 && !FactburstTrackerSettingsStore.Load(settingsPath).IsConfigured)
                        {
                            FactburstTrackerSettingsStore.Save(settingsPath, FactburstTrackerSettingsStore.PreferredBaseUrl(ReadString(tracker!, "base_url")), clear);
                            recovered = true;
                            source = "database-backup";
                        }
                    }
                    catch (InvalidOperationException) { }
                }
            }

            if (changed)
            {
                BackupDestinationSettings(settingsPath, appDataRoot);
                AppSettingsDocumentStore.Save(settingsPath, destination);
                source = "database-backup";
            }

            if (recovered) break;
        }

        if (recovered || changed) WriteMarker(markerPath, source);
        return new(recovered, changed, source);
    }

    private static IEnumerable<string> FindCandidates(string dataDirectory, string destination)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dataDirectory, DatabaseBackupPrefix + "*.db", SearchOption.TopDirectoryOnly); }
        catch { yield break; }
        foreach (var file in files.OrderByDescending(SafeLastWriteUtc))
            if (!PathsEqual(file, destination)) yield return file;
    }

    private static string? TryLoad(string databasePath, string settingKey)
    {
        try { return DatabaseSettingsStore.LoadJson(databasePath, settingKey); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or SqliteException)
        { Debug.WriteLine($"Could not inspect configuration backup '{databasePath}': {error.Message}"); return null; }
    }

    private static JsonObject? ParseObject(string json)
    { try { return JsonNode.Parse(json) as JsonObject; } catch (JsonException) { return null; } }

    private static string ReadString(JsonObject root, string section, string key)
    { try { return root[section]?[key]?.GetValue<string>()?.Trim() ?? ""; } catch (InvalidOperationException) { return ""; } }
    private static string ReadString(JsonObject root, string key)
    { try { return root[key]?.GetValue<string>()?.Trim() ?? ""; } catch (InvalidOperationException) { return ""; } }

    private static JsonObject ReadSettingsOrEmpty(string path)
    { try { return !File.Exists(path) ? new JsonObject() : JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new JsonObject(); } }

    private static void BackupDestinationSettings(string path, string appDataRoot)
    {
        if (!File.Exists(path)) return;
        var root = Path.Combine(appDataRoot, "configuration-recovery-backup");
        Directory.CreateDirectory(root);
        File.Copy(path, Path.Combine(root, $"settings-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"), false);
    }

    private static void WriteMarker(string path, string source)
    {
        var marker = new JsonObject { ["version"] = 1, ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"), ["source"] = source };
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, marker.ToJsonString());
        File.Move(temporary, path, true);
    }

    private static DateTime SafeLastWriteUtc(string path)
    { try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; } }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

internal sealed record InstalledConfigurationBackupRecoveryResult(bool Recovered, bool SettingsChanged, string Source);
