namespace FactVaultManager.Desktop;

public sealed record QuizArchivePathMatch(
    int HistoryId,
    string Label,
    string ArchiveFolder);

public sealed record QuizArchiveExistingPathMatch(
    int HistoryId,
    string Label,
    string CurrentFolder,
    string ArchiveFolder);

public sealed record QuizArchiveAuditEntry(
    int HistoryId,
    string Label,
    string CurrentFolder,
    IReadOnlyList<string> CandidateFolders);

public sealed record QuizArchiveMatchPreview(
    int ArchiveFolders,
    int HistoryEntries,
    int AlreadyLinked,
    int LocalPathExists,
    int ReadyToMatch,
    int Ambiguous,
    int Unmatched,
    IReadOnlyList<QuizArchivePathMatch> Matches,
    IReadOnlyList<string> UnlinkedArchiveFolders,
    IReadOnlyList<QuizArchiveExistingPathMatch> ExistingPathArchiveMatches,
    IReadOnlyList<QuizArchiveAuditEntry> AmbiguousEntries,
    IReadOnlyList<QuizArchiveAuditEntry> UnmatchedEntries);

public sealed record QuizArchiveMatchApplyResult(int Updated, int Skipped);

public sealed partial class DesktopDataService
{
    public QuizArchiveMatchPreview PreviewQuizArchiveMatches()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var archiveRoot = Path.GetFullPath(settings.NasArchiveFolder.Trim());
        var quizRoot = Path.Combine(archiveRoot, "Quizzes");
        if (!Directory.Exists(quizRoot))
            throw new DirectoryNotFoundException($"The quiz archive folder was not found: {quizRoot}");

        var archiveFolders = Directory.EnumerateDirectories(quizRoot)
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var archiveFolderSet = new HashSet<string>(archiveFolders, StringComparer.OrdinalIgnoreCase);
        var histories = GetQuizHistory();
        var alreadyLinked = new List<QuizHistorySummary>();
        var existingOutsideArchive = new List<QuizHistorySummary>();
        var missingPath = new List<QuizHistorySummary>();
        var linkedArchiveFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var history in histories)
        {
            var current = (history.ProjectFolder ?? "").Trim();
            if (current.Length > 0 && Directory.Exists(current))
            {
                if (IsPathWithin(quizRoot, current))
                {
                    alreadyLinked.Add(history);
                    var linkedProjectFolder = ResolveTopLevelArchiveFolder(quizRoot, current);
                    if (linkedProjectFolder is not null)
                        linkedArchiveFolders.Add(linkedProjectFolder);
                }
                else
                {
                    existingOutsideArchive.Add(history);
                }
                continue;
            }

            missingPath.Add(history);
        }

        // Clear ProjectFolder for matching so an existing C:/other path does not prevent the
        // path finder from auditing whether the same quiz also has a copy in the Z: archive.
        var auditableHistories = existingOutsideArchive
            .Concat(missingPath)
            .Select(history => history with { ProjectFolder = string.Empty })
            .ToList();
        var rawMatches = SocialUploadQueuePathFinder.FindMissingVideos(auditableHistories, quizRoot);
        var foldersByHistory = rawMatches
            .GroupBy(match => match.HistoryId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(match => ResolveArchiveProjectFolderFromVideo(quizRoot, match.VideoPath))
                    .Where(folder => folder is not null)
                    .Select(folder => folder!)
                    .Where(folder => archiveFolderSet.Contains(folder) && !linkedArchiveFolders.Contains(folder))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(folder => Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)
                    .ToList());

        // A high-confidence automatic match must be one history -> one folder and that folder
        // must not simultaneously be a candidate for another history record.
        var historiesByFolder = foldersByHistory
            .SelectMany(pair => pair.Value.Select(folder => (HistoryId: pair.Key, Folder: folder)))
            .GroupBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.HistoryId).Distinct().ToList(),
                StringComparer.OrdinalIgnoreCase);

        var matches = new List<QuizArchivePathMatch>();
        var existingPathArchiveMatches = new List<QuizArchiveExistingPathMatch>();
        var ambiguousEntries = new List<QuizArchiveAuditEntry>();
        var unmatchedEntries = new List<QuizArchiveAuditEntry>();

        foreach (var history in missingPath)
        {
            var folders = CandidateFolders(history.Id, foldersByHistory);
            if (folders.Count == 0)
            {
                unmatchedEntries.Add(new QuizArchiveAuditEntry(
                    history.Id,
                    HistoryLabel(history),
                    (history.ProjectFolder ?? string.Empty).Trim(),
                    folders));
                continue;
            }

            if (!IsUniqueArchiveMatch(history.Id, folders, historiesByFolder))
            {
                ambiguousEntries.Add(new QuizArchiveAuditEntry(
                    history.Id,
                    HistoryLabel(history),
                    (history.ProjectFolder ?? string.Empty).Trim(),
                    folders));
                continue;
            }

            matches.Add(new QuizArchivePathMatch(history.Id, HistoryLabel(history), folders[0]));
        }

        foreach (var history in existingOutsideArchive)
        {
            var folders = CandidateFolders(history.Id, foldersByHistory);
            if (!IsUniqueArchiveMatch(history.Id, folders, historiesByFolder))
                continue;

            existingPathArchiveMatches.Add(new QuizArchiveExistingPathMatch(
                history.Id,
                HistoryLabel(history),
                history.ProjectFolder.Trim(),
                folders[0]));
        }

        var unlinkedArchiveFolders = archiveFolders
            .Where(folder => !linkedArchiveFolders.Contains(folder))
            .ToList();

        return new QuizArchiveMatchPreview(
            archiveFolders.Count,
            histories.Count,
            alreadyLinked.Count,
            existingOutsideArchive.Count,
            matches.Count,
            ambiguousEntries.Count,
            unmatchedEntries.Count,
            matches.OrderBy(match => match.HistoryId).ToList(),
            unlinkedArchiveFolders,
            existingPathArchiveMatches.OrderBy(match => match.HistoryId).ToList(),
            ambiguousEntries.OrderBy(entry => entry.HistoryId).ToList(),
            unmatchedEntries.OrderBy(entry => entry.HistoryId).ToList());
    }

    public QuizArchiveMatchApplyResult ApplyQuizArchiveMatches(IReadOnlyList<QuizArchivePathMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var quizRoot = Path.Combine(Path.GetFullPath(settings.NasArchiveFolder.Trim()), "Quizzes");
        var histories = GetQuizHistory().ToDictionary(history => history.Id);
        var updated = 0;
        var skipped = 0;

        foreach (var match in matches)
        {
            if (!histories.TryGetValue(match.HistoryId, out var history) ||
                !Directory.Exists(match.ArchiveFolder) ||
                !IsPathWithin(quizRoot, match.ArchiveFolder))
            {
                skipped++;
                continue;
            }

            // Applying a recovered match is intentionally restricted to records whose current
            // folder is missing. Existing C:/other paths are audit-only and can never be replaced here.
            var current = (history.ProjectFolder ?? "").Trim();
            if (current.Length > 0 && Directory.Exists(current))
            {
                skipped++;
                continue;
            }

            if (UpdateQuizHistoryProjectFolder(history.Id, match.ArchiveFolder))
                updated++;
            else
                skipped++;
        }

        return new QuizArchiveMatchApplyResult(updated, skipped);
    }

    private static IReadOnlyList<string> CandidateFolders(
        int historyId,
        IReadOnlyDictionary<int, List<string>> foldersByHistory) =>
        foldersByHistory.TryGetValue(historyId, out var folders)
            ? folders
            : Array.Empty<string>();

    private static bool IsUniqueArchiveMatch(
        int historyId,
        IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, List<int>> historiesByFolder)
    {
        if (folders.Count != 1 ||
            !historiesByFolder.TryGetValue(folders[0], out var folderHistories))
            return false;

        return folderHistories.Count == 1 && folderHistories[0] == historyId;
    }

    private static string? ResolveArchiveProjectFolderFromVideo(string quizRoot, string videoPath)
    {
        var videoFolder = Path.GetDirectoryName(videoPath);
        return videoFolder is null ? null : ResolveTopLevelArchiveFolder(quizRoot, videoFolder);
    }

    private static string? ResolveTopLevelArchiveFolder(string quizRoot, string candidate)
    {
        try
        {
            var root = Path.GetFullPath(quizRoot);
            var fullCandidate = Path.GetFullPath(candidate);
            var relative = Path.GetRelativePath(root, fullCandidate);
            if (relative == "." || Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                return null;

            var firstSegment = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstSegment)
                ? null
                : Path.GetFullPath(Path.Combine(root, firstSegment));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string HistoryLabel(QuizHistorySummary history)
    {
        var series = history.SeriesName.Trim();
        if (series.Length == 0)
            series = history.Title.Trim();
        if (series.Length == 0)
            series = $"Quiz {history.Id}";

        var episode = history.EpisodeNumber > 0 ? $" #{history.EpisodeNumber:000}" : "";
        return $"{series}{episode} ({history.VideoType})";
    }

    private static bool IsPathWithin(string root, string candidate)
    {
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
            return relative == "." ||
                   (!Path.IsPathRooted(relative) && relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
