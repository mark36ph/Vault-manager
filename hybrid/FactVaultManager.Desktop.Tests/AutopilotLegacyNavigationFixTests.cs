using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotLegacyNavigationFixTests
{
    [Fact]
    public void LibraryNavigation_InitializesQuizHistoryWhenDeferredStartupHasNotReachedIt()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotLegacyNavigationFix.cs");
        Assert.Contains("autopilot-first-nav:Library", source, StringComparison.Ordinal);
        Assert.Contains("window.InitializeQuizHistoryPage();", source, StringComparison.Ordinal);
        Assert.Contains("window._quizHistoryTabIndex", source, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceNavigation_InitializesYouTubeManagerWhenDeferredStartupHasNotReachedIt()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotLegacyNavigationFix.cs");
        Assert.Contains("autopilot-first-nav:Performance", source, StringComparison.Ordinal);
        Assert.Contains("window.InitializeYouTubeAnalyticsPage();", source, StringComparison.Ordinal);
        Assert.Contains("window._youtubeAnalyticsTabIndex", source, StringComparison.Ordinal);
        Assert.Contains("window.SelectAutopilotNav(\"Performance\")", source, StringComparison.Ordinal);
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
