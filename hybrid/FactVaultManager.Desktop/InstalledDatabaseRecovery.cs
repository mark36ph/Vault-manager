using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal sealed record InstalledDatabaseRecoveryResult(
    bool Completed,
    bool DatabaseRecovered,
    string SourceDatabase,
    long SourcePrimaryRows,
    long DestinationPrimaryRows);

public static class InstalledDatabaseRecovery
{
    private const int RecoveryVersion = 1;
    private const string RecoveryMarkerName = "installed-database-recovery-v1.json";

    private static readonly string[] PrimaryTables =
    [
        "projects",
        "quiz_questions",
        "quiz_history",
        "social_upload_journal",
    ];

    private static readonly string[] SupportingTables =
    [
        "quiz_history_questions",
    ];

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(appDataRoot, CandidateDatabasePaths(appDataRoot));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException or
            SqliteException or JsonException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        {
            // Recovery is best-effort and must never prevent normal startup.
            Debug.WriteLine($"Installed database recovery could not complete: {error}");
        }
    }

    internal static InstalledDatabaseRecoveryResult Run(
        string appDataRoot,
        IEnumerable<string> sourceDatabasePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceDatabasePaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationData = Path.Combine(appDataRoot, "data");
        var destinationDatabase = Path.Combine(destinationData, "factvault.db");
        var destinationProjects = Path.Combine(appDataRoot, "projects");
        var markerPath = Path.Combine(appDataRoot, RecoveryMarkerName);

        if (File.Exists(markerPath))
        {
            var current = InspectDatabase(destinationDatabase);
            return new InstalledDatabaseRecoveryResult(
                Completed: true,
                DatabaseRecovered: false,
                SourceDatabase: "",
                SourcePrimaryRows: 0,
                DestinationPrimaryRows: current.PrimaryRows);
        }

        var destination = InspectDatabase(destinationDatabase);
        var source = sourceDatabasePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TryFullPath)
            .Where(path => path.Length > 0 && !PathsEqual(path, destinationDatabase))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(File.Exists)
            .Select(InspectDatabase)
            .Where(snapshot => snapshot.Exists && snapshot.PrimaryRows > 0)
            .OrderByDescending(snapshot => snapshot.NonEmptyPrimaryTables)
            .ThenByDescending(snapshot => snapshot.PrimaryRows)
            .ThenByDescending(snapshot => snapshot.SupportingRows)
            .ThenByDescending(snapshot => snapshot.DatabaseBytes)
            .FirstOrDefault();

        if (source is null)
        {
            return new InstalledDatabaseRecoveryResult(
                Completed: false,
                DatabaseRecovered: false,
                SourceDatabase: "",
                SourcePrimaryRows: 0,
                DestinationPrimaryRows: destination.PrimaryRows);
        }

        // Never replace an installed database that already contains meaningful user data.
        // This recovery is specifically for Build 52-54 installations that ended up with
        // an empty/bootstrap DB while the real legacy DB still exists in the checkout.
        if (destination.PrimaryRows > 0)
        {
            return new InstalledDatabaseRecoveryResult(
                Completed: false,
                DatabaseRecovered: false,
                SourceDatabase: source.Path,
                SourcePrimaryRows: source.PrimaryRows,
                DestinationPrimaryRows: destination.PrimaryRows);
        }

        Directory.CreateDirectory(destinationData);
        var stagingDatabase = Path.Combine(
            appDataRoot,
            $".database-recovery-{Guid.NewGuid():N}.db");

        try
        {
            CopyDatabaseWithSqliteBackup(source.Path, stagingDatabase);
            var staged = InspectDatabase(stagingDatabase);
            if (staged.PrimaryRows < source.PrimaryRows ||
                staged.NonEmptyPrimaryTables < source.NonEmptyPrimaryTables)
            {
                throw new InvalidDataException(
                    "Database recovery verification failed before replacing the installed database.");
            }

            BackupInstalledDatabase(destinationDatabase, appDataRoot);
            SqliteConnection.ClearAllPools();
            DeleteDatabaseSidecars(destinationDatabase);
            File.Move(stagingDatabase, destinationDatabase, overwrite: true);

            var sourceData = Path.GetDirectoryName(source.Path) ?? "";
            var sourceProjects = ResolveSourceProjects(appDataRoot, sourceData);
            RebaseRecoveredPaths(
                destinationDatabase,
                sourceData,
                destinationData,
                sourceProjects,
                destinationProjects);
            CopySupplementalManagerFiles(sourceData, destinationData);

            var recovered = InspectDatabase(destinationDatabase);
            if (recovered.PrimaryRows < source.PrimaryRows)
                throw new InvalidDataException("Recovered database contains fewer user records than the source database.");

            WriteCompletionMarker(
                markerPath,
                source.Path,
                destinationDatabase,
                sourceProjects,
                destinationProjects,
                source,
                recovered);

            return new InstalledDatabaseRecoveryResult(
                Completed: true,
                DatabaseRecovered: true,
                SourceDatabase: source.Path,
                SourcePrimaryRows: source.PrimaryRows,
                DestinationPrimaryRows: recovered.PrimaryRows);
        }
        finally
        {
            try
            {
                if (File.Exists(stagingDatabase))
                    File.Delete(stagingDatabase);
            }
            catch (Exception cleanupError)
            {
                Debug.WriteLine($"Could not remove database-recovery staging file: {cleanupError.Message}");
            }
        }
    }

    private static DatabaseSnapshot InspectDatabase(string path)
    {
        var fullPath = TryFullPath(path);
        if (fullPath.Length == 0 || !File.Exists(fullPath))
            return new DatabaseSnapshot(fullPath, false, 0, 0, 0, 0, new Dictionary<string, long>());

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in PrimaryTables.Concat(SupportingTables))
                counts[table] = CountRows(connection, table);

            var primaryRows = PrimaryTables.Sum(table => counts[table]);
            var supportingRows = SupportingTables.Sum(table => counts[table]);
            var nonEmptyPrimaryTables = PrimaryTables.Count(table => counts[table] > 0);
            return new DatabaseSnapshot(
                fullPath,
                true,
                new FileInfo(fullPath).Length,
                primaryRows,
                supportingRows,
                nonEmptyPrimaryTables,
                counts);
        }
        catch (SqliteException)
        {
            return new DatabaseSnapshot(fullPath, true, new FileInfo(fullPath).Length, 0, 0, 0,
                new Dictionary<string, long>());
        }
    }

    private static long CountRows(SqliteConnection connection, string table)
    {
        if (!TableExists(connection, table))
            return 0;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        if (!TableExists(connection, table))
            return false;

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void CopyDatabaseWithSqliteBackup(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
            File.Delete(destination);

        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };

        using var sourceConnection = new SqliteConnection(sourceBuilder.ToString());
        using var destinationConnection = new SqliteConnection(destinationBuilder.ToString());
        sourceConnection.Open();
        destinationConnection.Open();
        sourceConnection.BackupDatabase(destinationConnection);
    }

    private static void BackupInstalledDatabase(string destinationDatabase, string appDataRoot)
    {
        if (!File.Exists(destinationDatabase))
            return;

        var backupRoot = Path.Combine(appDataRoot, "database-recovery-backup");
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(
            backupRoot,
            $"factvault-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        CopyDatabaseWithSqliteBackup(destinationDatabase, backup);
    }

    private static void DeleteDatabaseSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void RebaseRecoveredPaths(
        string databasePath,
        string sourceData,
        string destinationData,
        string sourceProjects,
        string destinationProjects)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (sourceProjects.Length > 0 && Directory.Exists(destinationProjects))
        {
            if (ColumnExists(connection, "quiz_history", "project_folder"))
            {
                var rows = ReadStringRows(connection, transaction, "quiz_history", "id", "project_folder");
                foreach (var row in rows)
                {
                    var rebased = RebasePath(row.Value, sourceProjects, destinationProjects);
                    if (rebased is null || PathsEqual(rebased, row.Value))
                        continue;
                    UpdateStringRow(connection, transaction, "quiz_history", "id", row.Id, "project_folder", rebased);
                }
            }

            if (ColumnExists(connection, "projects", "folder"))
            {
                var rows = ReadStringRows(connection, transaction, "projects", "id", "folder");
                foreach (var row in rows)
                {
                    if (!Path.IsPathRooted(row.Value))
                        continue;
                    var rebased = RebasePath(row.Value, sourceProjects, destinationProjects);
                    if (rebased is null)
                        continue;
                    var relative = Path.GetRelativePath(destinationProjects, rebased);
                    UpdateStringRow(connection, transaction, "projects", "id", row.Id, "folder", relative);
                }
            }
        }

        if (sourceData.Length > 0 && ColumnExists(connection, "quiz_questions", "image_path"))
        {
            var rows = ReadStringRows(connection, transaction, "quiz_questions", "id", "image_path");
            foreach (var row in rows)
            {
                var rebased = RebasePath(row.Value, sourceData, destinationData);
                if (rebased is null || PathsEqual(rebased, row.Value) || !File.Exists(rebased))
                    continue;
                UpdateStringRow(connection, transaction, "quiz_questions", "id", row.Id, "image_path", rebased);
            }
        }

        transaction.Commit();
    }

    private static List<(long Id, string Value)> ReadStringRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string idColumn,
        string valueColumn)
    {
        var results = new List<(long Id, string Value)>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {idColumn}, {valueColumn} FROM {table} WHERE TRIM({valueColumn}) <> ''";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetInt64(0), reader.GetString(1)));
        return results;
    }

    private static void UpdateStringRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string idColumn,
        long id,
        string valueColumn,
        string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"UPDATE {table} SET {valueColumn}=$value WHERE {idColumn}=$id";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static string? RebasePath(string path, string sourceRoot, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(sourceRoot) ||
            string.IsNullOrWhiteSpace(destinationRoot))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            var fullSource = Path.GetFullPath(sourceRoot);
            var relative = Path.GetRelativePath(fullSource, fullPath);
            if (relative == ".")
                return Path.GetFullPath(destinationRoot);
            if (relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
                return null;
            return Path.GetFullPath(Path.Combine(destinationRoot, relative));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not rebase recovered path '{path}': {error.Message}");
            return null;
        }
    }

    private static string ResolveSourceProjects(string appDataRoot, string sourceData)
    {
        foreach (var markerName in new[]
                 {
                     "installed-project-consolidation-v1.json",
                     "installed-data-migration-v2.json",
                 })
        {
            var markerPath = Path.Combine(appDataRoot, markerName);
            if (!File.Exists(markerPath))
                continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(markerPath)) is not JsonObject marker)
                    continue;
                var value = marker["source_projects"]?.GetValue<string>()?.Trim() ?? "";
                if (value.Length > 0)
                    return TryFullPath(value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                Debug.WriteLine($"Could not inspect project migration marker: {error.Message}");
            }
        }

        var settingsPath = Path.Combine(sourceData, "settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                var value = JsonNode.Parse(File.ReadAllText(settingsPath))?["general"]?["projects_folder"]
                    ?.GetValue<string>()?.Trim() ?? "";
                if (value.Length > 0)
                    return TryFullPath(value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                Debug.WriteLine($"Could not read legacy Projects Folder for database recovery: {error.Message}");
            }
        }

        return "";
    }

    private static void CopySupplementalManagerFiles(string sourceData, string destinationData)
    {
        if (sourceData.Length == 0 || !Directory.Exists(sourceData))
            return;

        foreach (var name in new[] { "youtube-manager-cache.json" })
        {
            var source = Path.Combine(sourceData, name);
            var destination = Path.Combine(destinationData, name);
            if (!File.Exists(source) || File.Exists(destination))
                continue;
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static IEnumerable<string> CandidateDatabasePaths(string appDataRoot)
    {
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
                yield return Path.Combine(sourceData, "factvault.db");
        }

        var developmentRootMarker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(developmentRootMarker))
        {
            var root = File.ReadAllText(developmentRootMarker).Trim();
            if (root.Length > 0)
                yield return Path.Combine(root, "data", "factvault.db");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in CommonCheckoutRoots(profile))
            yield return Path.Combine(root, "data", "factvault.db");

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in NamedDocumentRoots(documents))
            yield return Path.Combine(root, "data", "factvault.db");

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        foreach (var root in CommonCheckoutRoots(oneDrive))
            yield return Path.Combine(root, "data", "factvault.db");

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
                yield return Path.Combine(backup, "factvault.db");
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

    private static void WriteCompletionMarker(
        string markerPath,
        string sourceDatabase,
        string destinationDatabase,
        string sourceProjects,
        string destinationProjects,
        DatabaseSnapshot source,
        DatabaseSnapshot destination)
    {
        var marker = new JsonObject
        {
            ["version"] = RecoveryVersion,
            ["completed_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_database"] = sourceDatabase,
            ["destination_database"] = destinationDatabase,
            ["source_projects"] = sourceProjects,
            ["destination_projects"] = destinationProjects,
            ["source_primary_rows"] = source.PrimaryRows,
            ["destination_primary_rows"] = destination.PrimaryRows,
            ["source_nonempty_primary_tables"] = source.NonEmptyPrimaryTables,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var temporary = markerPath + ".tmp";
        File.WriteAllText(temporary, marker.ToJsonString());
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static string TryFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var fullLeft = TryFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRight = TryFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullLeft.Length > 0 && fullRight.Length > 0 && string.Equals(
            fullLeft,
            fullRight,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private sealed record DatabaseSnapshot(
        string Path,
        bool Exists,
        long DatabaseBytes,
        long PrimaryRows,
        long SupportingRows,
        int NonEmptyPrimaryTables,
        IReadOnlyDictionary<string, long> TableCounts);
}
