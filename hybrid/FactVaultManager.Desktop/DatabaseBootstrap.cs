using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

/// <summary>
/// Creates the local database container when it does not exist yet.
/// This is bootstrap, not recovery: it never searches for or copies a database
/// from another location. Any recovery remains an explicit user action.
/// </summary>
internal static class DatabaseBootstrap
{
    public static void EnsureInstalledDatabase()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");
        var dataDirectory = Path.Combine(appDataRoot, "data");
        var databasePath = Path.Combine(dataDirectory, "factvault.db");

        if (File.Exists(databasePath))
            return;

        Directory.CreateDirectory(dataDirectory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                category TEXT NOT NULL,
                status TEXT NOT NULL,
                folder TEXT NOT NULL DEFAULT '',
                created TEXT NOT NULL DEFAULT '',
                script TEXT NOT NULL DEFAULT '',
                on_screen_text TEXT NOT NULL DEFAULT '',
                visual_plan TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                pinned_comment TEXT NOT NULL DEFAULT '',
                notes TEXT NOT NULL DEFAULT '',
                tags TEXT NOT NULL DEFAULT '',
                sources TEXT NOT NULL DEFAULT '',
                pinned INTEGER NOT NULL DEFAULT 0,
                updated TEXT NOT NULL DEFAULT ''
            );
            """;
        command.ExecuteNonQuery();
    }
}
