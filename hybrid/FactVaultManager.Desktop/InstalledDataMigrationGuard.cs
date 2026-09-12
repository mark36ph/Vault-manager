using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal static class InstalledDataMigrationGuard
{
    public static bool ShouldRun(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        var dataDirectory = Path.Combine(Path.GetFullPath(appDataRoot), "data");
        if (!Directory.Exists(dataDirectory))
            return true;

        var databasePath = Path.Combine(dataDirectory, "factvault.db");
        if (!File.Exists(databasePath))
            return true;

        // DatabaseBootstrap can leave a new/empty SQLite container behind when a
        // previous migration attempt could not locate the user's existing database.
        // Treat an empty database as migration-pending so a later launch can retry the
        // known migration locations. A database containing real user records remains
        // authoritative and is never replaced by migration.
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            long records = 0;
            foreach (var table in new[] { "projects", "quiz_questions", "quiz_history" })
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
                command.Parameters.AddWithValue("$table", table);
                var exists = Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
                if (!exists)
                    continue;

                using var count = connection.CreateCommand();
                count.CommandText = $"SELECT COUNT(*) FROM {table}";
                records += Convert.ToInt64(count.ExecuteScalar() ?? 0L);
            }

            return records == 0;
        }
        catch (SqliteException)
        {
            // A corrupt/incomplete database should be eligible for the normal migration
            // path rather than permanently blocking recovery behind a file-exists check.
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
