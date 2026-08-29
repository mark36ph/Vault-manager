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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or JsonException)
        {
            // Migration must never prevent the application from starting. It will retry
            // on the next launch until the completion marker has been written.
            Debug.WriteLine($"Installed data migration could not complete: {error}");
        }
    }

    internal static InstalledDataMigrationResult Run(string appDataRoot, IEnumerable<string> candidateRoots)
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
                true,
                false,
                false,
                "",
                destinationData,
                destinationProjects);
        }

        var destinationSnapshot = InspectDataDirectory(destinationData);
        var source = CandidateDataDirectories(appDataRoot, candidateRoots)
            .Select(InspectDataDirectory)
            .Where(snapshot => snapshot.DatabaseExists)
            .OrderByDescending(MigrationScore)
            .FirstOrDefault();

        if (source is null)
        {
            return new InstalledDataMigrationResult(
                false,
                false,
                false,
                "",
                destinationData,
                destinationProjects);
        }

        var sourceProjects = ReadProjectsFolder(Path.Combine(source.Directory, "settings.json"));
        var dataCopied = false;
        var projectsCopied = false;
        var sourceAccepted = false;

        if (!destinationSnapshot.HasUserData && IsBetterSource(source, destinationSnapshot))
        {
            ReplaceDirectoryVerified(source.Directory, destinationData, appDataRoot, "data");
            dataCopied = true;
            sourceAccepted = true;
        }
        else if (destinationSnapshot.HasUserData)
        {
            // Never overwrite an installed database that already contains user data.
            // This protects projects created after installation from an old checkout.
            sourceAccepted = false;
        }
        else if (PathsEqual(source.Directory, destinationData))
        {
            sourceAccepted = true;
        }

        if (!sourceAccepted)
        {
            return new InstalledDataMigrationResult(
                false,
                dataCopied,
                false,
                source.Directory,
                destinationData,
                destinationProjects);
        }

        var projectsRequired = !string.IsNullOrWhiteSpace(sourceProjects);
        var projectsReady = !projectsRequired;
        if (projectsRequired)
        {
            if (Directory.Exists(sourceProjects))
            {
                if (!PathsEqual(sourceProjects, destinationProjects))
                {
                    CopyDirectory(sourceProjects, destinationProjects, overwrite: true);
                    VerifyDirectory(sourceProjects, destinationProjects);
                    projectsCopied = true;
                }

                WriteProjectsFolder(Path.Combine(destinationData, "settings.json"), destinationProjects);
                RebaseDatabasePaths(
                    Path.Combine(destinationData, "factvault.db"),
                    sourceProjects,
                    destinationProjects,
                    Path.GetDirectoryName(source.Directory) ?? source.Directory,
                    appDataRoot);
                projectsReady = true;
            }
        }
        else
        {
            RebaseDatabasePaths(
                Path.Combine(destinationData, "factvault.db"),
                "",
                "",
                Path.GetDirectoryName(source.Directory) ?? source.Directory,
                appDataRoot);
        }

        var finalSnapshot = InspectDataDirectory(destinationData);
        var dataReady = finalSnapshot.DatabaseExists &&
                        (source.RecordCount == 0 || finalSnapshot.RecordCount >= source.RecordCount);
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
            completed,
            dataCopied,
            projectsCopied,
            source.Directory,
            destinationData,
            destinationProjects);
    }

    private static void RememberDevelopmentRoot(string appDataRoot)
    {
        var developmentRoot = FindDevelopmentRepositoryRoot();
        if (developmentRoot.Length == 0)
            return;

        Directory.CreateDirectory(appDataRoot);
        File.WriteAllText(Path.Combine(appDataRoot, "development-root.txt"), developmentRoot);
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

    private static IEnumerable<string> CandidateDataDirectories(
        string appDataRoot,
        IEnumerable<string> candidateRoots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destination = Path.GetFullPath(Path.Combine(appDataRoot, "data"));

        foreach (var root in candidateRoots)
        {
            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            var data = Path.GetFullPath(Path.Combine(fullRoot, "data"));
            if (!PathsEqual(data, destination) && seen.Add(data))
                yield return data;
        }

        // Velopack has used version/current folders under the application root.
        // Keep checking them so installs made by older builds remain recoverable.
        if (!Directory.Exists(appDataRoot))
            yield break;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(appDataRoot, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            directories = Array.Empty<string>();
        }

        foreach (var directory in directories)
        {
            var data = Path.GetFullPath(Path.Combine(directory, "data"));
            if (!PathsEqual(data, destination) && seen.Add(data))
                yield return data;
        }
    }

    private static IEnumerable<string> CandidateRoots(string appDataRoot)
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
        var profileDocuments = Path.Combine(userProfile, "Documents");
        foreach (var root in NamedDocumentRoots(profileDocuments))
            yield return root;

        foreach (var root in CommonCheckoutRoots(userProfile))
            yield return root;

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
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

    private static DataDirectorySnapshot InspectDataDirectory(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var databasePath = Path.Combine(fullDirectory, "factvault.db");
        var settingsPath = Path.Combine(fullDirectory, "settings.json");
        var databaseExists = File.Exists(databasePath);
        var databaseBytes = databaseExists ? new FileInfo(databasePath).Length : 0;
        var settingsBytes = File.Exists(settingsPath) ? new FileInfo(settingsPath).Length : 0;
        var recordCount = databaseExists ? CountUserRecords(databasePath) : 0;
        return new DataDirectorySnapshot(
            fullDirectory,
            databaseExists,
            databaseBytes,
            settingsBytes,
            recordCount);
    }

    private static long CountUserRecords(string databasePath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
            connection.Open();
            long total = 0;
            foreach (var table in new[] { "projects", "quiz_questions", "quiz_history" })
            {
                if (!TableExists(connection, table))
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
        var fileScore = Math.Min(snapshot.DatabaseBytes + snapshot.SettingsBytes, recordWeight - 1);
        return recordScore + fileScore;
    }

    private static bool IsBetterSource(DataDirectorySnapshot source, DataDirectorySnapshot destination)
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
            var root = JsonNode.Parse(File.ReadAllText(settingsPath));
            var value = root?["general"]?["projects_folder"]?.GetValue<string>()?.Trim() ?? "";
            return value.Length == 0 ? "" : Path.GetFullPath(value);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not read legacy Projects Folder: {error.Message}");
            return "";
        }
    }

    private static void WriteProjectsFolder(string settingsPath, string projectsFolder)
    {
        JsonObject root;
        if (File.Exists(settingsPath))
            root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject();
        else
            root = new JsonObject();

        var general = root["general"] as JsonObject ?? new JsonObject();
        root["general"] = general;
        general["projects_folder"] = Path.GetFullPath(projectsFolder);

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".migration.tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, settingsPath, overwrite: true);
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

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (!string.IsNullOrWhiteSpace(sourceProjects) &&
            !string.IsNullOrWhiteSpace(destinationProjects) &&
            TableExists(connection, "quiz_history"))
        {
            var paths = new List<(long Id, string Path)>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT id, project_folder FROM quiz_history WHERE TRIM(project_folder) <> ''";
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
                update.CommandText = "UPDATE quiz_history SET project_folder=$path WHERE id=$id";
                update.Parameters.AddWithValue("$path", rebased);
                update.Parameters.AddWithValue("$id", item.Id);
                update.ExecuteNonQuery();
            }
        }

        if (TableExists(connection, "quiz_questions"))
        {
            var images = new List<(long Id, string Path)>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT id, image_path FROM quiz_questions WHERE TRIM(image_path) <> ''";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                    images.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            foreach (var item in images)
            {
                var rebased = RebasePath(item.Path, sourceAppRoot, destinationAppRoot);
                if (rebased is null || PathsEqual(rebased, item.Path) || !File.Exists(rebased))
                    continue;

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE quiz_questions SET image_path=$path WHERE id=$id";
                update.Parameters.AddWithValue("$path", rebased);
                update.Parameters.AddWithValue("$id", item.Id);
                update.ExecuteNonQuery();
            }
        }

        transaction.Commit();
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
            Debug.WriteLine($"Could not rebase migrated path '{path}': {error.Message}");
            return null;
        }
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32((long)(command.ExecuteScalar() ?? 0L)) > 0;
    }

    private static void ReplaceDirectoryVerified(
        string source,
        string destination,
        string appDataRoot,
        string label)
    {
        var staging = Path.Combine(appDataRoot, $".migration-{label}-{Guid.NewGuid():N}");
        var backup = Path.Combine(
            appDataRoot,
            "migration-backup",
            $"{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..Math.Min(48, $"{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}".Length)]);

        try
        {
            CopyDirectory(source, staging, overwrite: true);
            VerifyDirectory(source, staging);

            if (Directory.Exists(destination))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }

            Directory.Move(staging, destination);
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (!Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }
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
                throw new InvalidDataException($"Migration verification failed. Missing file: {relative}");
            if (new FileInfo(sourceFile).Length != new FileInfo(destinationFile).Length)
                throw new InvalidDataException($"Migration verification failed. File size differs: {relative}");
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
        File.WriteAllText(temporary, marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static bool IsDevelopmentRepositoryRoot(string root) =>
        File.Exists(Path.Combine(root, "hybrid", "FactVaultManager.Desktop", "FactVaultManager.Desktop.csproj"));

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
