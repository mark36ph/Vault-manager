using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

/// <summary>
/// Durable storage for global application settings. The SQLite database is the source of truth;
/// JSON files are retained only as migration inputs and compatibility mirrors for older builds.
/// </summary>
public static class DatabaseSettingsStore
{
    public const string MainSettingsKey = "desktop-app-settings";
    public const string AutopilotPreferencesKey = "autopilot-preferences";
    public const string TrackerSettingsKey = "factburst-link-tracker";

    public static string DatabasePathFromSettingsPath(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var fullPath = Path.GetFullPath(settingsPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        return Path.Combine(directory, "factvault.db");
    }

    public static string? LoadJson(string databasePath, string settingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        if (!File.Exists(databasePath)) return null;

        using var connection = Open(databasePath);
        EnsureTable(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM app_settings WHERE setting_key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", settingKey);
        return command.ExecuteScalar() as string;
    }

    public static void SaveJson(string databasePath, string settingKey, string valueJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        ArgumentNullException.ThrowIfNull(valueJson);
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("FactVault database was not found.", databasePath);

        using var connection = Open(databasePath);
        EnsureTable(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(setting_key, value_json, updated_at_utc)
            VALUES($key, $json, $updated)
            ON CONFLICT(setting_key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at_utc = excluded.updated_at_utc
            """;
        command.Parameters.AddWithValue("$key", settingKey);
        command.Parameters.AddWithValue("$json", valueJson);
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public static string? LoadOrMigrateLegacy(string settingsPath, string settingKey, string legacyPath)
    {
        var databasePath = DatabasePathFromSettingsPath(settingsPath);
        var databaseReadSucceeded = false;
        string? stored = null;
        try
        {
            stored = LoadJson(databasePath, settingKey);
            databaseReadSucceeded = true;
        }
        catch (SqliteException error)
        {
            // A compatibility mirror prevents a transient database lock/corruption from turning
            // into an unexpected settings reset. We never overwrite the database after a failed
            // read because an authoritative row may already exist there.
            Debug.WriteLine($"Could not read database setting '{settingKey}': {error.Message}");
        }

        if (!string.IsNullOrWhiteSpace(stored)) return stored;
        if (!File.Exists(legacyPath)) return null;

        var legacy = File.ReadAllText(legacyPath);
        if (string.IsNullOrWhiteSpace(legacy)) return null;
        if (databaseReadSucceeded && File.Exists(databasePath))
            SaveJson(databasePath, settingKey, legacy);
        return legacy;
    }

    public static void SaveJsonAndMirror(string settingsPath, string settingKey, string legacyPath, string valueJson)
    {
        var databasePath = DatabasePathFromSettingsPath(settingsPath);
        if (File.Exists(databasePath))
            SaveJson(databasePath, settingKey, valueJson);
        // Legacy recovery utilities and isolated preference tests can legitimately run before a
        // FactVault database exists. In the installed app, DesktopDataService enforces database
        // presence before user settings are saved, so this remains only a compatibility path.
        TryWriteCompatibilityMirror(legacyPath, valueJson);
    }

    public static void TryWriteCompatibilityMirror(string path, string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is null) return;
            Directory.CreateDirectory(directory);
            var temporary = path + ".mirror-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The mirror is deliberately non-authoritative. A failed JSON mirror must never
            // undo a successful database save or make the user lose newly saved settings.
            Debug.WriteLine("Could not update legacy settings mirror: " + error.Message);
        }
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    private static void EnsureTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            )
            """;
        command.ExecuteNonQuery();
    }
}

public static class AppSettingsDocumentStore
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static JsonObject Load(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var databasePath = DatabaseSettingsStore.DatabasePathFromSettingsPath(settingsPath);
        var databaseReadSucceeded = false;
        string? stored = null;
        try
        {
            stored = DatabaseSettingsStore.LoadJson(databasePath, DatabaseSettingsStore.MainSettingsKey);
            databaseReadSucceeded = true;
        }
        catch (SqliteException error)
        {
            Debug.WriteLine("Could not read app settings from database: " + error.Message);
        }

        if (TryParseObject(stored, out var databaseDocument))
            return databaseDocument;

        if (File.Exists(settingsPath))
        {
            var legacy = File.ReadAllText(settingsPath);
            if (TryParseObject(legacy, out var legacyDocument))
            {
                if (databaseReadSucceeded && File.Exists(databasePath))
                {
                    DatabaseSettingsStore.SaveJson(
                        databasePath,
                        DatabaseSettingsStore.MainSettingsKey,
                        legacyDocument.ToJsonString(IndentedJson));
                }
                return legacyDocument;
            }
        }

        return new JsonObject();
    }

    public static void Save(string settingsPath, JsonObject document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(document);
        var json = document.ToJsonString(IndentedJson);
        var databasePath = DatabaseSettingsStore.DatabasePathFromSettingsPath(settingsPath);
        if (File.Exists(databasePath))
        {
            DatabaseSettingsStore.SaveJson(
                databasePath,
                DatabaseSettingsStore.MainSettingsKey,
                json);
        }
        DatabaseSettingsStore.TryWriteCompatibilityMirror(settingsPath, json);
    }

    private static bool TryParseObject(string? json, out JsonObject document)
    {
        document = new JsonObject();
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            document = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
