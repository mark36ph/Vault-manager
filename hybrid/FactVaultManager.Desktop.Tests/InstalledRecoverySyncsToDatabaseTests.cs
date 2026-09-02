using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

/// <summary>
/// End-to-end regression coverage for the exact real-world failure reported after Build 151:
/// a machine already has a <c>factvault.db</c> with a stale/blank "desktop-app-settings" row
/// (because settings were saved by a previous install), and credential/settings recovery must
/// still make the recovered values observable via <see cref="AppSettingsDocumentStore.Load"/>,
/// not just the JSON mirror.
/// </summary>
public sealed class InstalledRecoverySyncsToDatabaseTests
{
    [Fact]
    public void CredentialRecovery_SyncsRecoveredSecretsIntoStaleDatabaseRow()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);

        // Simulate a real installed upgrade: factvault.db already exists with a stale,
        // blank "desktop-app-settings" row (as if written by an older build).
        CreateDatabaseWithStaleRow(destinationSettings);

        File.WriteAllText(
            destinationSettings,
            """
            {
              "general": { "theme": "dark" },
              "ai": { "api_key": "" }
            }
            """);

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        File.WriteAllText(
            sourceSettings,
            """
            {
              "ai": { "api_key": "recovered-openai-key" },
              "youtube": { "api_key": "recovered-youtube-key" }
            }
            """);

        var result = InstalledCredentialRecovery.Run(appDataRoot, [sourceSettings]);
        Assert.True(result.SettingsChanged);
        Assert.Equal(2, result.RecoveredCount);

        // The real regression: LoadSettings() reads through AppSettingsDocumentStore.Load,
        // which checks the SQLite row first. It must reflect the recovered credentials,
        // not the stale blank row that existed before recovery ran.
        var loaded = AppSettingsDocumentStore.Load(destinationSettings);
        var openAiKey = loaded["ai"]?["api_key"]?.GetValue<string>() ?? "";
        var youtubeKey = loaded["youtube"]?["api_key"]?.GetValue<string>() ?? "";

        Assert.StartsWith("dpapi:v1:", openAiKey);
        Assert.Equal("recovered-openai-key", LocalSecretProtector.Unprotect(openAiKey));
        Assert.StartsWith("dpapi:v1:", youtubeKey);
        Assert.Equal("recovered-youtube-key", LocalSecretProtector.Unprotect(youtubeKey));
    }

    [Fact]
    public void SettingsRecovery_SyncsRecoveredNonSecretSettingsIntoStaleDatabaseRow()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationSettings)!);

        CreateDatabaseWithStaleRow(destinationSettings);

        File.WriteAllText(destinationSettings, "{}");

        var sourceSettings = Path.Combine(sandbox.Path, "legacy", "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceSettings)!);
        File.WriteAllText(
            sourceSettings,
            """
            {
              "general": {
                "projects_folder": "D:\\Quiz Projects",
                "theme": "dark"
              }
            }
            """);

        var changed = InstalledSettingsRecovery.Run(appDataRoot, [sourceSettings]);
        Assert.True(changed);

        var loaded = AppSettingsDocumentStore.Load(destinationSettings);
        Assert.Equal("D:\\Quiz Projects", loaded["general"]?["projects_folder"]?.GetValue<string>());
        Assert.Equal("dark", loaded["general"]?["theme"]?.GetValue<string>());
    }

    private static void CreateDatabaseWithStaleRow(string settingsPath)
    {
        var databasePath = DatabaseSettingsStore.DatabasePathFromSettingsPath(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
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

        DatabaseSettingsStore.SaveJson(
            databasePath,
            DatabaseSettingsStore.MainSettingsKey,
            new JsonObject { ["general"] = new JsonObject(), ["ai"] = new JsonObject() }.ToJsonString());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.InstalledRecoveryDatabaseSync.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
