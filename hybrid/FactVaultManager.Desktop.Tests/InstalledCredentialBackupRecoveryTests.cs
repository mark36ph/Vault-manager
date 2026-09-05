using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledCredentialBackupRecoveryTests
{
    [Fact]
    public void ImportsCredentialsFromFactVaultBackupWithoutReplacingInstalledDatabase()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var dataDirectory = Path.Combine(appDataRoot, "data");
        Directory.CreateDirectory(dataDirectory);

        var destinationSettings = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(
            destinationSettings,
            """{ "general": { "theme": "dark" }, "ai": { "api_key": "" } }""");

        var destinationDatabase = Path.Combine(dataDirectory, "factvault.db");
        CreateDatabase(destinationDatabase);
        DatabaseSettingsStore.SaveJson(
            destinationDatabase,
            "installed-sentinel",
            "\"keep-this-database\"");

        var backupDatabase = Path.Combine(dataDirectory, "factvault-2026-09-02-005255.db");
        CreateDatabase(backupDatabase);
        DatabaseSettingsStore.SaveJson(
            backupDatabase,
            DatabaseSettingsStore.MainSettingsKey,
            $$"""{ "ai": { "api_key": "{{LocalSecretProtector.Protect("backup-openai")}}" } }""");

        var result = InstalledCredentialBackupRecovery.Run(appDataRoot);

        Assert.Equal(1, result.RecoveredCount);
        Assert.True(result.SettingsChanged);
        Assert.Equal("\"keep-this-database\"", DatabaseSettingsStore.LoadJson(destinationDatabase, "installed-sentinel"));

        var migrated = ReadObject(destinationSettings);
        Assert.Equal("dark", migrated["general"]!["theme"]!.GetValue<string>());
        var stored = migrated["ai"]!["api_key"]!.GetValue<string>();
        Assert.StartsWith("dpapi:v1:", stored);
        Assert.Equal("backup-openai", LocalSecretProtector.Unprotect(stored));
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-credential-backup-recovery-v1.json")));
    }

    [Fact]
    public void DoesNotImportSameBackupTwice()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var dataDirectory = Path.Combine(appDataRoot, "data");
        Directory.CreateDirectory(dataDirectory);

        var destinationSettings = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(destinationSettings, """{ "ai": { "api_key": "" } }""");

        var backupDatabase = Path.Combine(dataDirectory, "credential-recovery-backup.db");
        CreateDatabase(backupDatabase);
        DatabaseSettingsStore.SaveJson(
            backupDatabase,
            DatabaseSettingsStore.MainSettingsKey,
            $$"""{ "ai": { "api_key": "{{LocalSecretProtector.Protect("backup-openai")}}" } }""");

        var first = InstalledCredentialBackupRecovery.Run(appDataRoot);
        Assert.Equal(1, first.RecoveredCount);

        var second = InstalledCredentialBackupRecovery.Run(appDataRoot);
        Assert.Equal(0, second.RecoveredCount);
        Assert.False(second.SettingsChanged);
    }

    private static void CreateDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE app_settings (setting_key TEXT PRIMARY KEY, value_json TEXT NOT NULL, updated_at_utc TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidOperationException("Expected a JSON object.");

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.CredentialBackupRecovery.Tests",
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
