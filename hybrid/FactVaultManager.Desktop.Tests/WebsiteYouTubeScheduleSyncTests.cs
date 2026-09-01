using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteYouTubeScheduleSyncTests
{
    [Fact]
    public void ScheduledYouTubeReleaseWinsOverCallerWebsiteTime()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var scheduled = now.AddDays(3);
        var history = History(
            privacy: "private",
            scheduledFor: scheduled.ToString("O"));

        var plan = WebsiteYouTubeSchedulePlanner.Plan(history, now, now.AddMinutes(5));
        var resolved = WebsiteYouTubeSchedulePlanner.ResolvePublishAtOrThrow(
            history,
            now.AddMinutes(5),
            now);

        Assert.NotNull(plan);
        Assert.True(plan!.IsScheduled);
        Assert.Equal(scheduled, plan.PublishAt);
        Assert.Equal(scheduled, resolved);
    }

    [Fact]
    public void PublicYouTubeVideoIsLiveAndUsesItsUploadTime()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var uploaded = now.AddHours(-4);
        var history = History(
            privacy: "public",
            uploadDate: uploaded.ToString("O"));

        var plan = WebsiteYouTubeSchedulePlanner.Plan(history, now);

        Assert.NotNull(plan);
        Assert.True(plan!.IsLive);
        Assert.Equal(uploaded, plan.PublishAt);
    }

    [Fact]
    public void PastScheduledReleaseIsTreatedAsLiveButKeepsExactReleaseTime()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var scheduled = now.AddMinutes(-30);
        var history = History(
            privacy: "private",
            scheduledFor: scheduled.ToString("O"));

        var plan = WebsiteYouTubeSchedulePlanner.Plan(history, now);

        Assert.NotNull(plan);
        Assert.True(plan!.IsLive);
        Assert.Equal(scheduled, plan.PublishAt);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("unlisted")]
    public void PrivateOrUnlistedUnscheduledUploadCannotPublishWebsiteEarly(string privacy)
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var history = History(privacy: privacy);

        Assert.Null(WebsiteYouTubeSchedulePlanner.Plan(history, now));
        var error = Assert.Throws<InvalidDataException>(() =>
            WebsiteYouTubeSchedulePlanner.ResolvePublishAtOrThrow(history, now, now));
        Assert.Contains("waiting for its long-form YouTube release", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(WebsiteYouTubeSchedulePlanner.IsKnownNotPublic(history));
    }

    [Fact]
    public void ShortsAreNeverWebsiteScheduleTargets()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var history = History(
            privacy: "public",
            format: "9:16",
            uploadDate: now.AddHours(-1).ToString("O"));

        Assert.Null(WebsiteYouTubeSchedulePlanner.Plan(history, now));
    }

    [Fact]
    public void WebsiteTimingComparisonUsesOneSecondTolerance()
    {
        var expected = new DateTimeOffset(2026, 9, 4, 18, 30, 0, TimeSpan.Zero);

        Assert.True(WebsiteYouTubeSchedulePlanner.PublishTimesMatch(
            expected.AddMilliseconds(500).ToString("O"),
            expected));
        Assert.False(WebsiteYouTubeSchedulePlanner.PublishTimesMatch(
            expected.AddSeconds(2).ToString("O"),
            expected));
    }

    [Fact]
    public void Build130WiresAutomaticReconciliationAndExactScheduledSync()
    {
        var build = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var builder = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/FactburstWebsitePublishing.cs");
        var scheduled = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ScheduledWebsitePublishing.cs");
        var automatic = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.WebsiteYouTubeScheduleSync.cs");

        Assert.Contains("CurrentBuildNumber = 130", build, StringComparison.Ordinal);
        Assert.Contains("InitializeWebsiteYouTubeScheduleSync();", build, StringComparison.Ordinal);
        Assert.Contains("ResolvePublishAtOrThrow", builder, StringComparison.Ordinal);
        Assert.Contains("FollowScheduleAsync", scheduled, StringComparison.Ordinal);
        Assert.Contains("website-live-reconcile", automatic, StringComparison.Ordinal);
        Assert.Contains("AutoFillEnabled", automatic, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(5)", automatic, StringComparison.Ordinal);
    }

    private static QuizHistorySummary History(
        string privacy,
        string scheduledFor = "",
        string uploadDate = "",
        string format = "16:9",
        bool published = true,
        string url = "https://www.youtube.com/watch?v=abc123") =>
        new(
            Id: 1,
            Title: "Quiz",
            Created: "2026-09-01 10:00:00",
            QuestionCount: 10,
            Categories: "Science",
            Format: format,
            QuestionSeconds: 10,
            ShuffleAnswers: false,
            ProjectFolder: "C:\\Factburst\\Quiz",
            SeriesName: "",
            EpisodeNumber: 1,
            YouTubeTitle: "Quiz",
            YouTubeDescription: "",
            Hashtags: "",
            PinnedComment: "",
            PublishedOnYouTube: published,
            YouTubeUrl: url,
            YouTubeUploadDate: uploadDate,
            YouTubePrivacy: privacy,
            YouTubeScheduledFor: scheduledFor);

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
