using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal sealed record InstalledDataMigrationResult(
    bool Completed,
    bool DataCopied,
    bool ProjectsCopied,
    string SourceDataDirectory,
    string DestinationDataDirectory,
    string DestinationProjectsDirectory);

public static class InstalledDataMigration
{
    private const int MigrationVersion = 2;
    private const string MigrationMarkerName = "installed-data-migration-v2.json";

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            RememberDevelopmentRoot(appDataRoot);
            _ = Run(appDataRoot, CandidateRoots(appDataRoot));
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            SqliteException or
            JsonException)
        {
            // A failed migration must not prevent the app from starting. No completion
            // marker is written, so it will be attempted again on the next launch.
            Debug.WriteLine($"Installed data migration could not complete: {error}");
        }
    }

    internal static InstalledDataMigrationResult Run(
        string appDataRoot,
        IEnumerable<string> candidateRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(candidateRoots);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationData = Path.Combine(appDataRoot, "data");
        var destinationProjects = Path.Combine(appDataRoot, "projects");
        var markerPath = Path.Combine(appDataRoot, MigrationMarkerName);

        if (File.Exists(markerPath))
        {
            return new InstalledDataMigrationResult(
                Completed: true,
                DataCopied: false,
                ProjectsCopied: false,
                SourceDataDirectory: "",
                DestinationDataDirectory: destinationData,
                DestinationProjectsDirectory: destinationProjects);
        }

        var destinationSnapshot = InspectDataDirectory(destinationData);
        if (destinationSnapshot.HasUserData)
        {
            // Never replace an installed database that already contains real user data.
            return new InstalledDataMigrationResult(
                Completed: false,
                DataCopied: false,
                ProjectsCopied: false,
                SourceDataDirectory: "",
                DestinationDataDirectory: destinationData,
                DestinationProjectsDirectory: destinationProjects);
        }

        var source = CandidateDataDirectories(appDataRoot, candidateRoots)
            .Select(InspectDataDirectory)
            .Where(snapshot => snapshot.DatabaseExists)
            .OrderByDescending(MigrationScore)
            .FirstOrDefault();

        if (source is null || !IsBetterSource(source, destinationSnapshot))
        {
            return new InstalledDataMigrationResult(
                Completed: false,
                DataCopied: false,
                ProjectsCopied: false,
                SourceDataDirectory: source?.Directory ?? "",
                DestinationDataDirectory: destinationData,
                DestinationProjectsDirectory: destinationProjects);
        }

        var sourceSettings = Path.Combine(source.Directory, "settings.json");
        var sourceProjects = ReadProjectsFolder(sourceSettings);

        ReplaceDirectoryVerified(source.Directory, destinationData, appDataRoot, "data");
        var dataCopied = true;
        var projectsCopied = false;
        var projectsReady = string.IsNullOrWhiteSpace(sourceProjects);

        if (!string.IsNullOrWhiteSpace(sourceProjects) && Directory.Exists(sourceProjects))
        {
            if (!PathsEqual(sourceProjects, destinationProjects))
            {
                CopyDirectory(sourceProjects, destinationProjects, overwrite: true);
                VerifyDirectory(sourceProjects, destinationProjects);
                projectsCopied = true;
            }

            WriteProjectsFolder(
                Path.Combine(destinationData, "settings.json"),
                destinationProjects);

            RebaseDatabasePaths(
                Path.Combine(destinationData, "factvault.db"),
                sourceProjects,
                destinationProjects,
                Path.GetDirectoryName(source.Directory) ?? source.Directory,
                appDataRoot);

            projectsReady = true;
        }
        else
        {
            // Managed quiz-question images live underneath the legacy app data root,
            // so they still need their absolute paths rebased even when no Projects
            // Folder was configured.
            RebaseDatabasePaths(
                Path.Combine(destinationData, "factvault.db"),
                sourceProjects: "",
                destinationProjects: "",
                Path.GetDirectoryName(source.Directory) ?? source.Directory,
                appDataRoot);
        }

        var finalSnapshot = InspectDataDirectory(destinationData);
        var dataReady = finalSnapshot.DatabaseExists &&
                        finalSnapshot.RecordCount >= source.RecordCount;
        var completed = dataReady && projectsReady;

        if (completed)
        {
            WriteCompletionMarker(
                markerPath,
                source.Directory,
                destinationData,
                sourceProjects,
                destinationProjects,
                dataCopied,
                projectsCopied);
        }

        return new InstalledDataMigrationResult(
            Completed: completed,
            DataCopied: dataCopied,
            ProjectsCopied: projectsCopied,
            SourceDataDirectory: source.Directory,
            DestinationDataDirectory: destinationData,
            DestinationProjectsDirectory: destinationProjects);
    }

    private static void RememberDevelopmentRoot(string appDataRoot)
    {
        var developmentRoot = FindDevelopmentRepositoryRoot();
        if (developmentRoot.Length == 0)
            return;

        Directory.CreateDirectory(appDataRoot);
        File.WriteAllText(
            Path.Combine(appDataRoot, "development-root.txt"),
            developmentRoot);
    }

    private static string FindDevelopmentRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (IsDevelopmentRepositoryRoot(directory.FullName))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        return "";
    }

    private static IEnumerable<string> CandidateRoots(string appDataRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidateRoots(appDataRoot))
        {
            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (seen.Add(full))
                yield return full;
        }
    }

    private static IEnumerable<string> EnumerateCandidateRoots(string appDataRoot)
    {
        var marker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(marker))
        {
            var marked = File.ReadAllText(marker).Trim();
            if (!string.IsNullOrWhiteSpace(marked))
                yield return marked;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in NamedDocumentRoots(documents))
            yield return root;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in NamedDocumentRoots(Path.Combine(userProfile, "Documents")))
            yield return root;
        foreach (var root in CommonCheckoutRoots(userProfile))
            yield return root;

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        foreach (var root in NamedDocumentRoots(Path.Combine(oneDrive, "Documents")))
            yield return root;
        foreach (var root in CommonCheckoutRoots(oneDrive))
            yield return root;
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

    private static IEnumerable<string> CandidateDataDirectories(
        string appDataRoot,
        IEnumerable<string> candidateRoots)
    {
        var destination = Path.GetFullPath(Path.Combine(appDataRoot, "data"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in candidateRoots)
        {
            string data;
            try
            {
                data = Path.GetFullPath(Path.Combine(root, "data"));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!PathsEqual(data, destination) && seen.Add(data))
                yield return data;
        }

        // Older Velopack layouts could briefly resolve data underneath a version/current
        // directory. Keep those locations recoverable as well.
        if (!Directory.Exists(appDataRoot))
            yield break;

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(appDataRoot, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            directories = [];
        }

        foreach (var directory in directories)
        {
            var data = Path.GetFullPath(Path.Combine(directory, "data"));
            if (!PathsEqual(data, destination) && seen.Add(data))
                yield return data;
        }
    }

    private static DataDirectorySnapshot InspectDataDirectory(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var databasePath = Path.Combine(fullDirectory, "factvault.db");
        var settingsPath = Path.Combine(fullDirectory, "settings.json");
        var databaseExists = File.Exists(databasePath);

        return new DataDirectorySnapshot(
            Directory: fullDirectory,
            DatabaseExists: databaseExists,
            DatabaseBytes: databaseExists ? new FileInfo(databasePath).Length : 0,
            SettingsBytes: File.Exists(settingsPath) ? new FileInfo(settingsPath).Length : 0,
            RecordCount: databaseExists ? CountUserRecords(databasePath) : 0);
    }

    private static long CountUserRecords(string databasePath)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            long total = 0;
            foreach (var table in new[] { "projects", "quiz_questions", "quiz_history" })
            {
                if (!TableExists(connection, transaction: null, table))
                    continue;

                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM {table}";
                total += Convert.ToInt64(command.ExecuteScalar() ?? 0L);
            }

            return total;
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static long MigrationScore(DataDirectorySnapshot snapshot)
    {
        const long recordWeight = 1_000_000_000L;
        var recordScore = snapshot.RecordCount > long.MaxValue / recordWeight
            ? long.MaxValue
            : snapshot.RecordCount * recordWeight;
        var fileScore = Math.Min(
            snapshot.DatabaseBytes + snapshot.SettingsBytes,
            recordWeight - 1);
        return recordScore + fileScore;
    }

    private static bool IsBetterSource(
        DataDirectorySnapshot source,
        DataDirectorySnapshot destination)
    {
        if (!source.DatabaseExists)
            return false;
        if (!destination.DatabaseExists)
            return true;
        if (source.RecordCount != destination.RecordCount)
            return source.RecordCount > destination.RecordCount;
        if (source.DatabaseBytes != destination.DatabaseBytes)
            return source.DatabaseBytes > destination.DatabaseBytes;
        return source.SettingsBytes > destination.SettingsBytes;
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
            error is JsonException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not read legacy Projects Folder: {error.Message}");
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
        var temporary = settingsPath + ".migration.tmp";
        File.WriteAllText(
            temporary,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, overwrite: true);
    }

    private static void ReplaceDirectoryVerified(
        string source,
        string destination,
        string appDataRoot,
        string label)
    {
        var staging = Path.Combine(
            appDataRoot,
            $".migration-{label}-{Guid.NewGuid():N}");
        var backup = Path.Combine(
            appDataRoot,
            "migration-backup",
            $"{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        var hadDestination = Directory.Exists(destination);
        var backupReady = false;

        try
        {
            CopyDirectory(source, staging, overwrite: true);
            VerifyDirectory(source, staging);

            // Microsoft.Data.Sqlite pools connections by default. A disposed pooled
            // connection can still keep a Windows handle on factvault.db, preventing
            // replacement. Clear pools before touching the bootstrap destination.
            SqliteConnection.ClearAllPools();

            if (hadDestination)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                CopyDirectory(destination, backup, overwrite: true);
                VerifyDirectory(destination, backup);
                backupReady = true;
                ClearDirectory(destination);
            }
            else
            {
                Directory.CreateDirectory(destination);
            }

            CopyDirectory(staging, destination, overwrite: true);
            VerifyDirectory(staging, destination);
        }
        catch
        {
            try
            {
                SqliteConnection.ClearAllPools();
                Directory.CreateDirectory(destination);
                ClearDirectory(destination);

                if (hadDestination && backupReady && Directory.Exists(backup))
                {
                    CopyDirectory(backup, destination, overwrite: true);
                    VerifyDirectory(backup, destination);
                }
            }
            catch (Exception rollbackError)
            {
                Debug.WriteLine($"Installed data migration rollback failed: {rollbackError}");
            }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch (Exception cleanupError)
            {
                Debug.WriteLine($"Could not remove migration staging folder: {cleanupError.Message}");
            }
        }
    }

    private static void ClearDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.GetFiles(directory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var child in Directory.GetDirectories(directory))
            Directory.Delete(child, recursive: true);
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)),
                overwrite);
        }
    }

    private static void VerifyDirectory(string source, string destination)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relative);

            if (!File.Exists(destinationFile))
                throw new InvalidDataException(
                    $"Migration verification failed. Missing file: {relative}");

            if (new FileInfo(sourceFile).Length != new FileInfo(destinationFile).Length)
                throw new InvalidDataException(
                    $"Migration verification failed. File size differs: {relative}");
        }
    }

    private static void RebaseDatabasePaths(
        string databasePath,
        string sourceProjects,
        string destinationProjects,
        string sourceAppRoot,
        string destinationAppRoot)
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

        if (!string.IsNullOrWhiteSpace(sourceProjects) &&
            !string.IsNullOrWhiteSpace(destinationProjects) &&
            TableExists(connection, transaction, "quiz_history"))
        {
            var paths = new List<(long Id, string Path)>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText =
                    "SELECT id, project_folder FROM quiz_history WHERE TRIM(project_folder) <> ''";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                    paths.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            foreach (var item in paths)
            {
                var rebased = RebasePath(item.Path, sourceProjects, destinationProjects);
                if (rebased is null || PathsEqual(rebased, item.Path))
                    continue;

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    "UPDATE quiz_history SET project_folder=$path WHERE id=$id";
                update.Parameters.AddWithValue("$path", rebased);
                update.Parameters.AddWithValue("$id", item.Id);
                update.ExecuteNonQuery();
            }
        }

        if (TableExists(connection, transaction, "quiz_questions"))
        {
            var images = new List<(long Id, string Path)>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText =
                    "SELECT id, image_path FROM quiz_questions WHERE TRIM(image_path) <> ''";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                    images.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            foreach (var item in images)
            {
                var rebased = RebasePath(item.Path, sourceAppRoot, destinationAppRoot);
                if (rebased is null ||
                    PathsEqual(rebased, item.Path) ||
                    !File.Exists(rebased))
                {
                    continue;
                }

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    "UPDATE quiz_questions SET image_path=$path WHERE id=$id";
                update.Parameters.AddWithValue("$path", rebased);
                update.Parameters.AddWithValue("$id", item.Id);
                update.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private static bool TableExists(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32((long)(command.ExecuteScalar() ?? 0L)) > 0;
    }

    private static string? RebasePath(
        string path,
        string sourceRoot,
        string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(sourceRoot) ||
            string.IsNullOrWhiteSpace(destinationRoot))
        {
            return null;
        }

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
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(destinationRoot, relative));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not rebase migrated path '{path}': {error.Message}");
            return null;
        }
    }

    private static void WriteCompletionMarker(
        string markerPath,
        string sourceData,
        string destinationData,
        string sourceProjects,
        string destinationProjects,
        bool dataCopied,
        bool projectsCopied)
    {
        var marker = new JsonObject
        {
            ["version"] = MigrationVersion,
            ["completed_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_data"] = sourceData,
            ["destination_data"] = destinationData,
            ["source_projects"] = sourceProjects,
            ["destination_projects"] = destinationProjects,
            ["data_copied"] = dataCopied,
            ["projects_copied"] = projectsCopied,
        };

        var temporary = markerPath + ".tmp";
        File.WriteAllText(
            temporary,
            marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static bool IsDevelopmentRepositoryRoot(string root) =>
        File.Exists(Path.Combine(
            root,
            "hybrid",
            "FactVaultManager.Desktop",
            "FactVaultManager.Desktop.csproj"));

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record DataDirectorySnapshot(
        string Directory,
        bool DatabaseExists,
        long DatabaseBytes,
        long SettingsBytes,
        long RecordCount)
    {
        public bool HasUserData => RecordCount > 0;
    }
}
