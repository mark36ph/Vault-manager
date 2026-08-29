using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

internal sealed record InstalledQuestionLibraryRecoveryV3Result(
    bool Completed,
    bool DatabaseRecovered,
    string SourceDatabase,
    long SourceQuestions,
    long DestinationQuestions,
    int CandidatesInspected);

public static class InstalledQuestionLibraryRecoveryV3
{
    private const string MarkerName = "installed-question-library-recovery-v3.json";
    private const string DiagnosticsName = "installed-question-library-recovery-v3-diagnostics.json";

    private static readonly string[] InspectedTables =
    [
        "projects",
        "quiz_questions",
        "quiz_history",
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
            var destination = InspectDatabase(Path.Combine(appDataRoot, "data", "factvault.db"));
            if (destination.QuestionRows > 0)
            {
                WriteDiagnostics(
                    Path.Combine(appDataRoot, DiagnosticsName),
                    "installed-question-library-present",
                    destination,
                    [],
                    null);
                return;
            }

            var candidates = CandidateDatabasePaths(appDataRoot).ToList();
            if (!ContainsQuestionSource(candidates))
                candidates.AddRange(DeepDiscoverDatabasePaths(appDataRoot));

            _ = Run(appDataRoot, candidates);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException or
            SqliteException or JsonException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Installed question-library recovery v3 could not complete: {error}");
        }
    }

    internal static InstalledQuestionLibraryRecoveryV3Result Run(
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

        // The reusable quiz Library is quiz_questions. Project rows belong to the
        // separate Projects workspace and must never be used as proof that the
        // question Library has already been recovered.
        if (destination.QuestionRows > 0)
        {
            WriteDiagnostics(
                diagnosticsPath,
                "installed-question-library-present",
                destination,
                [],
                null);
            return new InstalledQuestionLibraryRecoveryV3Result(
                Completed: true,
                DatabaseRecovered: false,
                SourceDatabase: "",
                SourceQuestions: 0,
                DestinationQuestions: destination.QuestionRows,
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
            .Where(snapshot => snapshot.QuestionRows > 0)
            .OrderByDescending(snapshot => snapshot.QuestionRows)
            .ThenByDescending(snapshot => snapshot.HistoryRows)
            .ThenByDescending(snapshot => snapshot.HistoryQuestionRows)
            .ThenByDescending(snapshot => snapshot.DatabaseBytes)
            .FirstOrDefault();

        if (source is null)
        {
            WriteDiagnostics(diagnosticsPath, "no-question-library-source-found", destination, snapshots, null);
            return new InstalledQuestionLibraryRecoveryV3Result(
                Completed: false,
                DatabaseRecovered: false,
                SourceDatabase: "",
                SourceQuestions: 0,
                DestinationQuestions: destination.QuestionRows,
                CandidatesInspected: snapshots.Count);
        }

        Directory.CreateDirectory(destinationData);
        var backup = BackupInstalledDatabase(destinationDatabase, appDataRoot);

        using var sourceConnection = OpenReadOnly(source.Path);
        using var destinationConnection = OpenReadWrite(destinationDatabase);
        using var transaction = destinationConnection.BeginTransaction();

        var destinationHistoryWasEmpty = CountRows(destinationConnection, "quiz_history", transaction) == 0;
        var destinationHistoryQuestionsWereEmpty = CountRows(destinationConnection, "quiz_history_questions", transaction) == 0;
        var destinationJournalWasEmpty = CountRows(destinationConnection, "social_upload_journal", transaction) == 0;

        CopyMissingTableRows(sourceConnection, destinationConnection, transaction, "quiz_questions");

        // History IDs are referenced by the history-question and upload-journal tables.
        // Only import the source history set when the installed history is empty, avoiding
        // ID collisions with any history the installed app may already have created.
        if (destinationHistoryWasEmpty)
        {
            CopyMissingTableRows(sourceConnection, destinationConnection, transaction, "quiz_history");
            if (destinationHistoryQuestionsWereEmpty)
                CopyMissingTableRows(sourceConnection, destinationConnection, transaction, "quiz_history_questions");
            if (destinationJournalWasEmpty)
                CopyMissingTableRows(sourceConnection, destinationConnection, transaction, "social_upload_journal");
        }

        var recoveredQuestionRows = CountRows(destinationConnection, "quiz_questions", transaction);
        if (recoveredQuestionRows < source.QuestionRows)
        {
            throw new InvalidDataException(
                $"Question-library recovery verification failed: source has {source.QuestionRows} questions but installed database has {recoveredQuestionRows}.");
        }

        transaction.Commit();
        SqliteConnection.ClearAllPools();

        CopySupplementalManagerFiles(
            Path.GetDirectoryName(source.Path) ?? "",
            destinationData);

        var recovered = InspectDatabase(destinationDatabase);
        if (recovered.QuestionRows < source.QuestionRows)
            throw new InvalidDataException("Recovered question Library contains fewer questions than its source database.");

        WriteCompletionMarker(markerPath, source, recovered, backup);
        WriteDiagnostics(diagnosticsPath, "question-library-recovered", recovered, snapshots, source);

        return new InstalledQuestionLibraryRecoveryV3Result(
            Completed: true,
            DatabaseRecovered: true,
            SourceDatabase: source.Path,
            SourceQuestions: source.QuestionRows,
            DestinationQuestions: recovered.QuestionRows,
            CandidatesInspected: snapshots.Count);
    }

    private static void CopyMissingTableRows(
        SqliteConnection source,
        SqliteConnection destination,
        SqliteTransaction transaction,
        string table)
    {
        if (!TableExists(source, table) || !TableExists(destination, table, transaction))
            return;

        var sourceColumns = TableColumns(source, table);
        var destinationColumns = TableColumns(destination, table, transaction);
        var columns = sourceColumns
            .Where(column => destinationColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (columns.Count == 0)
            return;

        var quotedColumns = string.Join(", ", columns.Select(QuoteIdentifier));
        using var read = source.CreateCommand();
        read.CommandText = $"SELECT {quotedColumns} FROM {QuoteIdentifier(table)}";
        using var reader = read.ExecuteReader();

        using var insert = destination.CreateCommand();
        insert.Transaction = transaction;
        var parameters = columns.Select((_, index) => $"$p{index}").ToArray();
        insert.CommandText =
            $"INSERT OR IGNORE INTO {QuoteIdentifier(table)} ({quotedColumns}) VALUES ({string.Join(", ", parameters)})";
        for (var index = 0; index < parameters.Length; index++)
            insert.Parameters.Add(new SqliteParameter(parameters[index], DBNull.Value));

        while (reader.Read())
        {
            for (var index = 0; index < columns.Count; index++)
                insert.Parameters[index].Value = reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index);
            insert.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<string> TableColumns(
        SqliteConnection connection,
        string table,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1));
        return columns;
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool ContainsQuestionSource(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (InspectDatabase(path).QuestionRows > 0)
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

        foreach (var root in CommonCheckoutRoots(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
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

        foreach (var backupName in new[]
                 {
                     "migration-backup",
                     "library-recovery-v2-backup",
                     "question-library-recovery-v3-backup",
                 })
        {
            var backupRoot = Path.Combine(appDataRoot, backupName);
            if (!Directory.Exists(backupRoot))
                continue;
            foreach (var path in SafeEnumerateDatabaseFiles(backupRoot, 5, 4_000))
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
                     "installed-library-recovery-v2.json",
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
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
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
        var seen = new HashSet<string>(
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
                if (seen.Add(full))
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

        if (OperatingSystem.IsWindows())
        {
            var profileRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "";
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { drives = []; }

            foreach (var drive in drives)
            {
                string root = "";
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        root = drive.RootDirectory.FullName;
                }
                catch { root = ""; }

                if (root.Length == 0 || string.Equals(root, profileRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddSearchRoot(roots, root, 6, 20_000);
            }
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return roots
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

    private static IEnumerable<string> SafeEnumerateDatabaseFiles(string root, int maxDepth, int maxDirectories)
    {
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < maxDirectories)
        {
            var (directory, depth) = queue.Dequeue();
            visited++;

            string[] files;
            try { files = Directory.GetFiles(directory); }
            catch { files = []; }

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
            try { children = Directory.GetDirectories(directory); }
            catch { children = []; }

            foreach (var child in children)
            {
                try
                {
                    var info = new DirectoryInfo(child);
                    if (!SkippedDirectoryNames.Contains(info.Name) &&
                        !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        queue.Enqueue((child, depth + 1));
                }
                catch
                {
                }
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
            using var connection = OpenReadOnly(full);
            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in InspectedTables)
                counts[table] = CountRows(connection, table);

            return new DatabaseSnapshot(
                full,
                true,
                new FileInfo(full).Length,
                counts["projects"],
                counts["quiz_questions"],
                counts["quiz_history"],
                counts["quiz_history_questions"],
                counts["social_upload_journal"],
                counts);
        }
        catch (Exception error) when (
            error is SqliteException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not inspect database '{full}': {error.Message}");
            return DatabaseSnapshot.Empty(full) with { Exists = true, DatabaseBytes = SafeFileLength(full) };
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenReadWrite(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long CountRows(SqliteConnection connection, string table, SqliteTransaction? transaction = null)
    {
        if (!TableExists(connection, table, transaction))
            return 0;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(table)}";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static bool TableExists(SqliteConnection connection, string table, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static string BackupInstalledDatabase(string destinationDatabase, string appDataRoot)
    {
        if (!File.Exists(destinationDatabase))
            return "";

        var backupRoot = Path.Combine(appDataRoot, "question-library-recovery-v3-backup");
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(
            backupRoot,
            $"factvault-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");

        using var source = OpenReadOnly(destinationDatabase);
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backup,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        return backup;
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
        DatabaseSnapshot destination,
        string backup)
    {
        WriteJson(markerPath, new JsonObject
        {
            ["version"] = 3,
            ["completed_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_database"] = source.Path,
            ["destination_database"] = destination.Path,
            ["backup_database"] = backup,
            ["source_questions"] = source.QuestionRows,
            ["destination_questions"] = destination.QuestionRows,
            ["destination_projects_preserved"] = destination.ProjectRows,
            ["destination_history"] = destination.HistoryRows,
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
                         .OrderByDescending(item => item.QuestionRows)
                         .ThenByDescending(item => item.HistoryRows)
                         .ThenByDescending(item => item.DatabaseBytes)
                         .Take(100))
                candidateArray.Add(SnapshotJson(candidate));

            WriteJson(path, new JsonObject
            {
                ["version"] = 3,
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
            Debug.WriteLine($"Could not write question-library recovery diagnostics: {error.Message}");
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
            ["projects"] = snapshot.ProjectRows,
            ["quiz_questions"] = snapshot.QuestionRows,
            ["quiz_history"] = snapshot.HistoryRows,
            ["quiz_history_questions"] = snapshot.HistoryQuestionRows,
            ["social_upload_journal"] = snapshot.UploadJournalRows,
            ["tables"] = counts,
        };
    }

    private static void WriteJson(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static string ReadString(JsonObject root, string name)
    {
        try { return root[name]?.GetValue<string>()?.Trim() ?? ""; }
        catch (InvalidOperationException) { return ""; }
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
        try { return Path.GetFullPath(path.Trim()); }
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
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private sealed record SearchRoot(string Root, int MaxDepth, int MaxDirectories);

    private sealed record DatabaseSnapshot(
        string Path,
        bool Exists,
        long DatabaseBytes,
        long ProjectRows,
        long QuestionRows,
        long HistoryRows,
        long HistoryQuestionRows,
        long UploadJournalRows,
        IReadOnlyDictionary<string, long> TableCounts)
    {
        public static DatabaseSnapshot Empty(string path) =>
            new(path, false, 0, 0, 0, 0, 0, 0, new Dictionary<string, long>());
    }
}
