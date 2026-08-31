namespace FactVaultManager.Desktop;

public sealed record QuizHistoryPathRecoveryResult(
    int Updated,
    int AlreadyCorrect,
    int Unresolved,
    IReadOnlyList<int> UnresolvedHistoryIds);

public sealed partial class DesktopDataService
{
    private const int WebsiteQuizHistoryRecoveryLimit = 2_000;

    public QuizHistoryPathRecoveryResult RecoverQuizHistoryProjectFolders()
    {
        var histories = GetQuizHistory(WebsiteQuizHistoryRecoveryLimit);
        if (histories.Count == 0)
            return new QuizHistoryPathRecoveryResult(0, 0, 0, []);

        HashSet<int> archiveRecoveredIds;
        try
        {
            archiveRecoveredIds = RecoverJournaledArchiveLinksForProjectRecovery(histories);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A temporarily unavailable archive must not prevent the normal local-project recovery pass.
            archiveRecoveredIds = [];
        }

        // Journal restoration can update paths in SQLite, so always take a fresh snapshot before the
        // general path finder runs. This keeps the Website audit and project recovery on the same scope.
        histories = GetQuizHistory(WebsiteQuizHistoryRecoveryLimit);

        // Existing valid one-to-one paths are authoritative. Shared existing paths are deliberately
        // left unresolved for the duplicate-path repair workflow instead of being silently reused.
        var duplicateExistingIds = histories
            .Where(history => !string.IsNullOrWhiteSpace(history.ProjectFolder) && Directory.Exists(history.ProjectFolder))
            .GroupBy(history => NormalizeRecoveryPath(history.ProjectFolder), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .SelectMany(group => group.Select(history => history.Id))
            .ToHashSet();

        var resolvedIds = new HashSet<int>();
        var reservedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alreadyCorrect = 0;
        foreach (var history in histories)
        {
            if (duplicateExistingIds.Contains(history.Id) ||
                string.IsNullOrWhiteSpace(history.ProjectFolder) ||
                !Directory.Exists(history.ProjectFolder))
            {
                continue;
            }

            resolvedIds.Add(history.Id);
            if (!archiveRecoveredIds.Contains(history.Id))
                alreadyCorrect++;
            var normalized = NormalizeRecoveryPath(history.ProjectFolder);
            if (normalized.Length > 0)
                reservedFolders.Add(normalized);
        }

        var needsRecovery = histories
            .Where(history => !resolvedIds.Contains(history.Id) && !duplicateExistingIds.Contains(history.Id))
            .ToList();
        var matches = SocialUploadQueuePathFinder.FindMissingVideos(needsRecovery, GetProjectsRoot());

        // The legacy finder scores each History row independently. Never allow two rows to claim the
        // same discovered project folder, even when their broad titles/categories are similar.
        var uniqueMatches = matches
            .Select(match => new
            {
                Match = match,
                Folder = NormalizeRecoveryPath(Path.GetDirectoryName(match.VideoPath) ?? ""),
            })
            .Where(item => item.Folder.Length > 0 && !reservedFolders.Contains(item.Folder))
            .GroupBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToList();

        var historiesById = histories.ToDictionary(history => history.Id);
        var updated = archiveRecoveredIds.Count;
        foreach (var item in uniqueMatches)
        {
            if (!historiesById.TryGetValue(item.Match.HistoryId, out var history) ||
                resolvedIds.Contains(history.Id) ||
                duplicateExistingIds.Contains(history.Id))
            {
                continue;
            }

            if (UpdateQuizHistoryProjectFolder(history.Id, item.Folder))
            {
                updated++;
                resolvedIds.Add(history.Id);
                reservedFolders.Add(item.Folder);
            }
        }

        var unresolvedIds = histories
            .Where(history => !resolvedIds.Contains(history.Id))
            .Select(history => history.Id)
            .ToList();
        return new QuizHistoryPathRecoveryResult(
            updated,
            alreadyCorrect,
            unresolvedIds.Count,
            unresolvedIds);
    }

    private HashSet<int> RecoverJournaledArchiveLinksForProjectRecovery(IReadOnlyList<QuizHistorySummary> histories)
    {
        var repairedIds = new HashSet<int>();
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            return repairedIds;

        string quizRoot;
        try
        {
            quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return repairedIds;
        }

        if (!Directory.Exists(quizRoot))
            return repairedIds;

        var historyById = histories.ToDictionary(history => history.Id);
        var owners = BuildArchiveOwners(histories, quizRoot);
        var primaryDatabase = Path.GetFullPath(_databasePath);

        foreach (var entry in LoadQuizArchiveRelinkJournal().OrderBy(entry => entry.HistoryId))
        {
            if (!historyById.TryGetValue(entry.HistoryId, out var history))
                continue;

            var current = (history.ProjectFolder ?? "").Trim();
            if (SameStoredPath(current, entry.ArchiveFolder))
                continue;

            // Use the same narrow safety rule as the archive reconciliation workflow: the exact
            // History ID must still point at the exact folder it was archived from, and the journal
            // must belong to this same SQLite database.
            if (!SameStoredPath(current, entry.PreviousFolder) ||
                !SameStoredPath(entry.DatabasePath, primaryDatabase))
            {
                continue;
            }

            string destination;
            try
            {
                destination = Path.GetFullPath(entry.ArchiveFolder);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!Directory.Exists(destination) ||
                !IsPathWithin(quizRoot, destination) ||
                !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, destination), destination, StringComparison.OrdinalIgnoreCase) ||
                ArchiveFolderOwnedByAnotherHistory(destination, history.Id, owners))
            {
                continue;
            }

            if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
                continue;

            try
            {
                RequirePersistedQuizHistoryPath(history.Id, destination);
                repairedIds.Add(history.Id);
                owners[destination] = new HashSet<int> { history.Id };
                historyById[history.Id] = history with { ProjectFolder = destination };
            }
            catch (InvalidOperationException)
            {
                // Leave the row for the fresh snapshot/general recovery pass rather than claiming it repaired.
            }
        }

        return repairedIds;
    }

    private static string NormalizeRecoveryPath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path.Trim());
        }
        catch (ArgumentException)
        {
            return "";
        }
        catch (NotSupportedException)
        {
            return "";
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim()),
                Path.GetFullPath(right.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
