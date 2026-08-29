using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotNeedsYouCountSyncTests
{
    [Fact]
    public void Total_CountsActualPendingItemsNotTaskCategories()
    {
        var total = AutopilotNeedsYouCountSummary.Total(
            relatedVideos: 1,
            instagramPromos: 8,
            packagingRescues: 0,
            viewerReplies: 12,
            releaseWarnings: 0);

        Assert.Equal(21, total);
    }

    [Fact]
    public void Total_IncludesEveryManualWorkItemType()
    {
        Assert.Equal(15, AutopilotNeedsYouCountSummary.Total(2, 3, 4, 5, 1));
    }

    [Fact]
    public void Total_DoesNotAllowNegativeCountsToReduceTheDisplay()
    {
        Assert.Equal(4, AutopilotNeedsYouCountSummary.Total(-2, 1, -1, 3, 0));
    }

    [Fact]
    public void Build59Source_WiresCountSyncAfterTaskQueueInitialization()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var queueIndex = buildInfo.IndexOf("InitializeAutopilotNeedsYouTaskQueue", StringComparison.Ordinal);
        var countIndex = buildInfo.IndexOf("InitializeAutopilotNeedsYouCountSync", StringComparison.Ordinal);

        Assert.True(queueIndex >= 0);
        Assert.True(countIndex > queueIndex);

        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");
        Assert.Contains("Actual pending tasks requiring your input", source, StringComparison.Ordinal);
        Assert.Contains("{total:N0} need you", source, StringComparison.Ordinal);
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
