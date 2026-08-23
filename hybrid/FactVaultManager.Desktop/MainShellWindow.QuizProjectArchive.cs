namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private async Task<QuizProjectArchiveResult?> ArchiveCompletedQuizAsync(
        int historyId,
        Action<string> reportStatus)
    {
        var settings = _data.LoadSettings();
        if (!settings.ArchiveAfterUpload)
            return null;

        var history = _data.GetQuizHistory().FirstOrDefault(item => item.Id == historyId);
        if (history is null ||
            SocialUploadQueuePlanner.RemainingDestinations(history) != SocialUploadDestination.None)
            return null;

        reportStatus("All uploads are complete. Copying and verifying the project on the NAS...");
        return await Task.Run(() => _data.ArchiveQuizProject(historyId));
    }
}
