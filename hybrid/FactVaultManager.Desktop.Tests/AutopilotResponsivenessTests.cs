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
    public void Build136_IsTheAutopilotResponsivenessRelease()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        Assert.Contains("CurrentBuildNumber = 136", build, StringComparison.Ordinal);
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
