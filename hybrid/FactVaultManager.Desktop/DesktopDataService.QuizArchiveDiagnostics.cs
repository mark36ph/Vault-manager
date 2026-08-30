using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record QuizArchiveDatabaseDiagnostic(
    string DatabasePath,
    string RuntimeRoot,
    string DataRoot,
    long DatabaseBytes,
    string DatabaseModified,
    int StoredArchivePaths,
    int ExistingArchivePaths,
    int MissingArchivePaths,
    IReadOnlyList<string> DatabaseCandidates,
    IReadOnlyList<string> PersistenceWarnings);

public sealed record QuizArchiveRelinkJournalEntry(
    int HistoryId,
    string PreviousFolder,
    string ArchiveFolder,
    string DatabasePath,
    string RelinkedUtc);

public sealed partial class DesktopDataService
{
    private string QuizArchiveRelinkJournalPath =>
        Path.Combine(_dataRoot, "data", "quiz-archive-relinks.json");

    private QuizArchiveDatabaseDiagnostic BuildQuizArchiveDatabaseDiagnostic(
        string quizRoot,
        IReadOnlyList<QuizHistorySummary> histories,
        int existingArchivePaths)
    {
        var storedArchivePaths = histories.Count(history =>
        {
            var path = (history.ProjectFolder ?? "").Trim();
            return path.Length > 0 && IsPathWithin(quizRoot, path);
        });

        var primary = Path.GetFullPath(_databasePath);
        var candidates = new[]
        {
            primary,
            Path.Combine(_runtimeRoot, "data", "factvault.db"),
            Path.Combine(AppContext.BaseDirectory, "data", "factvault.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "factvault.db"),
        }
        .Select(path =>
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => DescribeDatabaseCandidate(path, string.Equals(path, primary, StringComparison.OrdinalIgnoreCase)))
        .ToList();

        long bytes = 0;
        string modified = "(missing)";
        try
        {
            if (File.Exists(primary))
            {
                var info = new FileInfo(primary);
                bytes = info.Length;
                modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            modified = "(unavailable: " + error.Message + ")";
        }

        var historyById = histories.ToDictionary(history => history.Id);
        var warnings = new List<string>();
        foreach (var entry in LoadQuizArchiveRelinkJournal())
        {
            if (!historyById.TryGetValue(entry.HistoryId, out var history))
            {
                warnings.Add($"History #{entry.HistoryId}: relink journal entry exists but Quiz History row is missing");
                continue;
            }

            var current = (history.ProjectFolder ?? "").Trim();
            if (!SameStoredPath(current, entry.ArchiveFolder))
            {
                warnings.Add(
                    $"History #{entry.HistoryId}: journal says relinked to '{entry.ArchiveFolder}' at {entry.RelinkedUtc}, " +
                    $"but the current database stores '{(current.Length == 0 ? "(blank)" : current)}'");
            }

            if (!SameStoredPath(entry.DatabasePath, primary))
            {
                warnings.Add(
                    $"History #{entry.HistoryId}: relink journal used database '{entry.DatabasePath}', " +
                    $"but this run is using '{primary}'");
            }
        }

        return new QuizArchiveDatabaseDiagnostic(
            primary,
            _runtimeRoot,
            _dataRoot,
            bytes,
            modified,
            storedArchivePaths,
            existingArchivePaths,
            Math.Max(0, storedArchivePaths - existingArchivePaths),
            candidates,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    private void RecordQuizArchiveRelinkSuccess(
        int historyId,
        string previousFolder,
        string archiveFolder)
    {
        try
        {
            var entries = LoadQuizArchiveRelinkJournal().ToList();
            entries.RemoveAll(entry => entry.HistoryId == historyId);
            entries.Add(new QuizArchiveRelinkJournalEntry(
                historyId,
                previousFolder,
                archiveFolder,
                Path.GetFullPath(_databasePath),
                DateTime.UtcNow.ToString("O")));

            Directory.CreateDirectory(Path.GetDirectoryName(QuizArchiveRelinkJournalPath)!);
            var temporary = QuizArchiveRelinkJournalPath + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(entries.OrderBy(entry => entry.HistoryId), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, QuizArchiveRelinkJournalPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not write quiz archive relink journal: {error.Message}");
        }
    }

    private IReadOnlyList<QuizArchiveRelinkJournalEntry> LoadQuizArchiveRelinkJournal()
    {
        try
        {
            if (!File.Exists(QuizArchiveRelinkJournalPath))
                return Array.Empty<QuizArchiveRelinkJournalEntry>();

            return JsonSerializer.Deserialize<List<QuizArchiveRelinkJournalEntry>>(
                       File.ReadAllText(QuizArchiveRelinkJournalPath)) ??
                   Array.Empty<QuizArchiveRelinkJournalEntry>();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz archive relink journal: {error.Message}");
            return Array.Empty<QuizArchiveRelinkJournalEntry>();
        }
    }

    private static string DescribeDatabaseCandidate(string path, bool primary)
    {
        var prefix = primary ? "PRIMARY" : "alternate";
        try
        {
            if (!File.Exists(path))
                return $"{prefix}: {path} | missing";

            var info = new FileInfo(path);
            return $"{prefix}: {path} | {info.Length:N0} bytes | modified {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"{prefix}: {path} | unavailable: {error.Message}";
        }
    }
}
