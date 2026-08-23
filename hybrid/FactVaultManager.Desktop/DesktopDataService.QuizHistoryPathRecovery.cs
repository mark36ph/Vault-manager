namespace FactVaultManager.Desktop;

public sealed record QuizHistoryPathRecoveryResult(int Updated, int AlreadyCorrect, int Unresolved);

public sealed partial class DesktopDataService
{
    public QuizHistoryPathRecoveryResult RecoverQuizHistoryProjectFolders()
    {
        var histories = GetQuizHistory();
        if (histories.Count == 0)
            return new QuizHistoryPathRecoveryResult(0, 0, 0);

        var matches = SocialUploadQueuePathFinder.FindMissingVideos(histories, GetProjectsRoot());
        var historiesById = histories.ToDictionary(history => history.Id);
        var updated = 0;
        var alreadyCorrect = 0;

        foreach (var match in matches)
        {
            if (!historiesById.TryGetValue(match.HistoryId, out var history))
                continue;

            var projectFolder = Path.GetDirectoryName(match.VideoPath) ?? "";
            if (PathsEqual(history.ProjectFolder, projectFolder))
            {
                alreadyCorrect++;
                continue;
            }

            if (UpdateQuizHistoryProjectFolder(history.Id, projectFolder))
                updated++;
        }

        return new QuizHistoryPathRecoveryResult(
            updated,
            alreadyCorrect,
            histories.Count - updated - alreadyCorrect);
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
