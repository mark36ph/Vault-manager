using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class ScheduledReleaseReadinessTests
{
    [Fact]
    public void Build_only_includes_future_scheduled_long_form_quizzes()
    {
        var now = new DateTimeOffset(2026, 8, 27, 15, 0, 0, TimeSpan.FromHours(1));
        using var folders = new TemporaryFolders();
        var future = History(1, folders.Create("future"), now.AddDays(2).ToString("O"));
        var earlier = History(2, folders.Create("earlier"), now.AddDays(1).ToString("O"));
        var past = History(3, folders.Create("past"), now.AddMinutes(-1).ToString("O"));
        var shortVideo = History(4, folders.Create("short"), now.AddDays(1).ToString("O"), format: "9:16");
        var notUploaded = History(5, folders.Create("not-uploaded"), now.AddDays(1).ToString("O"), publishedOnYouTube: false);

        var rows = ScheduledReleaseReadinessPlanner.Build(
            [future, past, shortVideo, notUploaded, earlier],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            trackerConfigured: true,
            now);

        Assert.Collection(
            rows,
            row => Assert.Equal(2, row.HistoryId),
            row => Assert.Equal(1, row.HistoryId));
    }

    [Fact]
    public void Build_reports_missing_preparation_and_next_action()
    {
        var now = new DateTimeOffset(2026, 8, 27, 15, 0, 0, TimeSpan.FromHours(1));
        using var folders = new TemporaryFolders();
        var history = History(7, folders.Create("missing"), now.AddDays(1).ToString("O"));

        var row = Assert.Single(ScheduledReleaseReadinessPlanner.Build(
            [history],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            trackerConfigured: true,
            now));

        Assert.Equal("Scheduled", row.FullQuiz);
        Assert.Equal("Missing", row.Package);
        Assert.Equal("Missing", row.Promo);
        Assert.Equal("Missing", row.Tracking);
        Assert.Equal("Missing", row.YouTubePromo);
        Assert.Equal("Missing", row.FacebookPromo);
        Assert.Equal("Waiting", row.InstagramPromo);
        Assert.Equal("Waiting", row.RelatedVideo);
        Assert.Equal("Prepared", row.FirstComment);
        Assert.Equal(2, row.ReadyCount);
        Assert.Equal(8, row.TotalChecks);
        Assert.Equal(8, ScheduledReleaseReadinessPlanner.CheckCount);
        Assert.Equal("Create YouTube package", row.NextAction);
    }

    [Fact]
    public void Build_distinguishes_tracker_unavailable_from_missing_campaign()
    {
        var now = new DateTimeOffset(2026, 8, 27, 15, 0, 0, TimeSpan.FromHours(1));
        using var folders = new TemporaryFolders();
        var history = History(8, folders.Create("tracker"), now.AddDays(1).ToString("O"));

        var unavailable = Assert.Single(ScheduledReleaseReadinessPlanner.Build(
            [history],
            trackerCampaigns: null,
            trackerConfigured: true,
            now));
        var notConfigured = Assert.Single(ScheduledReleaseReadinessPlanner.Build(
            [history],
            trackerCampaigns: null,
            trackerConfigured: false,
            now));

        Assert.Equal("Unavailable", unavailable.Tracking);
        Assert.Equal("Not configured", notConfigured.Tracking);
    }

    [Theory]
    [InlineData(8, 8, "8/8 • Ready")]
    [InlineData(7, 8, "7/8 • Nearly ready")]
    [InlineData(6, 8, "6/8 • Nearly ready")]
    [InlineData(5, 8, "5/8 • Needs work")]
    public void ReadinessLabel_summarizes_progress(int ready, int total, string expected)
    {
        Assert.Equal(expected, ScheduledReleaseReadinessPlanner.ReadinessLabel(ready, total));
    }

    private static QuizHistorySummary History(
        int id,
        string folder,
        string schedule,
        string format = "16:9",
        bool publishedOnYouTube = true) =>
        new(
            Id: id,
            Title: $"Quiz {id}",
            Created: "2026-08-27 10:00:00",
            QuestionCount: 10,
            Categories: "Science",
            Format: format,
            QuestionSeconds: 8,
            ShuffleAnswers: false,
            ProjectFolder: folder,
            SeriesName: "Science Quiz",
            EpisodeNumber: id,
            YouTubeTitle: $"Science Quiz #{id:000}",
            YouTubeDescription: "Description",
            Hashtags: "#Science",
            PinnedComment: "What score did you get?",
            PublishedOnYouTube: publishedOnYouTube,
            YouTubeUrl: "https://www.youtube.com/watch?v=abcdefghijk",
            YouTubePrivacy: "private",
            YouTubeScheduledFor: schedule);

    private sealed class TemporaryFolders : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FactVaultManager-Readiness-" + Guid.NewGuid().ToString("N"));

        public string Create(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
