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

public sealed record QuizArchiveAmbiguityDiagnostic(
    string ArchiveFolder,
    int SuggestedHistoryId,
    string SuggestedLabel,
    int Score,
    IReadOnlyList<string> CompetingHistories,
    IReadOnlyList<string> CompetingArchiveFolders);

public sealed record QuizArchiveAuditDiagnostics(
    QuizArchiveDatabaseDiagnostic Database,
    IReadOnlyList<QuizArchiveAmbiguityDiagnostic> Ambiguities);

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

    public QuizArchiveAuditDiagnostics BuildQuizArchiveAuditDiagnostics()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        if (!Directory.Exists(quizRoot))
            throw new DirectoryNotFoundException($"The quiz archive folder was not found: {quizRoot}");

        var histories = GetQuizHistory();
        var existingArchiveHistories = histories
            .Where(history =>
            {
                var path = (history.ProjectFolder ?? "").Trim();
                return path.Length > 0 && Directory.Exists(path) && IsPathWithin(quizRoot, path);
            })
            .ToList();
        var database = BuildQuizArchiveDatabaseDiagnostic(quizRoot, histories, existingArchiveHistories.Count);

        var linkedArchiveFolders = existingArchiveHistories
            .Select(history => ResolveTopLevelArchiveFolder(quizRoot, history.ProjectFolder))
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlinkedArchiveFolders = Directory.EnumerateDirectories(quizRoot)
            .Select(Path.GetFullPath)
            .Where(folder => !linkedArchiveFolders.Contains(folder))
            .OrderBy(folder => Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var auditableHistories = histories
            .Where(history =>
            {
                var path = (history.ProjectFolder ?? "").Trim();
                return path.Length == 0 || !Directory.Exists(path) || !IsPathWithin(quizRoot, path);
            })
            .ToList();

        var currentFingerprints = new Dictionary<int, QuizArchiveFolderFingerprint>();
        foreach (var history in auditableHistories)
        {
            var path = (history.ProjectFolder ?? "").Trim();
            if (path.Length == 0 || !Directory.Exists(path) || IsPathWithin(quizRoot, path))
                continue;

            var fingerprint = TryInspectFolder(path);
            if (fingerprint is not null)
                currentFingerprints[history.Id] = fingerprint;
        }

        var archiveFingerprints = new Dictionary<string, QuizArchiveFolderFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in unlinkedArchiveFolders)
        {
            var fingerprint = TryInspectFolder(folder);
            if (fingerprint is not null)
                archiveFingerprints[folder] = fingerprint;
        }

        var candidatesByFolder = new Dictionary<string, List<QuizArchiveDeepCandidate>>(StringComparer.OrdinalIgnoreCase);
        var candidatesByHistory = new Dictionary<int, List<QuizArchiveDeepCandidate>>();
        foreach (var folder in unlinkedArchiveFolders)
        {
            var candidates = new List<QuizArchiveDeepCandidate>();
            if (archiveFingerprints.TryGetValue(folder, out var archiveFingerprint))
            {
                foreach (var history in auditableHistories)
                {
                    currentFingerprints.TryGetValue(history.Id, out var currentFingerprint);
                    var candidate = QuizArchiveDeepMatcher.Evaluate(history, archiveFingerprint, currentFingerprint);
                    if (candidate.Confidence == QuizArchiveMatchConfidence.NoMatch)
                        continue;

                    candidates.Add(candidate);
                    if (!candidatesByHistory.TryGetValue(history.Id, out var historyCandidates))
                        candidatesByHistory[history.Id] = historyCandidates = new List<QuizArchiveDeepCandidate>();
                    historyCandidates.Add(candidate);
                }
            }

            candidatesByFolder[folder] = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.HistoryId)
                .ToList();
        }

        var ambiguities = new List<QuizArchiveAmbiguityDiagnostic>();
        foreach (var folder in unlinkedArchiveFolders)
        {
            var candidates = candidatesByFolder[folder];
            if (candidates.Count == 0)
                continue;

            var best = candidates[0];
            var competingHistories = candidates
                .Skip(1)
                .Where(candidate => best.Score - candidate.Score < ArchiveUniquenessMargin)
                .Select(candidate =>
                    $"History #{candidate.HistoryId}: {candidate.Label} (score {candidate.Score}, current '{AuditPath(candidate.CurrentFolder)}')")
                .ToList();

            var competingArchiveFolders = candidatesByHistory.TryGetValue(best.HistoryId, out var sameHistoryCandidates)
                ? sameHistoryCandidates
                    .Where(candidate =>
                        !string.Equals(candidate.ArchiveFolder, folder, StringComparison.OrdinalIgnoreCase) &&
                        best.Score - candidate.Score < ArchiveUniquenessMargin)
                    .OrderByDescending(candidate => candidate.Score)
                    .Select(candidate => $"{candidate.ArchiveFolder} (score {candidate.Score})")
                    .ToList()
                : new List<string>();

            if (competingHistories.Count == 0 && competingArchiveFolders.Count == 0)
                continue;

            ambiguities.Add(new QuizArchiveAmbiguityDiagnostic(
                folder,
                best.HistoryId,
                best.Label,
                best.Score,
                competingHistories,
                competingArchiveFolders));
        }

        return new QuizArchiveAuditDiagnostics(
            database,
            ambiguities.OrderBy(item => Path.GetFileName(item.ArchiveFolder), StringComparer.OrdinalIgnoreCase).ToList());
    }

    public void RecordSuccessfulQuizArchiveRelinks(IReadOnlyList<QuizArchiveRelinkRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            return;

        var histories = GetQuizHistory().ToDictionary(history => history.Id);
        foreach (var request in requests)
        {
            if (!histories.TryGetValue(request.HistoryId, out var history))
                continue;

            var current = (history.ProjectFolder ?? "").Trim();
            if (!SameStoredPath(current, request.ArchiveFolder))
                continue;

            RecordQuizArchiveRelinkSuccess(
                request.HistoryId,
                request.ExpectedCurrentFolder,
                request.ArchiveFolder);
        }
    }

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
                   new List<QuizArchiveRelinkJournalEntry>();
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

    private static string AuditPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "(missing / none)" : path;
}
