namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotResponsivenessTests
{
    [Fact]
    public void QuizAutopilot_MediaPersistenceRunsOffDispatcherAndIsThrottled()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizAutopilot.cs");

        Assert.Contains(
            "QuizAutopilotMediaPersistencePollInterval = TimeSpan.FromSeconds(1)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await Task.Run(() => TryPersistNewQuizProjectQuestionMedia(existingIds, persistedIds, questionBank));",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await Task.Delay(QuizAutopilotMediaPersistencePollInterval);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(150);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuizAutopilot_LoadsQuestionBankOnceAndDoesNotRecopyCompletedProjects()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizAutopilot.cs");

        Assert.Contains(
            "var questionBank = await Task.Run(LoadQuizAutopilotQuestionBank);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("!persistedIds.Contains(history.Id)", source, StringComparison.Ordinal);
        Assert.Contains("persistedIds.Add(history.Id);", source, StringComparison.Ordinal);
        Assert.Contains(
            "var created = await Task.Run(() => _data.GetQuizHistory(2_000)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AutopilotNeedsYouRefresh_RunsFileReadsOffDispatcherAndPreventsOverlap()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");

        Assert.Contains(
            "Interlocked.CompareExchange(ref _autopilotNeedsYouCountSyncBusy, 1, 0)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("var grouped = await Task.Run(() =>", source, StringComparison.Ordinal);
        Assert.Contains("FactburstFullAutopilotStateStore.Load(settingsPath)", source, StringComparison.Ordinal);
        Assert.Contains("YouTubeGrowthSnapshotStore.Load(growthStorePath)", source, StringComparison.Ordinal);
        Assert.Contains(
            "Interlocked.Exchange(ref _autopilotNeedsYouCountSyncBusy, 0);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build136_RetiresObsoleteAutomaticRecoveryPasses()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var program = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/Program.cs");

        Assert.DoesNotContain("InitializeAutopilotRecoverySupervisor();", build, StringComparison.Ordinal);
        Assert.DoesNotContain("InstalledDatabaseRecovery.Run();", program, StringComparison.Ordinal);
        Assert.Contains("InstalledLibraryRecoveryV2.Run();", program, StringComparison.Ordinal);
        Assert.Contains("InstalledQuestionLibraryRecoveryV3.Run();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Build137_RemovesNeedsYouCompatibilityShim()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var countSync = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");
        var alignedQueue = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/AutopilotNeedsYouAlignedQueue.cs");

        Assert.Contains("CurrentBuildNumber = 137", build, StringComparison.Ordinal);
        Assert.DoesNotContain("private void SyncAutopilotNeedsYouCount()", countSync, StringComparison.Ordinal);
        Assert.Contains("await SyncAutopilotNeedsYouCountAsync();", alignedQueue, StringComparison.Ordinal);
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
