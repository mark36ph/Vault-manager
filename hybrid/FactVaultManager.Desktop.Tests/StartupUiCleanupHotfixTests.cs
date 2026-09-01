namespace FactVaultManager.Desktop.Tests;

public sealed class StartupUiCleanupHotfixTests
{
    [Fact]
    public void StartupCleanup_DoesNotStartTheLegacyRepeatingCleanupDuringWindowLoad()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");

        Assert.Contains("CurrentBuildNumber =", build, StringComparison.Ordinal);
        Assert.Contains("InitializeStartupSafeUiCleanup();", build, StringComparison.Ordinal);
        Assert.DoesNotContain("window.InitializeDailyUiCleanup();", build, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupSafeCleanup_IsOneIdlePassAndCoalescedPageChanges()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.StartupUiCleanupHotfix.cs");

        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
        Assert.Contains("_startupSafeUiCleanupQueued", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(eventArgs.OriginalSource, MainTabs)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyDailyUiCleanup();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromSeconds", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
