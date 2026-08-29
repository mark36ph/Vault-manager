using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal sealed record InstalledProjectConsolidationResult(
    bool Completed,
    int FilesCopied,
    int ConflictsBackedUp,
    string SourceProjectsDirectory,
    string DestinationProjectsDirectory);

public static class InstalledProjectConsolidation
{
    private const string MarkerName = "installed-project-consolidation-v1.json";

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(appDataRoot);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or SqliteException or ArgumentException or NotSupportedException)
        {
            // Consolidation is best-effort and must never prevent the app from starting.
            Debug.WriteLine($"Installed project consolidation could not complete: {error}");
        }
    }

    internal static InstalledProjectConsolidationResult Run(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationProjects = Path.Combine(appDataRoot, "projects");
        var destinationSettings = Path.Combine(appDataRoot, "data", "settings.json");
        var databasePath = Path.Combine(appDataRoot, "data", "factvault.db");
        var markerPath = Path.Combine(appDataRoot, MarkerName);

        if (File.Exists(markerPath))
        {
            return new InstalledProjectConsolidationResult(
                Completed: true,
                FilesCopied: 0,
                ConflictsBackedUp: 0,
                SourceProjectsDirectory: "",
                DestinationProjectsDirectory: destinationProjects);
        }

        var configuredProjects = ReadProjectsFolder(destinationSettings);
        var recordedLegacyProjects = ReadMigrationSourceProjects(appDataRoot);
        var sourceProjects = ChooseSourceProjects(
            configuredProjects,
            recordedLegacyProjects,
            destinationProjects);

        // If the installed settings already point at the managed folder and there is no
        // recoverable legacy source left, the app is already self-contained.
        if (sourceProjects.Length == 0)
        {
            if (Directory.Exists(destinationProjects) &&
                (configuredProjects.Length == 0 || PathsEqual(configuredProjects, destinationProjects)))
            {
                WriteProjectsFolder(destinationSettings, destinationProjects);
                WriteMarker(markerPath, "", destinationProjects, 0, 0);
                return new InstalledProjectConsolidationResult(
                    Completed: true,
                    FilesCopied: 0,
                    ConflictsBackedUp: 0,
                    SourceProjectsDirectory: "",
                    DestinationProjectsDirectory: destinationProjects);
            }

            return new InstalledProjectConsolidationResult(
                Completed: false,
                FilesCopied: 0,
                ConflictsBackedUp: 0,
                SourceProjectsDirectory: "",
                DestinationProjectsDirectory: destinationProjects);
        }

        Directory.CreateDirectory(destinationProjects);
        var (filesCopied, conflictsBackedUp) = MergeDirectoryVerified(
            sourceProjects,
            destinationProjects,
            Path.Combine(appDataRoot, "project-consolidation-backup"));

        WriteProjectsFolder(destinationSettings, destinationProjects);
        RebaseDatabaseProjectPaths(databasePath, sourceProjects, destinationProjects);
        RebaseProjectTextFiles(destinationProjects, sourceProjects, destinationProjects);

        VerifyAllSourceFilesRepresented(sourceProjects, destinationProjects);
        VerifyDatabaseDoesNotPointAtLegacyProjects(databasePath, sourceProjects);

        WriteMarker(
            markerPath,
            sourceProjects,
            destinationProjects,
            filesCopied,
            conflictsBackedUp);

        return new InstalledProjectConsolidationResult(
            Completed: true,
            FilesCopied: filesCopied,
            ConflictsBackedUp: conflictsBackedUp,
            SourceProjectsDirectory: sourceProjects,
            DestinationProjectsDirectory: destinationProjects);
    }

    private static string ChooseSourceProjects(
        string configuredProjects,
        string recordedLegacyProjects,
        string destinationProjects)
    {
        foreach (var candidate in new[] { recordedLegacyProjects, configuredProjects })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!PathsEqual(full, destinationProjects) && Directory.Exists(full))
                return full;
        }

        return "";
    }

    private static string ReadMigrationSourceProjects(string appDataRoot)
    {
        var markerPath = Path.Combine(appDataRoot, "installed-data-migration-v2.json");
        if (!File.Exists(markerPath))
            return "";

        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject;
            var value = marker?["source_projects"]?.GetValue<string>()?.Trim() ?? "";
            return value.Length == 0 ? "" : Path.GetFullPath(value);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not read legacy project location from migration marker: {error.Message}");
            return "";
        }
    }

    private static string ReadProjectsFolder(string settingsPath)
    {
        if (!File.Exists(settingsPath))
            return "";

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(settingsPath));
            var value = node?["general"]?["projects_folder"]?.GetValue<string>()?.Trim() ?? "";
            return value.Length == 0 ? "" : Path.GetFullPath(value);
        }
        catch (Exception error) when (
            error is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not read installed Projects Folder: {error.Message}");
            return "";
        }
    }

    private static void WriteProjectsFolder(string settingsPath, string projectsFolder)
    {
        JsonObject node = File.Exists(settingsPath)
            ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var general = node["general"] as JsonObject ?? new JsonObject();
        node["general"] = general;
        general["projects_folder"] = Path.GetFullPath(projectsFolder);

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".project-consolidation.tmp";
        File.WriteAllText(
            temporary,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, overwrite: true);
    }

    private static (int FilesCopied, int ConflictsBackedUp) MergeDirectoryVerified(
        string source,
        string destination,
        string backupRoot)
    {
        var copied = 0;
        var backedUp = 0;
        string? sessionBackup = null;

        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

            if (!File.Exists(destinationFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: false);
                CopyTimestamp(sourceFile, destinationFile);
                VerifyCopiedFile(sourceFile, destinationFile);
                copied++;
                continue;
            }

            if (FilesEquivalent(sourceFile, destinationFile))
                continue;

            // Preserve whichever copy is newer. If the legacy source is newer, back up
            // the installed conflict before replacing it. If installed is newer, leave it
            // untouched so consolidation never rolls back newer installed project work.
            if (File.GetLastWriteTimeUtc(sourceFile) <= File.GetLastWriteTimeUtc(destinationFile))
                continue;

            sessionBackup ??= Path.Combine(
                backupRoot,
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            var backupFile = Path.Combine(sessionBackup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
            File.Copy(destinationFile, backupFile, overwrite: false);
            backedUp++;

            File.Copy(sourceFile, destinationFile, overwrite: true);
            CopyTimestamp(sourceFile, destinationFile);
            VerifyCopiedFile(sourceFile, destinationFile);
            copied++;
        }

        return (copied, backedUp);
    }

    private static bool FilesEquivalent(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;

        // Length is sufficient for large generated media here; timestamp is deliberately
        // not required because copies can acquire a different filesystem timestamp.
        return true;
    }

    private static void CopyTimestamp(string source, string destination)
    {
        try
        {
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not preserve project file timestamp: {error.Message}");
        }
    }

    private static void VerifyCopiedFile(string source, string destination)
    {
        if (!File.Exists(destination))
            throw new InvalidDataException($"Project consolidation verification failed: {destination}");
        if (new FileInfo(source).Length != new FileInfo(destination).Length)
            throw new InvalidDataException($"Project consolidation file size differs: {destination}");
    }

    private static void VerifyAllSourceFilesRepresented(string source, string destination)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relative);
            if (!File.Exists(destinationFile))
                throw new InvalidDataException($"Project consolidation is missing: {relative}");
        }
    }

    private static void RebaseDatabaseProjectPaths(
        string databasePath,
        string sourceProjects,
        string destinationProjects)
    {
        if (!File.Exists(databasePath))
            return;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (TableHasColumn(connection, transaction, "quiz_history", "project_folder"))
        {
            var rows = ReadTextColumn(connection, transaction, "quiz_history", "id", "project_folder");
            foreach (var (id, path) in rows)
            {
                var rebased = RebasePath(path, sourceProjects, destinationProjects);
                if (rebased is null || PathsEqual(rebased, path))
                    continue;
                UpdateTextColumn(connection, transaction, "quiz_history", "project_folder", id, rebased);
            }
        }

        if (TableHasColumn(connection, transaction, "projects", "folder"))
        {
            var rows = ReadTextColumn(connection, transaction, "projects", "id", "folder");
            foreach (var (id, path) in rows)
            {
                if (!Path.IsPathRooted(path))
                    continue;

                var rebased = RebasePath(path, sourceProjects, destinationProjects);
                if (rebased is null)
                    continue;

                var relative = Path.GetRelativePath(destinationProjects, rebased);
                UpdateTextColumn(connection, transaction, "projects", "folder", id, relative);
            }
        }

        transaction.Commit();
    }

    private static List<(long Id, string Value)> ReadTextColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string idColumn,
        string valueColumn)
    {
        var rows = new List<(long Id, string Value)>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {idColumn}, {valueColumn} FROM {table} WHERE TRIM({valueColumn}) <> ''";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    private static void UpdateTextColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        long id,
        string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET {column}=$value WHERE id=$id";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static bool TableHasColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column)
    {
        using var tableExists = connection.CreateCommand();
        tableExists.Transaction = transaction;
        tableExists.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        tableExists.Parameters.AddWithValue("$name", table);
        if (Convert.ToInt32((long)(tableExists.ExecuteScalar() ?? 0L)) == 0)
            return false;

        using var columns = connection.CreateCommand();
        columns.Transaction = transaction;
        columns.CommandText = $"PRAGMA table_info({table})";
        using var reader = columns.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void VerifyDatabaseDoesNotPointAtLegacyProjects(
        string databasePath,
        string sourceProjects)
    {
        if (!File.Exists(databasePath))
            return;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        if (TableHasColumnReadOnly(connection, "quiz_history", "project_folder"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT project_folder FROM quiz_history WHERE TRIM(project_folder) <> ''";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (IsContainedBy(path, sourceProjects))
                    throw new InvalidDataException("Quiz History still references the legacy Projects Folder.");
            }
        }
    }

    private static bool TableHasColumnReadOnly(SqliteConnection connection, string table, string column)
    {
        using var tableExists = connection.CreateCommand();
        tableExists.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        tableExists.Parameters.AddWithValue("$name", table);
        if (Convert.ToInt32((long)(tableExists.ExecuteScalar() ?? 0L)) == 0)
            return false;

        using var columns = connection.CreateCommand();
        columns.CommandText = $"PRAGMA table_info({table})";
        using var reader = columns.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RebaseProjectTextFiles(
        string projectsRoot,
        string sourceProjects,
        string destinationProjects)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".xml", ".fcpxml", ".txt", ".md", ".csv"
        };

        foreach (var path in Directory.EnumerateFiles(projectsRoot, "*", SearchOption.AllDirectories))
        {
            if (!extensions.Contains(Path.GetExtension(path)))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not inspect project text file '{path}': {error.Message}");
                continue;
            }

            var replaced = ReplacePathForms(text, sourceProjects, destinationProjects);
            if (string.Equals(text, replaced, StringComparison.Ordinal))
                continue;

            var temporary = path + ".rebase.tmp";
            File.WriteAllText(temporary, replaced);
            File.Move(temporary, path, overwrite: true);
        }
    }

    private static string ReplacePathForms(string text, string sourceRoot, string destinationRoot)
    {
        var sourceFull = Path.GetFullPath(sourceRoot).TrimEnd('\\', '/');
        var destinationFull = Path.GetFullPath(destinationRoot).TrimEnd('\\', '/');

        var result = text.Replace(sourceFull, destinationFull, StringComparison.OrdinalIgnoreCase);
        result = result.Replace(
            sourceFull.Replace("\\", "\\\\", StringComparison.Ordinal),
            destinationFull.Replace("\\", "\\\\", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
        result = result.Replace(
            sourceFull.Replace('\\', '/'),
            destinationFull.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static string? RebasePath(string path, string sourceRoot, string destinationRoot)
    {
        if (!IsContainedBy(path, sourceRoot))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            var fullSource = Path.GetFullPath(sourceRoot);
            var relative = Path.GetRelativePath(fullSource, fullPath);
            return Path.GetFullPath(Path.Combine(destinationRoot, relative));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not rebase project path '{path}': {error.Message}");
            return null;
        }
    }

    private static bool IsContainedBy(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            var fullRoot = Path.GetFullPath(root);
            var relative = Path.GetRelativePath(fullRoot, fullPath);
            return relative == "." ||
                   (!relative.Equals("..", StringComparison.Ordinal) &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
                    !Path.IsPathRooted(relative));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static void WriteMarker(
        string markerPath,
        string sourceProjects,
        string destinationProjects,
        int filesCopied,
        int conflictsBackedUp)
    {
        var marker = new JsonObject
        {
            ["version"] = 1,
            ["completed_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_projects"] = sourceProjects,
            ["destination_projects"] = destinationProjects,
            ["files_copied"] = filesCopied,
            ["conflicts_backed_up"] = conflictsBackedUp,
        };

        var temporary = markerPath + ".tmp";
        File.WriteAllText(temporary, marker.ToJsonString());
        File.Move(temporary, markerPath, overwrite: true);
    }
}
