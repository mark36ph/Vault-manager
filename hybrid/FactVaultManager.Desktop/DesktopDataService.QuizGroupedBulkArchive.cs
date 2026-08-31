namespace FactVaultManager.Desktop;

public enum QuizGroupedCArchiveAction
{
    ReuseVerifiedArchiveCopy,
    CopyToArchive,
}

public sealed record QuizGroupedCArchiveItem(
    string SourceFolder,
    string Label,
    IReadOnlyList<int> HistoryIds,
    string DestinationHint,
    QuizGroupedCArchiveAction Action)
{
    public int HistoryId => HistoryIds.Count == 0 ? 0 : HistoryIds[0];
    public int HistoryRowCount => HistoryIds.Count;
}

public sealed record QuizGroupedCArchiveSkipped(
    string SourceFolder,
    string Label,
    IReadOnlyList<int> HistoryIds,
    string Reason);

public sealed record QuizGroupedCArchivePreview(
    int ExistingPhysicalFolders,
    int ExistingHistoryRows,
    int ReadyPhysicalFolders,
    int ReadyHistoryRows,
    int ReuseVerifiedCopies,
    int CopyNewProjects,
    int BlockedByUploads,
    int SafetySkipped,
    IReadOnlyList<QuizGroupedCArchiveItem> ReadyItems,
    IReadOnlyList<QuizGroupedCArchiveSkipped> SkippedItems);

public sealed record QuizGroupedCArchiveProgress(
    int Current,
    int Total,
    int HistoryId,
    string Label,
    int HistoryRowCount,
    string Stage,
    string SourceFolder,
    string DestinationFolder,
    bool ItemCompleted = false);

public sealed record QuizGroupedCArchiveItemResult(
    string SourceFolder,
    string DestinationFolder,
    string Label,
    IReadOnlyList<int> HistoryIds,
    QuizGroupedCArchiveAction Action,
    bool SourceDeleted,
    bool Succeeded,
    string Message)
{
    public int HistoryId => HistoryIds.Count == 0 ? 0 : HistoryIds[0];
    public int HistoryRowCount => HistoryIds.Count;
}

public sealed record QuizGroupedCArchiveApplyResult(
    int ReadyPhysicalFoldersAtStart,
    int ReadyHistoryRowsAtStart,
    int SucceededPhysicalFolders,
    int SucceededHistoryRows,
    int ReusedExistingCopies,
    int CopiedNewProjects,
    int SourceFoldersRemoved,
    int CleanupWarnings,
    int FailedPhysicalFolders,
    IReadOnlyList<QuizGroupedCArchiveItemResult> Results);

public sealed partial class DesktopDataService
{
    public QuizGroupedCArchivePreview PreviewGroupedCompletedCQuizProjects()
    {
        EnsureQuizHistoryProjectFolderUniquenessGuard();

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
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var archiveFoldersByName = Directory.EnumerateDirectories(quizRoot)
            .Select(Path.GetFullPath)
            .GroupBy(ArchiveFolderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var archiveOwners = BuildArchiveOwners(histories, quizRoot);
        var archiveSnapshotCache = new Dictionary<string, BulkArchiveFolderSnapshot?>(StringComparer.OrdinalIgnoreCase);

        BulkArchiveFolderSnapshot? ArchiveSnapshot(string folder)
        {
            if (archiveSnapshotCache.TryGetValue(folder, out var cached))
                return cached;
            var snapshot = TryBuildBulkArchiveSnapshot(folder);
            archiveSnapshotCache[folder] = snapshot;
            return snapshot;
        }

        var ready = new List<QuizGroupedCArchiveItem>();
        var skipped = new List<QuizGroupedCArchiveSkipped>();
        var blockedByUploads = 0;
        var safetySkipped = 0;

        foreach (var sourceGroup in sourceGroups)
        {
            var source = Path.GetFullPath(sourceGroup.Key);
            var groupHistories = sourceGroup.OrderBy(history => history.Id).ToList();
            var ids = groupHistories.Select(history => history.Id).ToList();
            var label = BuildGroupedArchiveLabel(groupHistories, source);

            var outstanding = groupHistories
                .Select(history => new
                {
                    History = history,
                    Remaining = SocialUploadQueuePlanner.RemainingDestinations(history),
                })
                .Where(item => item.Remaining != SocialUploadDestination.None)
                .ToList();
            if (outstanding.Count > 0)
            {
                blockedByUploads++;
                var detail = string.Join(", ", outstanding.Select(item => $"#{item.History.Id}: {item.Remaining}"));
                skipped.Add(new QuizGroupedCArchiveSkipped(
                    source,
                    label,
                    ids,
                    "At least one linked Quiz History row still has required uploads outstanding: " + detail + ". The whole physical folder stays on C:."));
                continue;
            }

            var sourceSnapshot = TryBuildBulkArchiveSnapshot(source);
            if (sourceSnapshot is null || sourceSnapshot.Files.Count == 0)
            {
                safetySkipped++;
                skipped.Add(new QuizGroupedCArchiveSkipped(
                    source,
                    label,
                    ids,
                    "The physical project folder could not be read safely or contains no files."));
                continue;
            }

            var sourceName = ArchiveFolderName(source);
            var allowedOwners = ids.ToHashSet();
            var reusableMatches = new List<BulkArchiveFolderSnapshot>();
            if (archiveFoldersByName.TryGetValue(sourceName, out var sameNameArchiveFolders))
            {
                foreach (var archiveFolder in sameNameArchiveFolders)
                {
                    if (ArchiveFolderOwnedOutsideGroup(archiveFolder, allowedOwners, archiveOwners))
                        continue;
                    var snapshot = ArchiveSnapshot(archiveFolder);
                    if (snapshot is not null && BulkSnapshotsStrongCopyIdentity(sourceSnapshot, snapshot))
                        reusableMatches.Add(snapshot);
                }
            }

            if (reusableMatches.Count == 1)
            {
                ready.Add(new QuizGroupedCArchiveItem(
                    source,
                    label,
                    ids,
                    reusableMatches[0].Folder,
                    QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy));
            }
            else
            {
                ready.Add(new QuizGroupedCArchiveItem(
                    source,
                    label,
                    ids,
                    Path.Combine(quizRoot, sourceName),
                    QuizGroupedCArchiveAction.CopyToArchive));
            }
        }

        return new QuizGroupedCArchivePreview(
            sourceGroups.Count,
            cProjects.Count,
            ready.Count,
            ready.Sum(item => item.HistoryRowCount),
            ready.Count(item => item.Action == QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy),
            ready.Count(item => item.Action == QuizGroupedCArchiveAction.CopyToArchive),
            blockedByUploads,
            safetySkipped,
            ready,
            skipped);
    }

    public QuizGroupedCArchiveApplyResult ArchiveGroupedCompletedCQuizProjects() =>
        ArchiveGroupedCompletedCQuizProjects(PreviewGroupedCompletedCQuizProjects(), progress: null);

    public QuizGroupedCArchiveApplyResult ArchiveGroupedCompletedCQuizProjects(
        QuizGroupedCArchivePreview confirmedPreview,
        IProgress<QuizGroupedCArchiveProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(confirmedPreview);
        var results = new List<QuizGroupedCArchiveItemResult>();
        var total = confirmedPreview.ReadyItems.Count;

        for (var index = 0; index < confirmedPreview.ReadyItems.Count; index++)
        {
            var item = confirmedPreview.ReadyItems[index];
            var currentNumber = index + 1;
            ReportGroupedProgress(progress, currentNumber, total, item, "Preparing and rechecking physical folder", item.DestinationHint);

            try
            {
                var currentHistories = GetQuizHistory();
                var currentSourceRows = currentHistories
                    .Where(history => SameStoredPath(history.ProjectFolder, item.SourceFolder))
                    .OrderBy(history => history.Id)
                    .ToList();
                var expectedIds = item.HistoryIds.OrderBy(id => id).ToList();
                var currentIds = currentSourceRows.Select(history => history.Id).OrderBy(id => id).ToList();
                if (!expectedIds.SequenceEqual(currentIds))
                {
                    throw new InvalidOperationException(
                        "The Quiz History rows linked to this physical C: folder changed after the preview, so the whole folder was kept on C:.");
                }
                if (!Directory.Exists(item.SourceFolder) || !IsExistingCDriveFolder(item.SourceFolder))
                    throw new InvalidOperationException("The physical C: project folder is no longer available.");

                var nowOutstanding = currentSourceRows
                    .Select(history => new
                    {
                        History = history,
                        Remaining = SocialUploadQueuePlanner.RemainingDestinations(history),
                    })
                    .Where(value => value.Remaining != SocialUploadDestination.None)
                    .ToList();
                if (nowOutstanding.Count > 0)
                {
                    var detail = string.Join(", ", nowOutstanding.Select(value => $"#{value.History.Id}: {value.Remaining}"));
                    throw new InvalidOperationException(
                        "Required uploads are now outstanding for linked row(s) " + detail + ". The whole physical folder was kept on C:.");
                }

                var result = item.Action == QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy
                    ? FinalizeGroupedVerifiedArchiveCopy(currentSourceRows, item, currentNumber, total, progress)
                    : CopyGroupedProjectToArchive(currentSourceRows, item, currentNumber, total, progress);

                results.Add(result);
                ReportGroupedProgress(
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
                results.Add(new QuizGroupedCArchiveItemResult(
                    item.SourceFolder,
                    item.DestinationHint,
                    item.Label,
                    item.HistoryIds,
                    item.Action,
                    false,
                    false,
                    error.Message));
                ReportGroupedProgress(progress, currentNumber, total, item, "Failed safely — whole folder kept on C:", item.DestinationHint, itemCompleted: true);
            }
        }

        return new QuizGroupedCArchiveApplyResult(
            confirmedPreview.ReadyPhysicalFolders,
            confirmedPreview.ReadyHistoryRows,
            results.Count(result => result.Succeeded),
            results.Where(result => result.Succeeded).Sum(result => result.HistoryRowCount),
            results.Count(result => result.Succeeded && result.Action == QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy),
            results.Count(result => result.Succeeded && result.Action == QuizGroupedCArchiveAction.CopyToArchive),
            results.Count(result => result.Succeeded && result.SourceDeleted),
            results.Count(result => result.Succeeded && !result.SourceDeleted),
            results.Count(result => !result.Succeeded),
            results);
    }

    private QuizGroupedCArchiveItemResult FinalizeGroupedVerifiedArchiveCopy(
        IReadOnlyList<QuizHistorySummary> histories,
        QuizGroupedCArchiveItem item,
        int current,
        int total,
        IProgress<QuizGroupedCArchiveProgress>? progress)
    {
        var settings = LoadSettings();
        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        var destination = Path.GetFullPath(item.DestinationHint);
        if (!Directory.Exists(destination) || !IsPathWithin(quizRoot, destination) ||
            !string.Equals(ResolveTopLevelArchiveFolder(quizRoot, destination), destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The verified Z: project folder is no longer a valid top-level quiz archive folder.");
        }

        var allowedOwners = histories.Select(history => history.Id).ToHashSet();
        var owners = BuildArchiveOwners(GetQuizHistory(), quizRoot);
        if (ArchiveFolderOwnedOutsideGroup(destination, allowedOwners, owners))
            throw new InvalidOperationException("Another unrelated Quiz History row now owns this Z: project folder.");

        ReportGroupedProgress(progress, current, total, item, "Verifying existing Z: physical project copy", destination);
        var sourceSnapshot = TryBuildBulkArchiveSnapshot(item.SourceFolder);
        var archiveSnapshot = TryBuildBulkArchiveSnapshot(destination);
        if (sourceSnapshot is null || archiveSnapshot is null ||
            !string.Equals(ArchiveFolderName(sourceSnapshot.Folder), ArchiveFolderName(archiveSnapshot.Folder), StringComparison.OrdinalIgnoreCase) ||
            !BulkSnapshotsStrongCopyIdentity(sourceSnapshot, archiveSnapshot))
        {
            throw new InvalidOperationException(
                "The C: and Z: project folders no longer have a strong verified copy identity, so the whole C: folder was kept.");
        }

        ReportGroupedProgress(progress, current, total, item, $"Updating {histories.Count} Quiz History row(s) atomically to Z:", destination);
        UpdateGroupedHistoryPathsAtomically(histories, item.SourceFolder, destination);
        RecordGroupedArchiveJournal(histories, item.SourceFolder, destination);

        ReportGroupedProgress(progress, current, total, item, "Removing verified physical C: project folder", destination);
        try
        {
            QuizProjectArchive.DeleteSource(item.SourceFolder);
            RequirePersistedGroupedPaths(histories.Select(history => history.Id).ToList(), destination, item.SourceFolder);
            return new QuizGroupedCArchiveItemResult(
                item.SourceFolder,
                destination,
                item.Label,
                item.HistoryIds,
                QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy,
                true,
                true,
                $"Verified existing Z: physical project copy reused; {histories.Count} Quiz History row(s) updated together; C: folder removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizGroupedCArchiveItemResult(
                item.SourceFolder,
                destination,
                item.Label,
                item.HistoryIds,
                QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy,
                false,
                true,
                $"The Z: physical project copy and {histories.Count} Quiz History row(s) were verified, but the C: folder could not be removed: {error.Message}");
        }
    }

    private QuizGroupedCArchiveItemResult CopyGroupedProjectToArchive(
        IReadOnlyList<QuizHistorySummary> histories,
        QuizGroupedCArchiveItem item,
        int current,
        int total,
        IProgress<QuizGroupedCArchiveProgress>? progress)
    {
        var settings = LoadSettings();
        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");

        ReportGroupedProgress(progress, current, total, item, "Copying physical C: project folder to Z:", item.DestinationHint);
        var destination = QuizProjectArchive.CopyAndVerifyToQuizArchive(item.SourceFolder, quizRoot);

        try
        {
            ReportGroupedProgress(progress, current, total, item, $"Updating {histories.Count} Quiz History row(s) atomically to new Z: copy", destination);
            UpdateGroupedHistoryPathsAtomically(histories, item.SourceFolder, destination);
        }
        catch
        {
            var expectedIds = histories.Select(history => history.Id).ToHashSet();
            var stored = GetQuizHistory().Where(history => expectedIds.Contains(history.Id)).ToList();
            if (stored.All(history => !SameStoredPath(history.ProjectFolder, destination)) && Directory.Exists(item.SourceFolder))
                QuizProjectArchive.TryDelete(destination);
            throw;
        }

        RecordGroupedArchiveJournal(histories, item.SourceFolder, destination);

        ReportGroupedProgress(progress, current, total, item, "Removing verified physical C: project folder", destination);
        try
        {
            QuizProjectArchive.DeleteSource(item.SourceFolder);
            RequirePersistedGroupedPaths(histories.Select(history => history.Id).ToList(), destination, item.SourceFolder);
            return new QuizGroupedCArchiveItemResult(
                item.SourceFolder,
                destination,
                item.Label,
                item.HistoryIds,
                QuizGroupedCArchiveAction.CopyToArchive,
                true,
                true,
                $"Copied and verified on Z:; {histories.Count} Quiz History row(s) updated together and re-read; C: folder removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizGroupedCArchiveItemResult(
                item.SourceFolder,
                destination,
                item.Label,
                item.HistoryIds,
                QuizGroupedCArchiveAction.CopyToArchive,
                false,
                true,
                $"The Z: copy and {histories.Count} Quiz History row(s) were verified, but the C: folder could not be removed: {error.Message}");
        }
    }

    private void UpdateGroupedHistoryPathsAtomically(
        IReadOnlyList<QuizHistorySummary> histories,
        string expectedSource,
        string destination)
    {
        if (histories.Count == 0)
            throw new InvalidOperationException("No Quiz History rows were supplied for the physical project folder.");

        var ids = histories.Select(history => history.Id).Distinct().OrderBy(id => id).ToList();
        if (ids.Count != histories.Count)
            throw new InvalidOperationException("Duplicate Quiz History IDs were supplied for the physical project folder.");

        var normalizedSource = Path.GetFullPath(expectedSource.Trim());
        var normalizedDestination = Path.GetFullPath(destination.Trim());
        EnsureQuizHistoryProjectFolderUniquenessGuard();

        using (var connection = OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            foreach (var historyId in ids)
            {
                using var inspect = connection.CreateCommand();
                inspect.Transaction = transaction;
                inspect.CommandText = "SELECT project_folder FROM quiz_history WHERE id = $historyId";
                inspect.Parameters.AddWithValue("$historyId", historyId);
                var current = inspect.ExecuteScalar() as string;
                if (current is null || !SameStoredPath(current, normalizedSource))
                    throw new InvalidOperationException($"History #{historyId} no longer points at the expected physical C: folder.");
            }

            if (ids.Count > 1)
            {
                var groupKey = "physical:" + normalizedSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
                foreach (var historyId in ids)
                {
                    using var groupCommand = connection.CreateCommand();
                    groupCommand.Transaction = transaction;
                    groupCommand.CommandText = """
                        INSERT INTO quiz_history_project_folder_groups(history_id, group_key)
                        VALUES($historyId, $groupKey)
                        ON CONFLICT(history_id) DO UPDATE SET group_key = excluded.group_key
                        """;
                    groupCommand.Parameters.AddWithValue("$historyId", historyId);
                    groupCommand.Parameters.AddWithValue("$groupKey", groupKey);
                    if (groupCommand.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException($"History #{historyId} could not be registered as part of the shared physical project group.");
                }
            }

            foreach (var historyId in ids)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE quiz_history SET project_folder = $destination WHERE id = $historyId";
                update.Parameters.AddWithValue("$destination", normalizedDestination);
                update.Parameters.AddWithValue("$historyId", historyId);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"History #{historyId} could not be updated to the Z: project folder.");
            }

            foreach (var historyId in ids)
            {
                using var verify = connection.CreateCommand();
                verify.Transaction = transaction;
                verify.CommandText = "SELECT project_folder FROM quiz_history WHERE id = $historyId";
                verify.Parameters.AddWithValue("$historyId", historyId);
                var stored = verify.ExecuteScalar() as string;
                if (stored is null || !SameStoredPath(stored, normalizedDestination))
                    throw new InvalidOperationException($"History #{historyId} failed the in-transaction Z: path verification.");
            }

            transaction.Commit();
        }

        try
        {
            RequirePersistedGroupedPaths(ids, normalizedDestination, normalizedSource);
        }
        catch
        {
            TryRestoreGroupedHistoryPaths(ids, normalizedDestination, normalizedSource);
            throw;
        }
    }

    private void RequirePersistedGroupedPaths(
        IReadOnlyList<int> historyIds,
        string expectedDestination,
        string oldSource)
    {
        var idSet = historyIds.ToHashSet();
        var persisted = GetQuizHistory();
        foreach (var historyId in historyIds)
        {
            var history = persisted.FirstOrDefault(candidate => candidate.Id == historyId)
                ?? throw new InvalidOperationException($"History #{historyId} disappeared while the grouped archive path was being verified.");
            if (!SameStoredPath(history.ProjectFolder, expectedDestination))
            {
                throw new InvalidOperationException(
                    $"History #{historyId} did not retain the expected Z: path. The physical C: project folder was kept.");
            }
        }

        var unexpectedSourceOwners = persisted
            .Where(history => !idSet.Contains(history.Id) && SameStoredPath(history.ProjectFolder, oldSource))
            .Select(history => history.Id)
            .ToList();
        if (unexpectedSourceOwners.Count > 0)
        {
            throw new InvalidOperationException(
                "Another Quiz History row began referencing the physical C: folder during the archive operation. The folder was kept on C:.");
        }
    }

    private bool TryRestoreGroupedHistoryPaths(
        IReadOnlyList<int> historyIds,
        string expectedDestination,
        string source)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var historyId in historyIds)
            {
                using var inspect = connection.CreateCommand();
                inspect.Transaction = transaction;
                inspect.CommandText = "SELECT project_folder FROM quiz_history WHERE id = $historyId";
                inspect.Parameters.AddWithValue("$historyId", historyId);
                var current = inspect.ExecuteScalar() as string;
                if (current is null || !SameStoredPath(current, expectedDestination))
                    return false;
            }

            foreach (var historyId in historyIds)
            {
                using var restore = connection.CreateCommand();
                restore.Transaction = transaction;
                restore.CommandText = "UPDATE quiz_history SET project_folder = $source WHERE id = $historyId";
                restore.Parameters.AddWithValue("$source", Path.GetFullPath(source));
                restore.Parameters.AddWithValue("$historyId", historyId);
                if (restore.ExecuteNonQuery() != 1)
                    return false;
            }

            transaction.Commit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RecordGroupedArchiveJournal(
        IReadOnlyList<QuizHistorySummary> histories,
        string previous,
        string destination)
    {
        RecordSuccessfulQuizArchiveRelinks(histories.Select(history =>
            new QuizArchiveRelinkRequest(
                history.Id,
                HistoryLabel(history),
                previous,
                destination,
                QuizArchiveMatchConfidence.Exact)).ToList());
    }

    private static bool ArchiveFolderOwnedOutsideGroup(
        string folder,
        IReadOnlySet<int> allowedHistoryIds,
        IReadOnlyDictionary<string, HashSet<int>> owners)
    {
        return owners.TryGetValue(Path.GetFullPath(folder), out var ownerIds) &&
               ownerIds.Any(ownerId => !allowedHistoryIds.Contains(ownerId));
    }

    private static string BuildGroupedArchiveLabel(IReadOnlyList<QuizHistorySummary> histories, string source)
    {
        if (histories.Count == 1)
            return HistoryLabel(histories[0]);

        var ids = string.Join(", ", histories.Select(history => $"#{history.Id}"));
        return $"{ArchiveFolderName(source)} • {histories.Count} linked History rows ({ids})";
    }

    private static void ReportGroupedProgress(
        IProgress<QuizGroupedCArchiveProgress>? progress,
        int current,
        int total,
        QuizGroupedCArchiveItem item,
        string stage,
        string destination,
        bool itemCompleted = false)
    {
        progress?.Report(new QuizGroupedCArchiveProgress(
            current,
            total,
            item.HistoryId,
            item.Label,
            item.HistoryRowCount,
            stage,
            item.SourceFolder,
            destination,
            itemCompleted));
    }
}
