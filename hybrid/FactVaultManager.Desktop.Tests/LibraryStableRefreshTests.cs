namespace FactVaultManager.Desktop.Tests;

public sealed class LibraryStableRefreshTests
{
    [Fact]
    public void PlatformRefresh_StopsOlderTimerAndOnlyRepaintsWhenValuesChange()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformStatusFix.cs");

        Assert.Contains("_libraryPublicationStatusRefreshTimer?.Stop();", source, StringComparison.Ordinal);
        Assert.Contains("RefreshLibraryReleasePlatformStatusSnapshot();", source, StringComparison.Ordinal);
        Assert.Contains("var changed =", source, StringComparison.Ordinal);
        Assert.Contains("if (!changed)", source, StringComparison.Ordinal);
        Assert.Contains("_quizHistoryGrid.Items.Refresh();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformTimer_DoesNotRebindSocialColumnsOnEveryTick()
    {
        var statusSource = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformStatusFix.cs");
        var symbolSource = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformSymbolFix.cs");

        Assert.DoesNotContain("RebindLibraryPlatformColumn(\"FB\"", statusSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RebindLibraryPlatformColumn(\"IG\"", statusSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_libraryPlatformStatusFixTimer.Tick +=", symbolSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_quizHistoryGrid.Items.Refresh();", symbolSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DailyCleanup_LeavesLibraryColumnsAloneAfterLayoutIsLocked()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.DailyUiCleanup.cs");
        var platformSource = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.LibraryPlatformStatusFix.cs");

        Assert.Contains("if (_libraryStableLayoutLocked)", source, StringComparison.Ordinal);
        Assert.Contains("_libraryStableLayoutLocked = true;", platformSource, StringComparison.Ordinal);
        Assert.Contains("ApplyQuizHistoryTableCleanup();", platformSource, StringComparison.Ordinal);
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
