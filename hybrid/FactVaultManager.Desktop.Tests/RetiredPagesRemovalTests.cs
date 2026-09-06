namespace FactVaultManager.Desktop.Tests;

public sealed class RetiredPagesRemovalTests
{
    [Fact]
    public void CoreLifecycle_DoesNotConstructRetiredManagerPages()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.CoreLifecycle.cs");

        Assert.DoesNotContain("InitializeQuizQuestionBankPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeUploadManagerPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeFacebookAnalyticsPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeInstagramManagerPage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_DoesNotStartRetiredReleaseReadinessPage()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/Program.cs");

        Assert.DoesNotContain("InitializeScheduledReleaseReadinessForApp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedRemoval_RemovesNavigationButtonAndTab()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AdvancedRemoval.cs");

        Assert.Contains("_autopilotNavButtons.TryGetValue(\"Advanced\"", source, StringComparison.Ordinal);
        Assert.Contains("_autopilotNavButtons.Remove(\"Advanced\")", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.Items.RemoveAt(_autopilotAdvancedTabIndex)", source, StringComparison.Ordinal);
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
