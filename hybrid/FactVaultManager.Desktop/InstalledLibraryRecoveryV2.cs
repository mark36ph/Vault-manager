using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal sealed record InstalledLibraryRecoveryV2Result(
    bool Completed,
    bool DatabaseRecovered,
    string SourceDatabase,
    long SourceLibraryRows,
    long DestinationLibraryRows,
    int CandidatesInspected);

public static class InstalledLibraryRecoveryV2
{
    private const string MarkerName = "installed-library-recovery-v2.json";
    private const string DiagnosticsName = "installed-library-recovery-v2-diagnostics.json";

    private static readonly string[] LibraryTables =
    [
        "projects",
        "quiz_questions",
        "quiz_history",
    ];

    private static readonly string[] SupportingTables =
    [
        "quiz_history_questions",
        "social_upload_journal",
    ];

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "System Volume Information",
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "node_modules",
        ".git",
        ".vs",
        "bin",
        "obj",
        "packages",
    };

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            var candidates = CandidateDatabasePaths(appDataRoot).ToList();
            var destination = InspectDatabase(Path.Combine(appDataRoot, "data", "factvault.db"));

            // Only do the wider filesystem search when the installed Library/history is
            // genuinely empty and none of the known migration/check-out paths contains it.
            if (destination.LibraryRows == 0 && !ContainsUsableSource(candidates))
                candidates.AddRange(DeepDiscoverDatabasePaths(appDataRoot));

            _ = Run(appDataRoot, candidates);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException or
            SqliteException or JsonException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        {
            // Recovery must never stop the application from launching. A diagnostics file
            // is written by the inner pass whenever it can run far enough to inspect data.
            Debug.WriteLine($"Installed library recovery v2 could not complete: {error}");
        }
    }

    internal static InstalledLibraryRecoveryV2Result Run(
        string appDataRoot,
        IEnumerable<string> sourceDatabasePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(sourceDatabasePaths);

        appDataRoot = Path.GetFullPath(appDataRoot);
        Directory.CreateDirectory(appDataRoot);

        var destinationData = Path.Combine(appDataRoot, "data");
        var destinationDatabase = Path.Combine(destinationData, "factvault.db");
        var markerPath = Path.Combine(appDataRoot, MarkerName);
        var diagnosticsPath = Path.Combine(appDataRoot, DiagnosticsName);
        var destination = InspectDatabase(destinationDatabase);

        // Do not trust the Build 55 marker as proof that the correct DB was recovered.
        // Build 56 stops only when the database actually contains Library/history rows.
        if (destination.LibraryRows > 0)
        {
            WriteDiagnostics(diagnosticsPath, "installed-library-present", destination, [], null);
            return new InstalledLibraryRecoveryV2Result(
                Completed: true,
                DatabaseRecovered: false,
                SourceDatabase: "",
                SourceLibraryRows: 0,
                DestinationLibraryRows: destination.LibraryRows,
                CandidatesInspected: 0);
        }

        var snapshots = sourceDatabasePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TryFullPath)
            .Where(path => path.Length > 0 && !PathsEqual(path, destinationDatabase))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(File.Exists)
            .Select(InspectDatabase)
            .ToList();

        var source = snapshots
            .Where(snapshot => snapshot.LibraryRows > 0)
            .OrderByDescending(snapshot => snapshot.NonEmptyLibraryTables)
            .ThenByDescending(snapshot => snapshot.LibraryRows)
            .ThenByDescending(snapshot => snapshot.SupportingRows)
            .ThenByDescending(snapshot => snapshot.DatabaseBytes)
            .FirstOrDefault();

        if (source is null)
        {
            WriteDiagnostics(diagnosticsPath, "no-usable-source-found", destination, snapshots, null);
            return new InstalledLibraryRecoveryV2Result(
                Completed: false,
                DatabaseRecovered: false,
                SourceDatabase: "",
                SourceLibraryRows: 0,
                DestinationLibraryRows: destination.LibraryRows,
                CandidatesInspected: snapshots.Count);
        }

        Directory.CreateDirectory(destinationData);
        var stagingDatabase = Path.Combine(
            appDataRoot,
            $".library-recovery-v2-{Guid.NewGuid():N}.db");

        try
        {
            CopyDatabaseWithSqliteBackup(source.Path, stagingDatabase);
            var staged = InspectDatabase(stagingDatabase);
            if (staged.LibraryRows < source.LibraryRows ||
                staged.NonEmptyLibraryTables < source.NonEmptyLibraryTables)
            {
                throw new InvalidDataException(
                    "Library recovery verification failed before replacing the installed database.");
            }

            BackupInstalledDatabase(destinationDatabase, appDataRoot);
            SqliteConnection.ClearAllPools();
            DeleteDatabaseSidecars(destinationDatabase);
            File.Move(stagingDatabase, destinationDatabase, overwrite: true);

            CopySupplementalManagerFiles(
                Path.GetDirectoryName(source.Path) ?? "",
                destinationData);

            var recovered = InspectDatabase(destinationDatabase);
            if (recovered.LibraryRows < source.LibraryRows)
                throw new InvalidDataException(
                    "Recovered database contains fewer Library/history rows than the source database.");

            WriteCompletionMarker(markerPath, source, recovered);
            WriteDiagnostics(diagnosticsPath, "recovered", recovered, snapshots, source);

            return new InstalledLibraryRecoveryV2Result(
                Completed: true,
                DatabaseRecovered: true,
                SourceDatabase: source.Path,
                SourceLibraryRows: source.LibraryRows,
                DestinationLibraryRows: recovered.LibraryRows,
                CandidatesInspected: snapshots.Count);
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
                Debug.WriteLine($"Could not remove library-recovery staging database: {cleanupError.Message}");
            }
        }
    }

    private static bool ContainsUsableSource(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (InspectDatabase(path).LibraryRows > 0)
                return true;
        }
        return false;
    }

    private static IEnumerable<string> CandidateDatabasePaths(string appDataRoot)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var path in MarkerDatabasePaths(appDataRoot))
        {
            var full = TryFullPath(path);
            if (full.Length > 0 && seen.Add(full))
                yield return full;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in CommonCheckoutRoots(profile))
        {
            foreach (var path in DatabaseNamesUnderRoot(root))
            {
                var full = TryFullPath(path);
                if (full.Length > 0 && seen.Add(full))
                    yield return full;
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in NamedDocumentRoots(documents))
        {
            foreach (var path in DatabaseNamesUnderRoot(root))
            {
                var full = TryFullPath(path);
                if (full.Length > 0 && seen.Add(full))
                    yield return full;
            }
        }

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
        foreach (var root in CommonCheckoutRoots(oneDrive)
                     .Concat(NamedDocumentRoots(Path.Combine(oneDrive, "Documents"))))
        {
            foreach (var path in DatabaseNamesUnderRoot(root))
            {
                var full = TryFullPath(path);
                if (full.Length > 0 && seen.Add(full))
                    yield return full;
            }
        }

        var migrationBackup = Path.Combine(appDataRoot, "migration-backup");
        if (Directory.Exists(migrationBackup))
        {
            foreach (var path in SafeEnumerateDatabaseFiles(migrationBackup, 4, 2_000))
            {
                var full = TryFullPath(path);
                if (full.Length > 0 && seen.Add(full))
                    yield return full;
            }
        }
    }

    private static IEnumerable<string> MarkerDatabasePaths(string appDataRoot)
    {
        foreach (var markerName in new[]
                 {
                     "installed-data-migration-v2.json",
                     "installed-database-recovery-v1.json",
                     "installed-project-consolidation-v1.json",
                 })
        {
            var markerPath = Path.Combine(appDataRoot, markerName);
            if (!File.Exists(markerPath))
                continue;

            JsonObject? marker = null;
            try
            {
                marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject;
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException)
            {
                Debug.WriteLine($"Could not inspect migration marker '{markerPath}': {error.Message}");
            }

            if (marker is null)
                continue;

            var sourceDatabase = ReadString(marker, "source_database");
            if (sourceDatabase.Length > 0)
                yield return sourceDatabase;

            var sourceData = ReadString(marker, "source_data");
            if (sourceData.Length > 0)
                yield return Path.Combine(sourceData, "factvault.db");

            var sourceProjects = ReadString(marker, "source_projects");
            if (sourceProjects.Length > 0)
            {
                foreach (var path in AncestorDatabasePaths(sourceProjects))
                    yield return path;
            }
        }

        var developmentRootMarker = Path.Combine(appDataRoot, "development-root.txt");
        if (!File.Exists(developmentRootMarker))
            yield break;

        string developmentRoot;
        try
        {
            developmentRoot = File.ReadAllText(developmentRootMarker).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not inspect development root marker: {error.Message}");
            developmentRoot = "";
        }

        if (developmentRoot.Length > 0)
        {
            foreach (var path in DatabaseNamesUnderRoot(developmentRoot))
                yield return path;
        }
    }

    private static IEnumerable<string> AncestorDatabasePaths(string path)
    {
        var full = TryFullPath(path);
        if (full.Length == 0)
            yield break;

        var directory = new DirectoryInfo(full);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            foreach (var candidate in DatabaseNamesUnderRoot(directory.FullName))
                yield return candidate;
        }
    }

    private static IEnumerable<string> DatabaseNamesUnderRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            yield break;

        yield return Path.Combine(root, "data", "factvault.db");
        yield return Path.Combine(root, "factvault.db");
        yield return Path.Combine(root, "data", "factvault.sqlite");
        yield return Path.Combine(root, "data", "factvault.sqlite3");
    }

    private static IEnumerable<string> DeepDiscoverDatabasePaths(string appDataRoot)
    {
        var destinationDatabase = Path.Combine(appDataRoot, "data", "factvault.db");
        var seenFiles = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var search in BuildSearchRoots())
        {
            var root = TryFullPath(search.Root);
            if (root.Length == 0 || !Directory.Exists(root) || IsWithin(root, appDataRoot))
                continue;

            foreach (var path in SafeEnumerateDatabaseFiles(root, search.MaxDepth, search.MaxDirectories))
            {
                var full = TryFullPath(path);
                if (full.Length == 0 || PathsEqual(full, destinationDatabase) || IsWithin(full, appDataRoot))
                    continue;
                if (seenFiles.Add(full))
                    yield return full;
            }
        }
    }

    private static IReadOnlyList<SearchRoot> BuildSearchRoots()
    {
        var roots = new List<SearchRoot>();
        AddSearchRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 8, 12_000);
        AddSearchRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 8, 8_000);
        AddSearchRoot(roots, Environment.GetEnvironmentVariable("OneDrive") ?? "", 8, 12_000);
        AddSearchRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 5, 15_000);
        AddSearchRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 4, 8_000);
        AddSearchRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 4, 8_000);

        if (OperatingSystem.IsWindows())
        {
            var profileRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "";
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                drives = [];
            }

            foreach (var drive in drives)
            {
                string root = "";
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        root = drive.RootDirectory.FullName;
                }
                catch
                {
                    root = "";
                }

                if (root.Length == 0 || string.Equals(root, profileRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddSearchRoot(roots, root, 6, 20_000);
            }
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return roots
            .Where(item => !string.IsNullOrWhiteSpace(item.Root))
            .GroupBy(item => TryFullPath(item.Root), comparison)
            .Where(group => group.Key.Length > 0)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddSearchRoot(List<SearchRoot> roots, string root, int maxDepth, int maxDirectories)
    {
        if (!string.IsNullOrWhiteSpace(root))
            roots.Add(new SearchRoot(root, maxDepth, maxDirectories));
    }

    private static IEnumerable<string> SafeEnumerateDatabaseFiles(
        string root,
        int maxDepth,
        int maxDirectories)
    {
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < maxDirectories)
        {
            var (directory, depth) = queue.Dequeue();
            visited++;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch
            {
                files = [];
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".db", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".sqlite", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".sqlite3", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            if (depth >= maxDepth)
                continue;

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch
            {
                children = [];
            }

            foreach (var child in children)
            {
                var shouldAdd = false;
                try
                {
                    var info = new DirectoryInfo(child);
                    shouldAdd = !SkippedDirectoryNames.Contains(info.Name) &&
                                !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
                }
                catch
                {
                    shouldAdd = false;
                }

                if (shouldAdd)
                    queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static DatabaseSnapshot InspectDatabase(string path)
    {
        var full = TryFullPath(path);
        if (full.Length == 0 || !File.Exists(full))
            return DatabaseSnapshot.Empty(full);

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = full,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in LibraryTables.Concat(SupportingTables))
                counts[table] = CountRows(connection, table);

            return new DatabaseSnapshot(
                full,
                true,
                new FileInfo(full).Length,
                LibraryTables.Sum(table => counts[table]),
                SupportingTables.Sum(table => counts[table]),
                LibraryTables.Count(table => counts[table] > 0),
                counts);
        }
        catch (Exception error) when (
            error is SqliteException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not inspect database '{full}': {error.Message}");
            return DatabaseSnapshot.Empty(full) with
            {
                Exists = true,
                DatabaseBytes = SafeFileLength(full),
            };
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
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
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
        var backupRoot = Path.Combine(appDataRoot, "library-recovery-v2-backup");
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

    private static void CopySupplementalManagerFiles(string sourceData, string destinationData)
    {
        if (sourceData.Length == 0 || !Directory.Exists(sourceData))
            return;

        foreach (var name in new[] { "youtube-manager-cache.json" })
        {
            var source = Path.Combine(sourceData, name);
            var destination = Path.Combine(destinationData, name);
            if (!File.Exists(source))
                continue;

            if (!File.Exists(destination) ||
                new FileInfo(source).LastWriteTimeUtc > new FileInfo(destination).LastWriteTimeUtc)
                File.Copy(source, destination, overwrite: true);
        }
    }

    private static void WriteCompletionMarker(
        string markerPath,
        DatabaseSnapshot source,
        DatabaseSnapshot destination)
    {
        WriteJson(markerPath, new JsonObject
        {
            ["version"] = 2,
            ["completed_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_database"] = source.Path,
            ["destination_database"] = destination.Path,
            ["source_library_rows"] = source.LibraryRows,
            ["destination_library_rows"] = destination.LibraryRows,
            ["source_nonempty_library_tables"] = source.NonEmptyLibraryTables,
        });
    }

    private static void WriteDiagnostics(
        string path,
        string status,
        DatabaseSnapshot destination,
        IReadOnlyList<DatabaseSnapshot> candidates,
        DatabaseSnapshot? selected)
    {
        try
        {
            var candidateArray = new JsonArray();
            foreach (var candidate in candidates
                         .OrderByDescending(item => item.NonEmptyLibraryTables)
                         .ThenByDescending(item => item.LibraryRows)
                         .ThenByDescending(item => item.DatabaseBytes)
                         .Take(100))
                candidateArray.Add(SnapshotJson(candidate));

            WriteJson(path, new JsonObject
            {
                ["version"] = 2,
                ["updated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["status"] = status,
                ["destination"] = SnapshotJson(destination),
                ["selected_source"] = selected is null ? null : SnapshotJson(selected),
                ["candidates"] = candidateArray,
            });
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine($"Could not write library recovery diagnostics: {error.Message}");
        }
    }

    private static JsonObject SnapshotJson(DatabaseSnapshot snapshot)
    {
        var counts = new JsonObject();
        foreach (var pair in snapshot.TableCounts)
            counts[pair.Key] = pair.Value;

        return new JsonObject
        {
            ["path"] = snapshot.Path,
            ["exists"] = snapshot.Exists,
            ["bytes"] = snapshot.DatabaseBytes,
            ["library_rows"] = snapshot.LibraryRows,
            ["supporting_rows"] = snapshot.SupportingRows,
            ["nonempty_library_tables"] = snapshot.NonEmptyLibraryTables,
            ["tables"] = counts,
        };
    }

    private static void WriteJson(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static string ReadString(JsonObject root, string name)
    {
        try
        {
            return root[name]?.GetValue<string>()?.Trim() ?? "";
        }
        catch (InvalidOperationException)
        {
            return "";
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
            Debug.WriteLine($"Could not normalize path '{path}': {error.Message}");
            return "";
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var fullPath = TryFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = TryFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullPath.Length == 0 || fullRoot.Length == 0)
            return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.Equals(fullRoot, comparison) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool PathsEqual(string left, string right)
    {
        var fullLeft = TryFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRight = TryFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullLeft.Length > 0 && fullRight.Length > 0 && string.Equals(
            fullLeft,
            fullRight,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static long SafeFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed record SearchRoot(string Root, int MaxDepth, int MaxDirectories);

    private sealed record DatabaseSnapshot(
        string Path,
        bool Exists,
        long DatabaseBytes,
        long LibraryRows,
        long SupportingRows,
        int NonEmptyLibraryTables,
        IReadOnlyDictionary<string, long> TableCounts)
    {
        public static DatabaseSnapshot Empty(string path) =>
            new(path, false, 0, 0, 0, 0, new Dictionary<string, long>());
    }
}
