using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed record QuizProjectDatabaseSnapshot(
    int HistoryId,
    string ProjectPath,
    string QuizJson,
    byte[] SourceArchive,
    string SourceArchiveSha256,
    long SourceBytes,
    long ArchiveBytes,
    int FileCount,
    string CapturedAt);

public sealed record QuizProjectRestoreResult(
    int HistoryId,
    string ProjectFolder,
    int RestoredFiles,
    int SkippedFiles,
    long RestoredBytes);

public sealed record QuizProjectProtectionSummary(
    int Protected,
    int AlreadyProtected,
    int Unavailable,
    long ArchiveBytes);

public sealed record QuizProjectRecoverySummary(
    int ProjectsChecked,
    int ProjectsRestored,
    int FilesRestored,
    long RestoredBytes);

public sealed partial class DesktopDataService
{
    private void EnsureQuizProjectSnapshotSchema()
    {
        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS quiz_project_snapshots (
                history_id INTEGER PRIMARY KEY,
                project_path TEXT NOT NULL,
                quiz_json TEXT NOT NULL,
                source_archive BLOB NOT NULL,
                source_archive_sha256 TEXT NOT NULL,
                source_bytes INTEGER NOT NULL DEFAULT 0,
                archive_bytes INTEGER NOT NULL DEFAULT 0,
                file_count INTEGER NOT NULL DEFAULT 0,
                archive_version INTEGER NOT NULL DEFAULT 1,
                captured_at TEXT NOT NULL,
                FOREIGN KEY(history_id) REFERENCES quiz_history(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_quiz_project_snapshots_project_path
            ON quiz_project_snapshots(project_path);
            """;
        command.ExecuteNonQuery();
    }

    internal void StoreQuizProjectSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int historyId,
        string projectFolder,
        QuizProjectArchiveCapture capture)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(capture);
        if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO quiz_project_snapshots(
                history_id, project_path, quiz_json, source_archive, source_archive_sha256,
                source_bytes, archive_bytes, file_count, archive_version, captured_at)
            VALUES(
                $historyId, $projectPath, $quizJson, $sourceArchive, $sha256,
                $sourceBytes, $archiveBytes, $fileCount, 1, $capturedAt)
            ON CONFLICT(history_id) DO UPDATE SET
                project_path = excluded.project_path,
                quiz_json = excluded.quiz_json,
                source_archive = excluded.source_archive,
                source_archive_sha256 = excluded.source_archive_sha256,
                source_bytes = excluded.source_bytes,
                archive_bytes = excluded.archive_bytes,
                file_count = excluded.file_count,
                archive_version = excluded.archive_version,
                captured_at = excluded.captured_at
            """;
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$projectPath", Path.GetFullPath(projectFolder));
        command.Parameters.AddWithValue("$quizJson", capture.QuizJson);
        command.Parameters.Add("$sourceArchive", SqliteType.Blob).Value = capture.Archive;
        command.Parameters.AddWithValue("$sha256", capture.Sha256);
        command.Parameters.AddWithValue("$sourceBytes", capture.SourceBytes);
        command.Parameters.AddWithValue("$archiveBytes", capture.ArchiveBytes);
        command.Parameters.AddWithValue("$fileCount", capture.FileCount);
        command.Parameters.AddWithValue("$capturedAt", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public bool HasQuizProjectSnapshot(int historyId)
    {
        if (historyId <= 0) return false;
        EnsureQuizProjectSnapshotSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM quiz_project_snapshots WHERE history_id = $historyId LIMIT 1";
        command.Parameters.AddWithValue("$historyId", historyId);
        return command.ExecuteScalar() is not null;
    }

    public QuizProjectDatabaseSnapshot? GetQuizProjectSnapshot(int historyId)
    {
        if (historyId <= 0) throw new ArgumentOutOfRangeException(nameof(historyId));
        EnsureQuizProjectSnapshotSchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT history_id, project_path, quiz_json, source_archive, source_archive_sha256,
                   source_bytes, archive_bytes, file_count, captured_at
            FROM quiz_project_snapshots
            WHERE history_id = $historyId
            """;
        command.Parameters.AddWithValue("$historyId", historyId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadQuizProjectSnapshot(reader);
    }

    public string EnsureQuizManifestFromDatabase(int historyId, string projectFolder)
    {
        var path = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
        if (File.Exists(path)) return path;

        var snapshot = GetQuizProjectSnapshot(historyId)
            ?? throw new FileNotFoundException("quiz.json is missing and no database recovery snapshot exists for this quiz.", path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".restore-{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, snapshot.QuizJson, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public QuizProjectRestoreResult RestoreQuizProjectFiles(
        int historyId,
        string? destinationFolder = null,
        bool overwriteExisting = false)
    {
        var snapshot = GetQuizProjectSnapshot(historyId)
            ?? throw new InvalidOperationException($"Quiz History #{historyId} does not have a database recovery snapshot.");
        var destination = string.IsNullOrWhiteSpace(destinationFolder)
            ? snapshot.ProjectPath
            : Path.GetFullPath(destinationFolder);

        var restored = QuizProjectDatabaseArchive.Restore(
            snapshot.SourceArchive,
            snapshot.SourceArchiveSha256,
            destination,
            overwriteExisting);
        return new QuizProjectRestoreResult(
            historyId,
            destination,
            restored.RestoredFiles,
            restored.SkippedFiles,
            restored.RestoredBytes);
    }

    public bool TryRestoreMissingQuizProjectFiles(QuizHistorySummary history, out QuizProjectRestoreResult? result)
    {
        ArgumentNullException.ThrowIfNull(history);
        result = null;
        if (history.Id <= 0 || !HasQuizProjectSnapshot(history.Id))
            return false;

        var snapshot = GetQuizProjectSnapshot(history.Id)!;
        var destination = history.ProjectFolder.Trim().Length > 0
            ? history.ProjectFolder
            : snapshot.ProjectPath;
        result = RestoreQuizProjectFiles(history.Id, destination, overwriteExisting: false);
        return result.RestoredFiles > 0;
    }

    public QuizProjectProtectionSummary ProtectExistingQuizProjects(int limit = 2_000)
    {
        EnsureQuizProjectSnapshotSchema();
        var histories = GetQuizHistory(Math.Clamp(limit, 1, 2_000));
        var protectedCount = 0;
        var alreadyProtected = 0;
        var unavailable = 0;
        long archiveBytes = 0;

        foreach (var history in histories)
        {
            if (HasQuizProjectSnapshot(history.Id))
            {
                alreadyProtected++;
                continue;
            }

            var capture = QuizProjectDatabaseArchive.TryCapture(history.ProjectFolder);
            if (capture is null)
            {
                unavailable++;
                continue;
            }

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            StoreQuizProjectSnapshot(connection, transaction, history.Id, history.ProjectFolder, capture);
            transaction.Commit();
            protectedCount++;
            archiveBytes += capture.ArchiveBytes;
        }

        return new QuizProjectProtectionSummary(protectedCount, alreadyProtected, unavailable, archiveBytes);
    }

    public QuizProjectRecoverySummary RestoreMissingQuizProjectFiles(int limit = 2_000)
    {
        EnsureQuizProjectSnapshotSchema();
        var histories = GetQuizHistory(Math.Clamp(limit, 1, 2_000));
        var checkedCount = 0;
        var projectsRestored = 0;
        var filesRestored = 0;
        long restoredBytes = 0;

        foreach (var history in histories)
        {
            if (!HasQuizProjectSnapshot(history.Id))
                continue;
            checkedCount++;
            var snapshot = GetQuizProjectSnapshot(history.Id)!;
            var destination = history.ProjectFolder.Trim().Length > 0
                ? history.ProjectFolder
                : snapshot.ProjectPath;
            var result = RestoreQuizProjectFiles(history.Id, destination, overwriteExisting: false);
            if (result.RestoredFiles <= 0)
                continue;
            projectsRestored++;
            filesRestored += result.RestoredFiles;
            restoredBytes += result.RestoredBytes;
        }

        return new QuizProjectRecoverySummary(checkedCount, projectsRestored, filesRestored, restoredBytes);
    }

    private static QuizProjectDatabaseSnapshot ReadQuizProjectSnapshot(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetFieldValue<byte[]>(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt64(6),
        reader.GetInt32(7),
        reader.GetString(8));
}
