namespace FactVaultManager.Desktop;

public enum QuizCompletedCArchiveAction
{
    RestoreJournaledArchiveLink,
    ReuseVerifiedArchiveCopy,
    CopyToArchive,
}

public sealed record QuizCompletedCArchiveItem(
    int HistoryId,
    string Label,
    string SourceFolder,
    string DestinationHint,
    QuizCompletedCArchiveAction Action);

public sealed record QuizCompletedCArchiveSkipped(
    int HistoryId,
    string Label,
    string SourceFolder,
    string Reason);

public sealed record QuizCompletedCArchivePreview(
    int ExistingCProjects,
    int ReadyProjects,
    int RestoreArchivedLinks,
    int ReuseVerifiedCopies,
    int CopyNewProjects,
    int BlockedByUploads,
    int SafetySkipped,
    IReadOnlyList<QuizCompletedCArchiveItem> ReadyItems,
    IReadOnlyList<QuizCompletedCArchiveSkipped> SkippedItems);

public sealed record QuizCompletedCArchiveProgress(
    int Current,
    int Total,
    int HistoryId,
    string Label,
    string Stage,
    string SourceFolder,
    string DestinationFolder,
    bool ItemCompleted = false);

public sealed record QuizCompletedCArchiveItemResult(
    int HistoryId,
    string Label,
    string SourceFolder,
    string DestinationFolder,
    QuizCompletedCArchiveAction Action,
    bool SourceDeleted,
    bool Succeeded,
    string Message);

public sealed record QuizCompletedCArchiveApplyResult(
    int ReadyAtStart,
    int Succeeded,
    int RestoredArchivedLinks,
    int ReusedExistingCopies,
    int CopiedNewProjects,
    int SourceFoldersRemoved,
    int CleanupWarnings,
    int Failed,
    int JournalLinksReconciled,
    IReadOnlyList<QuizCompletedCArchiveItemResult> Results);

public sealed record QuizArchiveJournalReconciliationResult(
    int Repaired,
    int Skipped,
    IReadOnlyList<string> Details);

public sealed partial class DesktopDataService
{
    private sealed record BulkArchiveFolderSnapshot(
        string Folder,
        IReadOnlyDictionary<string, long> Files);

    public QuizArchiveJournalReconciliationResult ReconcileJournaledQuizArchivePaths()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            return new QuizArchiveJournalReconciliationResult(0, 0, Array.Empty<string>());

        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        if (!Directory.Exists(quizRoot))
            return new QuizArchiveJournalReconciliationResult(0, 0, Array.Empty<string>());

        var histories = GetQuizHistory();
        var historyById = histories.ToDictionary(history => history.Id);
        var owners = BuildArchiveOwners(histories, quizRoot);
        var primaryDatabase = Path.GetFullPath(_databasePath);
        var repaired = 0;
        var skipped = 0;
        var details = new List<string>();

        foreach (var entry in LoadQuizArchiveRelinkJournal().OrderBy(entry => entry.HistoryId))
        {
            if (!historyById.TryGetValue(entry.HistoryId, out var history))
                continue;

            var current = (history.ProjectFolder ?? "").Trim();
            if (SameStoredPath(current, entry.ArchiveFolder))
                continue;

            // A journal repair is intentionally narrow: the same History ID must have fallen back
            // to the exact path from which that History ID was previously archived, and the journal
            // must have been written by the same SQLite database used by this run.
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
                skipped++;
                details.Add($"History #{entry.HistoryId}: journal destination is invalid ({error.Message})");
                continue;
            }

            if (!Directory.Exists(destination) ||
                !IsPathWithin(quizRoot, destination) ||
                !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, destination), destination, StringComparison.OrdinalIgnoreCase) ||
                ArchiveFolderOwnedByAnotherHistory(destination, history.Id, owners))
            {
                skipped++;
                details.Add($"History #{entry.HistoryId}: journaled Z: destination could not be safely restored");
                continue;
            }

            if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
            {
                skipped++;
                details.Add($"History #{entry.HistoryId}: Quiz History database update was rejected");
                continue;
            }

            try
            {
                RequirePersistedQuizHistoryPath(history.Id, destination);
                repaired++;
                owners[destination] = new HashSet<int> { history.Id };
                historyById[history.Id] = history with { ProjectFolder = destination };
                details.Add($"History #{entry.HistoryId}: restored to {destination}");
            }
            catch (InvalidOperationException error)
            {
                skipped++;
                details.Add($"History #{entry.HistoryId}: {error.Message}");
            }
        }

        return new QuizArchiveJournalReconciliationResult(repaired, skipped, details);
    }

    public QuizCompletedCArchivePreview PreviewCompletedCQuizProjects()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var archiveRoot = Path.GetFullPath(settings.NasArchiveFolder.Trim());
        var quizRoot = Path.Combine(archiveRoot, "Quizzes");
        if (!Directory.Exists(archiveRoot))
            throw new DirectoryNotFoundException($"The configured archive drive/folder was not found: {archiveRoot}");
        Directory.CreateDirectory(quizRoot);

        var histories = GetQuizHistory();
        var cProjects = histories
            .Where(history => IsExistingCDriveFolder(history.ProjectFolder))
            .ToList();

        var sourceGroups = cProjects
            .GroupBy(history => NormalizeBulkArchivePath(history.ProjectFolder), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(history => history.Id).ToList(),
                StringComparer.OrdinalIgnoreCase);

        // The reuse rule requires the same project-folder name. Index only folder names up front;
        // do not recursively fingerprint every Z: project on every report run.
        var archiveFoldersByName = Directory.EnumerateDirectories(quizRoot)
            .Select(Path.GetFullPath)
            .GroupBy(ArchiveFolderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var archiveOwners = BuildArchiveOwners(histories, quizRoot);
        var archiveSnapshotCache = new Dictionary<string, BulkArchiveFolderSnapshot?>(StringComparer.OrdinalIgnoreCase);
        var journalByHistory = LoadQuizArchiveRelinkJournal()
            .GroupBy(entry => entry.HistoryId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(entry => entry.RelinkedUtc, StringComparer.Ordinal).First());
        var primaryDatabase = Path.GetFullPath(_databasePath);

        BulkArchiveFolderSnapshot? ArchiveSnapshot(string folder)
        {
            if (archiveSnapshotCache.TryGetValue(folder, out var cached))
                return cached;
            var snapshot = TryBuildBulkArchiveSnapshot(folder);
            archiveSnapshotCache[folder] = snapshot;
            return snapshot;
        }

        var ready = new List<QuizCompletedCArchiveItem>();
        var skipped = new List<QuizCompletedCArchiveSkipped>();
        var blockedByUploads = 0;
        var safetySkipped = 0;

        foreach (var history in cProjects.OrderBy(history => history.Id))
        {
            var source = Path.GetFullPath(history.ProjectFolder.Trim());
            var label = HistoryLabel(history);

            if (TryGetJournalRestoreDestination(
                    history,
                    source,
                    quizRoot,
                    primaryDatabase,
                    archiveOwners,
                    journalByHistory,
                    out var journalDestination))
            {
                ready.Add(new QuizCompletedCArchiveItem(
                    history.Id,
                    label,
                    source,
                    journalDestination,
                    QuizCompletedCArchiveAction.RestoreJournaledArchiveLink));
                continue;
            }

            if (sourceGroups.TryGetValue(source, out var sameSourceHistories) && sameSourceHistories.Count > 1)
            {
                safetySkipped++;
                var ids = string.Join(", ", sameSourceHistories.Select(item => $"#{item.Id}"));
                skipped.Add(new QuizCompletedCArchiveSkipped(
                    history.Id,
                    label,
                    source,
                    $"This C: folder is referenced by Quiz History rows {ids}. No folder will be deleted until the duplicate links are reconciled."));
                continue;
            }

            var remaining = SocialUploadQueuePlanner.RemainingDestinations(history);
            if (remaining != SocialUploadDestination.None)
            {
                blockedByUploads++;
                skipped.Add(new QuizCompletedCArchiveSkipped(
                    history.Id,
                    label,
                    source,
                    $"Required uploads are still outstanding: {remaining}."));
                continue;
            }

            var sourceSnapshot = TryBuildBulkArchiveSnapshot(source);
            if (sourceSnapshot is null || sourceSnapshot.Files.Count == 0)
            {
                safetySkipped++;
                skipped.Add(new QuizCompletedCArchiveSkipped(
                    history.Id,
                    label,
                    source,
                    "The project folder could not be read safely or contains no files."));
                continue;
            }

            var sourceName = ArchiveFolderName(source);
            var reusableMatches = new List<BulkArchiveFolderSnapshot>();
            if (archiveFoldersByName.TryGetValue(sourceName, out var sameNameArchiveFolders))
            {
                foreach (var archiveFolder in sameNameArchiveFolders)
                {
                    if (ArchiveFolderOwnedByAnotherHistory(archiveFolder, history.Id, archiveOwners))
                        continue;
                    var snapshot = ArchiveSnapshot(archiveFolder);
                    if (snapshot is not null && BulkSnapshotsStrongCopyIdentity(sourceSnapshot, snapshot))
                        reusableMatches.Add(snapshot);
                }
            }

            if (reusableMatches.Count == 1)
            {
                ready.Add(new QuizCompletedCArchiveItem(
                    history.Id,
                    label,
                    source,
                    reusableMatches[0].Folder,
                    QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy));
            }
            else
            {
                ready.Add(new QuizCompletedCArchiveItem(
                    history.Id,
                    label,
                    source,
                    Path.Combine(quizRoot, sourceName),
                    QuizCompletedCArchiveAction.CopyToArchive));
            }
        }

        return new QuizCompletedCArchivePreview(
            cProjects.Count,
            ready.Count,
            ready.Count(item => item.Action == QuizCompletedCArchiveAction.RestoreJournaledArchiveLink),
            ready.Count(item => item.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy),
            ready.Count(item => item.Action == QuizCompletedCArchiveAction.CopyToArchive),
            blockedByUploads,
            safetySkipped,
            ready,
            skipped);
    }

    public QuizCompletedCArchiveApplyResult ArchiveCompletedCQuizProjects() =>
        ArchiveCompletedCQuizProjects(PreviewCompletedCQuizProjects(), progress: null);

    public QuizCompletedCArchiveApplyResult ArchiveCompletedCQuizProjects(
        QuizCompletedCArchivePreview confirmedPreview,
        IProgress<QuizCompletedCArchiveProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(confirmedPreview);
        var results = new List<QuizCompletedCArchiveItemResult>();
        var total = confirmedPreview.ReadyItems.Count;

        for (var index = 0; index < confirmedPreview.ReadyItems.Count; index++)
        {
            var item = confirmedPreview.ReadyItems[index];
            var currentNumber = index + 1;
            ReportProgress(progress, currentNumber, total, item, "Preparing and rechecking", item.DestinationHint);

            try
            {
                var history = GetQuizHistory().FirstOrDefault(candidate => candidate.Id == item.HistoryId);
                if (history is null)
                    throw new InvalidOperationException("The Quiz History row no longer exists.");
                if (!SameStoredPath(history.ProjectFolder, item.SourceFolder))
                    throw new InvalidOperationException("The stored project path changed after the preview, so this quiz was skipped.");

                QuizCompletedCArchiveItemResult result;
                if (item.Action == QuizCompletedCArchiveAction.RestoreJournaledArchiveLink)
                {
                    result = RestoreJournaledArchiveLink(history, item, currentNumber, total, progress);
                }
                else
                {
                    if (!IsExistingCDriveFolder(history.ProjectFolder))
                        throw new InvalidOperationException("The C: project folder is no longer available.");

                    var remaining = SocialUploadQueuePlanner.RemainingDestinations(history);
                    if (remaining != SocialUploadDestination.None)
                        throw new InvalidOperationException($"Required uploads are now outstanding: {remaining}.");

                    result = item.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy
                        ? FinalizeVerifiedExistingArchiveCopy(history, item, currentNumber, total, progress)
                        : CopyCompletedProjectToArchive(history, item, currentNumber, total, progress);
                }

                results.Add(result);
                ReportProgress(
                    progress,
                    currentNumber,
                    total,
                    item,
                    result.Succeeded ? "Completed" : "Kept on C:",
                    result.DestinationFolder,
                    itemCompleted: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                results.Add(new QuizCompletedCArchiveItemResult(
                    item.HistoryId,
                    item.Label,
                    item.SourceFolder,
                    item.DestinationHint,
                    item.Action,
                    false,
                    false,
                    error.Message));
                ReportProgress(progress, currentNumber, total, item, "Failed safely — kept on C:", item.DestinationHint, itemCompleted: true);
            }
        }

        // One final journal reconciliation catches any path that was changed back while this long
        // operation was running. This is database-only and never deletes a folder.
        var reconciled = ReconcileJournaledQuizArchivePaths();

        return new QuizCompletedCArchiveApplyResult(
            confirmedPreview.ReadyProjects,
            results.Count(result => result.Succeeded),
            results.Count(result => result.Succeeded && result.Action == QuizCompletedCArchiveAction.RestoreJournaledArchiveLink),
            results.Count(result => result.Succeeded && result.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy),
            results.Count(result => result.Succeeded && result.Action == QuizCompletedCArchiveAction.CopyToArchive),
            results.Count(result => result.Succeeded && result.SourceDeleted),
            results.Count(result => result.Succeeded &&
                                    result.Action != QuizCompletedCArchiveAction.RestoreJournaledArchiveLink &&
                                    !result.SourceDeleted),
            results.Count(result => !result.Succeeded),
            reconciled.Repaired,
            results);
    }

    private QuizCompletedCArchiveItemResult RestoreJournaledArchiveLink(
        QuizHistorySummary history,
        QuizCompletedCArchiveItem item,
        int current,
        int total,
        IProgress<QuizCompletedCArchiveProgress>? progress)
    {
        var settings = LoadSettings();
        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        var primaryDatabase = Path.GetFullPath(_databasePath);
        var journal = LoadQuizArchiveRelinkJournal()
            .Where(entry => entry.HistoryId == history.Id)
            .OrderByDescending(entry => entry.RelinkedUtc, StringComparer.Ordinal)
            .FirstOrDefault();
        if (journal is null ||
            !SameStoredPath(journal.PreviousFolder, history.ProjectFolder) ||
            !SameStoredPath(journal.ArchiveFolder, item.DestinationHint) ||
            !SameStoredPath(journal.DatabasePath, primaryDatabase))
        {
            throw new InvalidOperationException("The archive journal no longer proves this exact C: → Z: path change, so the row was left untouched.");
        }

        var destination = Path.GetFullPath(journal.ArchiveFolder);
        if (!Directory.Exists(destination) ||
            !IsPathWithin(quizRoot, destination) ||
            !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, destination), destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The journaled Z: project folder is no longer a valid top-level archive folder.");
        }

        var owners = BuildArchiveOwners(GetQuizHistory(), quizRoot);
        if (ArchiveFolderOwnedByAnotherHistory(destination, history.Id, owners))
            throw new InvalidOperationException("Another Quiz History row now owns the journaled Z: project folder.");

        ReportProgress(progress, current, total, item, "Restoring verified Quiz History link to Z:", destination);
        if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
            throw new InvalidOperationException("Quiz History could not be restored to the journaled Z: folder.");
        RequirePersistedQuizHistoryPath(history.Id, destination);

        return new QuizCompletedCArchiveItemResult(
            history.Id,
            item.Label,
            item.SourceFolder,
            destination,
            QuizCompletedCArchiveAction.RestoreJournaledArchiveLink,
            false,
            true,
            "Restored the previously verified Z: link from the archive journal. No files were moved or deleted.");
    }

    private QuizCompletedCArchiveItemResult FinalizeVerifiedExistingArchiveCopy(
        QuizHistorySummary history,
        QuizCompletedCArchiveItem item,
        int current,
        int total,
        IProgress<QuizCompletedCArchiveProgress>? progress)
    {
        var settings = LoadSettings();
        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        var destination = Path.GetFullPath(item.DestinationHint);
        if (!Directory.Exists(destination) || !IsPathWithin(quizRoot, destination) ||
            !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, destination), destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The verified Z: project folder is no longer a valid top-level quiz archive folder.");
        }

        var owners = BuildArchiveOwners(GetQuizHistory(), quizRoot);
        if (ArchiveFolderOwnedByAnotherHistory(destination, history.Id, owners))
            throw new InvalidOperationException("Another Quiz History row now owns this Z: project folder.");

        ReportProgress(progress, current, total, item, "Verifying existing Z: project copy", destination);
        var sourceSnapshot = TryBuildBulkArchiveSnapshot(history.ProjectFolder);
        var archiveSnapshot = TryBuildBulkArchiveSnapshot(destination);
        if (sourceSnapshot is null || archiveSnapshot is null ||
            !string.Equals(ArchiveFolderName(sourceSnapshot.Folder), ArchiveFolderName(archiveSnapshot.Folder), StringComparison.OrdinalIgnoreCase) ||
            !BulkSnapshotsStrongCopyIdentity(sourceSnapshot, archiveSnapshot))
        {
            throw new InvalidOperationException(
                "The C: and Z: project folders no longer have a strong verified copy identity, so the C: copy was kept.");
        }

        var previous = history.ProjectFolder;
        ReportProgress(progress, current, total, item, "Updating Quiz History to Z:", destination);
        if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
            throw new InvalidOperationException("Quiz History could not be updated to the verified Z: copy.");
        RequirePersistedQuizHistoryPath(history.Id, destination);

        RecordSuccessfulQuizArchiveRelinks([
            new QuizArchiveRelinkRequest(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizArchiveMatchConfidence.Exact)
        ]);

        ReportProgress(progress, current, total, item, "Removing verified C: project copy", destination);
        try
        {
            QuizProjectArchive.DeleteSource(previous);
            RequirePersistedQuizHistoryPath(history.Id, destination);
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy,
                true,
                true,
                "Verified existing Z: project copy reused; Quiz History updated and re-read; C: copy removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy,
                false,
                true,
                "The Z: project copy was verified and Quiz History was confirmed, but the C: folder could not be removed: " + error.Message);
        }
    }

    private QuizCompletedCArchiveItemResult CopyCompletedProjectToArchive(
        QuizHistorySummary history,
        QuizCompletedCArchiveItem item,
        int current,
        int total,
        IProgress<QuizCompletedCArchiveProgress>? progress)
    {
        var settings = LoadSettings();
        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        var previous = history.ProjectFolder;

        ReportProgress(progress, current, total, item, "Copying C: project to Z:", item.DestinationHint);
        var destination = QuizProjectArchive.CopyAndVerifyToQuizArchive(previous, quizRoot);

        try
        {
            ReportProgress(progress, current, total, item, "Verifying database link to new Z: copy", destination);
            if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
                throw new InvalidOperationException("Quiz History could not be updated to the new Z: archive copy.");
            RequirePersistedQuizHistoryPath(history.Id, destination);
        }
        catch
        {
            // This destination was created by this operation. Remove it only while the C: source is
            // still present and the database has not been confirmed to point at the destination.
            var stored = GetQuizHistory().FirstOrDefault(candidate => candidate.Id == history.Id)?.ProjectFolder ?? "";
            if (!SameStoredPath(stored, destination) && Directory.Exists(previous))
                QuizProjectArchive.TryDelete(destination);
            throw;
        }

        RecordSuccessfulQuizArchiveRelinks([
            new QuizArchiveRelinkRequest(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizArchiveMatchConfidence.Exact)
        ]);

        ReportProgress(progress, current, total, item, "Removing verified C: project copy", destination);
        try
        {
            QuizProjectArchive.DeleteSource(previous);
            RequirePersistedQuizHistoryPath(history.Id, destination);
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizCompletedCArchiveAction.CopyToArchive,
                true,
                true,
                "Copied and verified on Z:; Quiz History updated and re-read; C: copy removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizCompletedCArchiveAction.CopyToArchive,
                false,
                true,
                "The Z: copy is complete and Quiz History was confirmed, but the C: folder could not be removed: " + error.Message);
        }
    }

    private QuizHistorySummary RequirePersistedQuizHistoryPath(int historyId, string expectedFolder)
    {
        var persisted = GetQuizHistory().FirstOrDefault(history => history.Id == historyId)
            ?? throw new InvalidOperationException($"History #{historyId} disappeared while its archive path was being verified.");
        if (!SameStoredPath(persisted.ProjectFolder, expectedFolder))
        {
            throw new InvalidOperationException(
                $"History #{historyId} did not retain the expected Z: path after the database update. The C: project was kept.");
        }
        return persisted;
    }

    private static bool TryGetJournalRestoreDestination(
        QuizHistorySummary history,
        string currentSource,
        string quizRoot,
        string primaryDatabase,
        IReadOnlyDictionary<string, HashSet<int>> archiveOwners,
        IReadOnlyDictionary<int, QuizArchiveRelinkJournalEntry> journalByHistory,
        out string destination)
    {
        destination = "";
        if (!journalByHistory.TryGetValue(history.Id, out var entry) ||
            !SameStoredPath(currentSource, entry.PreviousFolder) ||
            !SameStoredPath(primaryDatabase, entry.DatabasePath))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(entry.ArchiveFolder);
            if (!Directory.Exists(candidate) ||
                !IsPathWithin(quizRoot, candidate) ||
                !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, candidate), candidate, StringComparison.OrdinalIgnoreCase) ||
                ArchiveFolderOwnedByAnotherHistory(candidate, history.Id, archiveOwners))
            {
                return false;
            }

            destination = candidate;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static Dictionary<string, HashSet<int>> BuildArchiveOwners(
        IReadOnlyList<QuizHistorySummary> histories,
        string quizRoot)
    {
        var owners = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var history in histories)
        {
            var current = (history.ProjectFolder ?? "").Trim();
            if (current.Length == 0 || !Directory.Exists(current) || !IsPathWithin(quizRoot, current))
                continue;

            var top = ResolveTopLevelArchiveFolder(quizRoot, current);
            if (top is null)
                continue;
            if (!owners.TryGetValue(top, out var ids))
                owners[top] = ids = new HashSet<int>();
            ids.Add(history.Id);
        }
        return owners;
    }

    private static bool ArchiveFolderOwnedByAnotherHistory(
        string folder,
        int historyId,
        IReadOnlyDictionary<string, HashSet<int>> owners)
    {
        return owners.TryGetValue(Path.GetFullPath(folder), out var ownerIds) &&
               ownerIds.Any(ownerId => ownerId != historyId);
    }

    private static BulkArchiveFolderSnapshot? TryBuildBulkArchiveSnapshot(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;
            var full = Path.GetFullPath(folder.Trim());
            var files = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(full, path),
                    path => new FileInfo(path).Length,
                    StringComparer.OrdinalIgnoreCase);
            return new BulkArchiveFolderSnapshot(full, files);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool BulkSnapshotsStrongCopyIdentity(BulkArchiveFolderSnapshot left, BulkArchiveFolderSnapshot right) =>
        QuizProjectArchive.FileMapsHaveStrongCopyIdentity(left.Files, right.Files);

    private static bool IsExistingCDriveFolder(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
                return false;
            var full = Path.GetFullPath(folder.Trim());
            var root = Path.GetPathRoot(full);
            return Directory.Exists(full) &&
                   string.Equals(root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "C:", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string NormalizeBulkArchivePath(string folder)
    {
        try
        {
            return string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder.Trim());
        }
        catch
        {
            return (folder ?? "").Trim();
        }
    }

    private static string ArchiveFolderName(string folder) =>
        Path.GetFileName((folder ?? "").Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";

    private static void ReportProgress(
        IProgress<QuizCompletedCArchiveProgress>? progress,
        int current,
        int total,
        QuizCompletedCArchiveItem item,
        string stage,
        string destination,
        bool itemCompleted = false)
    {
        progress?.Report(new QuizCompletedCArchiveProgress(
            current,
            total,
            item.HistoryId,
            item.Label,
            stage,
            item.SourceFolder,
            destination,
            itemCompleted));
    }
}
