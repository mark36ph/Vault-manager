namespace FactVaultManager.Desktop;

public sealed record QuizHistoryPathRecoveryResult(
    int Updated,
    int AlreadyCorrect,
    int Unresolved,
    IReadOnlyList<int> UnresolvedHistoryIds);

public sealed partial class DesktopDataService
{
    public QuizHistoryPathRecoveryResult RecoverQuizHistoryProjectFolders()
    {
        var histories = GetQuizHistory();
        if (histories.Count == 0)
            return new QuizHistoryPathRecoveryResult(0, 0, 0, []);

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
        var updated = 0;
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
