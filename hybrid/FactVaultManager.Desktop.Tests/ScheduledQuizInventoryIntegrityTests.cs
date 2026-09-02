using System.Globalization;

namespace FactVaultManager.Desktop.Tests;

public sealed class ScheduledQuizInventoryIntegrityTests
{
    [Fact]
    public void LiveSync_PreservesKnownFutureSchedule_WhenPublishAtIsTemporarilyMissing()
    {
        var now = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        var saved = now.AddDays(4);

        var resolved = YouTubeScheduleIntegrity.ResolveScheduledFor(
            saved.ToString("O", CultureInfo.InvariantCulture),
            "private",
            livePublishAt: null,
            now);

        Assert.Equal(saved, resolved);
    }

    [Fact]
    public void LiveSync_UsesYouTubeSchedule_WhenPublishAtIsPresent()
    {
        var now = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        var saved = now.AddDays(4);
        var live = now.AddDays(6);

        var resolved = YouTubeScheduleIntegrity.ResolveScheduledFor(
            saved.ToString("O", CultureInfo.InvariantCulture),
            "private",
            live,
            now);

        Assert.Equal(live, resolved);
    }

    [Fact]
    public void LiveSync_ClearsSchedule_WhenYouTubeConfirmsPublic()
    {
        var now = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        var saved = now.AddDays(4);

        var resolved = YouTubeScheduleIntegrity.ResolveScheduledFor(
            saved.ToString("O", CultureInfo.InvariantCulture),
            "public",
            livePublishAt: null,
            now);

        Assert.Null(resolved);
    }

    [Fact]
    public void ScheduleInventory_QueryIsIndependentOfGenericHistoryWindow()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/ScheduledQuizInventory.cs");

        Assert.Contains("GetFutureScheduledYouTubeQuizHistory", source, StringComparison.Ordinal);
        Assert.Contains("TRIM(youtube_scheduled_for) <> ''", source, StringComparison.Ordinal);
        Assert.Contains("published_on_youtube = 1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetQuizHistory(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutopilotScheduleTarget_UsesDedicatedFutureScheduleInventory()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotScheduleTarget.cs");

        Assert.Contains("GetFutureScheduledYouTubeQuizHistory(now)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetQuizHistory(2_000)", source, StringComparison.Ordinal);
        Assert.Contains("_autopilotScheduledCountText.Text = scheduled.ToString", source, StringComparison.Ordinal);
    }

    [Fact]
    public void YouTubeStatusSync_DoesNotBlindlyReplaceSavedScheduleWithPublishAt()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.UploadManagerYouTubeStatusSync.cs");

        Assert.Contains("YouTubeScheduleIntegrity.ResolveScheduledFor", source, StringComparison.Ordinal);
        Assert.Contains("item.History.YouTubeScheduledFor", source, StringComparison.Ordinal);
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
