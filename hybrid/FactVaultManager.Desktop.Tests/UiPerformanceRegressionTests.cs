namespace FactVaultManager.Desktop.Tests;

public sealed class UiPerformanceRegressionTests
{
    [Fact]
    public void DailyCleanup_IsBoundedInsteadOfScanningForeverEverySecond()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.DailyUiCleanup.cs");

        Assert.Contains("_dailyUiCleanupStartupPassesRemaining = 8", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(500)", source, StringComparison.Ordinal);
        Assert.Contains("_dailyUiCleanupTimer?.Stop();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Interval = TimeSpan.FromSeconds(1)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsYouPolling_IsSlowerAndOnlyRunsWhileAutopilotHomeIsVisible()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");

        Assert.Contains("TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex == _autopilotHomeTabIndex", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex != _autopilotHomeTabIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Interval = TimeSpan.FromSeconds(1)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryStatusAudit_RunsOffUiThreadAndUsesRecyclingVirtualization()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformStatusFix.cs");

        Assert.Contains("TimeSpan.FromSeconds(30)", source, StringComparison.Ordinal);
        Assert.Contains("_libraryPlatformStatusRefreshRunning", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ComputeLibraryReleasePlatformStatuses(histories))", source, StringComparison.Ordinal);
        Assert.Contains("EnableRowVirtualization = true", source, StringComparison.Ordinal);
        Assert.Contains("EnableColumnVirtualization = true", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizationMode.Recycling", source, StringComparison.Ordinal);
        Assert.Contains("if (!changed)", source, StringComparison.Ordinal);
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
