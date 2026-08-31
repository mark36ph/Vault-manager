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

    [Theory]
    [InlineData(false, 0, "Healthy")]
    [InlineData(true, 0, "Working")]
    [InlineData(false, 3, "Needs you")]
    [InlineData(true, 3, "Needs you")]
    public void Health_KeepsNeedsYouStableWhileBackgroundWorkRuns(bool running, int total, string expected)
    {
        Assert.Equal(expected, AutopilotNeedsYouCountSummary.Health(running, total));
    }

    [Fact]
    public void FromAlignedTasks_MakesHomeGroupsAddUpToNeedsYouTotal()
    {
        var tasks = new[]
        {
            Task(AutopilotAlignedTaskKind.RelatedVideo, "related-1"),
            Task(AutopilotAlignedTaskKind.RelatedVideo, "related-2"),
            Task(AutopilotAlignedTaskKind.InstagramPromo, "instagram-1"),
            Task(AutopilotAlignedTaskKind.ViewerReply, "reply-1"),
            Task(AutopilotAlignedTaskKind.ReleaseWarning, "warning-1"),
        };

        var grouped = AutopilotNeedsYouCountSummary.FromAlignedTasks(tasks);

        Assert.Equal(5, grouped.Total);
        Assert.Equal(2, grouped.RelatedVideos);
        Assert.Equal(1, grouped.InstagramPromos);
        Assert.Equal(0, grouped.PackagingRescues);
        Assert.Equal(1, grouped.ViewerReplies);
        Assert.Equal(1, grouped.ReleaseWarnings);
        Assert.Equal(
            grouped.Total,
            grouped.RelatedVideos + grouped.InstagramPromos + grouped.PackagingRescues + grouped.ViewerReplies + grouped.ReleaseWarnings);
    }

    [Fact]
    public void NeedsCardRefresh_DoesNotRebuildUnchangedCards()
    {
        var summary = new AutopilotNeedsYouGroupedSummary(9, 9, 0, 0, 0, 0);

        Assert.True(AutopilotNeedsYouCountSummary.NeedsCardRefresh(null, summary));
        Assert.False(AutopilotNeedsYouCountSummary.NeedsCardRefresh(summary, summary));
        Assert.True(AutopilotNeedsYouCountSummary.NeedsCardRefresh(
            summary,
            summary with { Total = 8, RelatedVideos = 8 }));
    }

    [Fact]
    public void Build113Source_AvoidsRepaintingUnchangedNeedsYouText()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");

        Assert.Contains("SetAutopilotTextIfChanged(_autopilotNeedsText", source, StringComparison.Ordinal);
        Assert.Contains("SetAutopilotTextIfChanged(_autopilotHealthText, health)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureGuidedNeedsYouButton();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build76Source_RepairsCardsOverwrittenByLegacyHomeRefresh()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");

        Assert.Contains("RenderedAutopilotHomeTaskCardsMatch(cards)", source, StringComparison.Ordinal);
        Assert.Contains("summaryChanged || !visibleCardsMatch", source, StringComparison.Ordinal);
        Assert.Contains("Set Related Video on {grouped.RelatedVideos:N0}", source, StringComparison.Ordinal);
        Assert.Contains("SequenceEqual(expectedTitles, StringComparer.Ordinal)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build75Source_GuardsHomeCardRenderingAgainstOneSecondRebuilds()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");

        Assert.Contains("_autopilotNeedsYouRenderedSummary", source, StringComparison.Ordinal);
        Assert.Contains("NeedsCardRefresh(_autopilotNeedsYouRenderedSummary, grouped)", source, StringComparison.Ordinal);
        Assert.Contains("_autopilotNeedsYouRenderedSummary = grouped", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build74Source_RendersHomeTaskCardsFromSameAlignedSummaryAsCounter()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouCountSync.cs");

        Assert.Contains("FromAlignedTasks(tasks)", source, StringComparison.Ordinal);
        Assert.Contains("BuildAutopilotHomeTaskCards(grouped)", source, StringComparison.Ordinal);
        Assert.Contains("Set Related Video on {grouped.RelatedVideos:N0}", source, StringComparison.Ordinal);
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

    private static AutopilotAlignedTaskItem Task(AutopilotAlignedTaskKind kind, string key) =>
        new(
            kind,
            key,
            1,
            key,
            "detail",
            "pending",
            null,
            "",
            "",
            "",
            "",
            true);

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