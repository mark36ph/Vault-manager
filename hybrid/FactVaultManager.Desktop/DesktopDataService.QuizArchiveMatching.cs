namespace FactVaultManager.Desktop;

public sealed record QuizArchivePathMatch(
    int HistoryId,
    string Label,
    string ArchiveFolder);

public sealed record QuizArchiveMatchPreview(
    int ArchiveFolders,
    int HistoryEntries,
    int AlreadyLinked,
    int LocalPathExists,
    int ReadyToMatch,
    int Ambiguous,
    int Unmatched,
    IReadOnlyList<QuizArchivePathMatch> Matches);

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

        var histories = GetQuizHistory();
        var alreadyLinked = 0;
        var localPathExists = 0;
        var candidates = new List<QuizHistorySummary>();

        foreach (var history in histories)
        {
            var current = (history.ProjectFolder ?? "").Trim();
            if (current.Length > 0 && Directory.Exists(current))
            {
                if (IsPathWithin(quizRoot, current))
                    alreadyLinked++;
                else
                    localPathExists++;
                continue;
            }

            candidates.Add(history);
        }

        var rawMatches = SocialUploadQueuePathFinder.FindMissingVideos(candidates, quizRoot);
        var foldersByHistory = rawMatches
            .GroupBy(match => match.HistoryId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(match => Path.GetDirectoryName(match.VideoPath) ?? "")
                    .Where(folder => folder.Length > 0 && IsPathWithin(quizRoot, folder))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var historiesByFolder = foldersByHistory
            .SelectMany(pair => pair.Value.Select(folder => (HistoryId: pair.Key, Folder: folder)))
            .GroupBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.HistoryId).Distinct().ToList(),
                StringComparer.OrdinalIgnoreCase);

        var historyById = histories.ToDictionary(history => history.Id);
        var matches = new List<QuizArchivePathMatch>();
        var ambiguousIds = new HashSet<int>();

        foreach (var history in candidates)
        {
            if (!foldersByHistory.TryGetValue(history.Id, out var folders) || folders.Count == 0)
                continue;

            if (folders.Count != 1 ||
                !historiesByFolder.TryGetValue(folders[0], out var folderHistories) ||
                folderHistories.Count != 1)
            {
                ambiguousIds.Add(history.Id);
                continue;
            }

            var label = HistoryLabel(historyById[history.Id]);
            matches.Add(new QuizArchivePathMatch(history.Id, label, folders[0]));
        }

        var unmatched = Math.Max(0, candidates.Count - matches.Count - ambiguousIds.Count);
        var archiveFolders = Directory.EnumerateDirectories(quizRoot).Count();
        return new QuizArchiveMatchPreview(
            archiveFolders,
            histories.Count,
            alreadyLinked,
            localPathExists,
            matches.Count,
            ambiguousIds.Count,
            unmatched,
            matches.OrderBy(match => match.HistoryId).ToList());
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
