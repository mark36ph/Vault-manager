namespace FactVaultManager.Desktop;

public enum QuizCompletedCArchiveAction
{
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
    int ReuseVerifiedCopies,
    int CopyNewProjects,
    int BlockedByUploads,
    int SafetySkipped,
    IReadOnlyList<QuizCompletedCArchiveItem> ReadyItems,
    IReadOnlyList<QuizCompletedCArchiveSkipped> SkippedItems);

public sealed record QuizCompletedCArchiveItemResult(
    int HistoryId,
    string Label,
    string SourceFolder,
    string DestinationFolder,
    bool ReusedExistingCopy,
    bool SourceDeleted,
    bool Succeeded,
    string Message);

public sealed record QuizCompletedCArchiveApplyResult(
    int ReadyAtStart,
    int Succeeded,
    int ReusedExistingCopies,
    int CopiedNewProjects,
    int SourceFoldersRemoved,
    int CleanupWarnings,
    int Failed,
    IReadOnlyList<QuizCompletedCArchiveItemResult> Results);

public sealed partial class DesktopDataService
{
    private sealed record BulkArchiveFolderSnapshot(
        string Folder,
        IReadOnlyDictionary<string, long> Files,
        long TotalBytes);

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

        var duplicateSources = cProjects
            .GroupBy(history => NormalizeBulkArchivePath(history.ProjectFolder), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var archiveFolders = Directory.EnumerateDirectories(quizRoot)
            .Select(Path.GetFullPath)
            .OrderBy(folder => Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var archiveOwners = BuildArchiveOwners(histories, quizRoot);
        var archiveSnapshots = archiveFolders
            .Select(TryBuildBulkArchiveSnapshot)
            .Where(snapshot => snapshot is not null)
            .Cast<BulkArchiveFolderSnapshot>()
            .ToList();

        var ready = new List<QuizCompletedCArchiveItem>();
        var skipped = new List<QuizCompletedCArchiveSkipped>();
        var blockedByUploads = 0;
        var safetySkipped = 0;

        foreach (var history in cProjects.OrderBy(history => history.Id))
        {
            var source = Path.GetFullPath(history.ProjectFolder.Trim());
            var label = HistoryLabel(history);

            if (duplicateSources.Contains(source))
            {
                safetySkipped++;
                skipped.Add(new QuizCompletedCArchiveSkipped(
                    history.Id,
                    label,
                    source,
                    "More than one Quiz History row points at this same C: project folder, so it was left untouched."));
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

            var equivalent = archiveSnapshots
                .Where(snapshot => BulkSnapshotsEquivalent(sourceSnapshot, snapshot))
                .Where(snapshot => !ArchiveFolderOwnedByAnotherHistory(snapshot.Folder, history.Id, archiveOwners))
                .ToList();

            var sourceName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var sameName = equivalent
                .Where(snapshot => string.Equals(Path.GetFileName(snapshot.Folder), sourceName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            BulkArchiveFolderSnapshot? reusable = null;
            if (sameName.Count == 1)
                reusable = sameName[0];
            else if (sameName.Count == 0 && equivalent.Count == 1)
                reusable = equivalent[0];

            if (reusable is not null)
            {
                ready.Add(new QuizCompletedCArchiveItem(
                    history.Id,
                    label,
                    source,
                    reusable.Folder,
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
            ready.Count(item => item.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy),
            ready.Count(item => item.Action == QuizCompletedCArchiveAction.CopyToArchive),
            blockedByUploads,
            safetySkipped,
            ready,
            skipped);
    }

    public QuizCompletedCArchiveApplyResult ArchiveCompletedCQuizProjects()
    {
        // Rebuild immediately before changing anything. This rechecks upload completion,
        // source paths, duplicate-history ownership, archive contents, and existing Z: owners.
        var preview = PreviewCompletedCQuizProjects();
        var results = new List<QuizCompletedCArchiveItemResult>();

        foreach (var item in preview.ReadyItems)
        {
            try
            {
                var history = GetQuizHistory().FirstOrDefault(candidate => candidate.Id == item.HistoryId);
                if (history is null)
                    throw new InvalidOperationException("The Quiz History row no longer exists.");
                if (!SameStoredPath(history.ProjectFolder, item.SourceFolder))
                    throw new InvalidOperationException("The stored project path changed after the preview, so this quiz was skipped.");
                if (!IsExistingCDriveFolder(history.ProjectFolder))
                    throw new InvalidOperationException("The C: project folder is no longer available.");

                var remaining = SocialUploadQueuePlanner.RemainingDestinations(history);
                if (remaining != SocialUploadDestination.None)
                    throw new InvalidOperationException($"Required uploads are now outstanding: {remaining}.");

                results.Add(item.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy
                    ? FinalizeVerifiedExistingArchiveCopy(history, item)
                    : CopyCompletedProjectToArchive(history, item));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                results.Add(new QuizCompletedCArchiveItemResult(
                    item.HistoryId,
                    item.Label,
                    item.SourceFolder,
                    item.DestinationHint,
                    item.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy,
                    false,
                    false,
                    error.Message));
            }
        }

        return new QuizCompletedCArchiveApplyResult(
            preview.ReadyProjects,
            results.Count(result => result.Succeeded),
            results.Count(result => result.Succeeded && result.ReusedExistingCopy),
            results.Count(result => result.Succeeded && !result.ReusedExistingCopy),
            results.Count(result => result.Succeeded && result.SourceDeleted),
            results.Count(result => result.Succeeded && !result.SourceDeleted),
            results.Count(result => !result.Succeeded),
            results);
    }

    private QuizCompletedCArchiveItemResult FinalizeVerifiedExistingArchiveCopy(
        QuizHistorySummary history,
        QuizCompletedCArchiveItem item)
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

        if (!QuizProjectArchive.AreDirectoriesEquivalent(history.ProjectFolder, destination))
            throw new InvalidOperationException("The C: and Z: project folders are no longer identical, so the C: copy was kept.");

        var previous = history.ProjectFolder;
        if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
            throw new InvalidOperationException("Quiz History could not be updated to the verified Z: copy.");

        RecordSuccessfulQuizArchiveRelinks([
            new QuizArchiveRelinkRequest(
                history.Id,
                item.Label,
                previous,
                destination,
                QuizArchiveMatchConfidence.Exact)
        ]);

        try
        {
            QuizProjectArchive.DeleteSource(previous);
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                true,
                true,
                true,
                "Verified existing Z: copy reused; Quiz History updated; C: copy removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                true,
                false,
                true,
                "The Z: copy was verified and Quiz History was updated, but the C: folder could not be removed: " + error.Message);
        }
    }

    private QuizCompletedCArchiveItemResult CopyCompletedProjectToArchive(
        QuizHistorySummary history,
        QuizCompletedCArchiveItem item)
    {
        var settings = LoadSettings();
        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        var previous = history.ProjectFolder;
        var destination = QuizProjectArchive.CopyAndVerifyToQuizArchive(previous, quizRoot);

        try
        {
            if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
                throw new InvalidOperationException("Quiz History could not be updated to the new Z: archive copy.");
        }
        catch
        {
            // This destination was created by this operation, so it is safe to remove it if the DB update failed.
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

        try
        {
            QuizProjectArchive.DeleteSource(previous);
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                false,
                true,
                true,
                "Copied and verified on Z:; Quiz History updated; C: copy removed.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizCompletedCArchiveItemResult(
                history.Id,
                item.Label,
                previous,
                destination,
                false,
                false,
                true,
                "The Z: copy is complete and Quiz History was updated, but the C: folder could not be removed: " + error.Message);
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
            return new BulkArchiveFolderSnapshot(full, files, files.Values.Sum());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool BulkSnapshotsEquivalent(BulkArchiveFolderSnapshot left, BulkArchiveFolderSnapshot right)
    {
        return left.Files.Count == right.Files.Count &&
               left.TotalBytes == right.TotalBytes &&
               left.Files.All(pair => right.Files.TryGetValue(pair.Key, out var length) && length == pair.Value);
    }

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
}
