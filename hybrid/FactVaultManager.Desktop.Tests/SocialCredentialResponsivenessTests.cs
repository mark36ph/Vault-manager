namespace FactVaultManager.Desktop.Tests;

public sealed class SocialCredentialResponsivenessTests
{
    [Fact]
    public void Build140_FullAutopilotDoesNotRunRecursiveProjectRecoveryDuringRoutineRefresh()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.FullAutopilot.cs");

        Assert.DoesNotContain("_data.RecoverQuizHistoryProjectFolders();", source, StringComparison.Ordinal);
        Assert.Contains("var local = await Task.Run(() =>", source, StringComparison.Ordinal);
        Assert.Contains("var histories = _data.GetQuizHistory(2_000);", source, StringComparison.Ordinal);
        Assert.Contains("YouTubeGrowthSnapshotStore.Load(growthStorePath)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build140_InstagramNeedsYouSyncIsThrottledOffUiThreadAndNonOverlapping()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramPromoFollowup.cs");

        Assert.Contains("Interval = TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Interval = TimeSpan.FromSeconds(1)", source, StringComparison.Ordinal);
        Assert.Contains(
            "Interlocked.CompareExchange(ref _instagramPromoNeedsSyncBusy, 1, 0)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("var summary = await Task.Run(() =>", source, StringComparison.Ordinal);
        Assert.Contains("BuildInstagramPromoFollowupNeeds(state)", source, StringComparison.Ordinal);
        Assert.Contains(
            "Interlocked.Exchange(ref _instagramPromoNeedsSyncBusy, 0);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build140_InstagramAutopilotCandidateScanRunsOffUiThread()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramPromoFollowup.cs");

        Assert.Contains("var local = await Task.Run(() =>", source, StringComparison.Ordinal);
        Assert.Contains("var preferences = AutopilotSchedulePreferencesStore.Load(settingsPath);", source, StringComparison.Ordinal);
        Assert.Contains("var publicationState = _data.PublicationState;", source, StringComparison.Ordinal);
        Assert.Contains("_data.GetQuizHistory(2_000)", source, StringComparison.Ordinal);
        Assert.Contains("QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder)", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
