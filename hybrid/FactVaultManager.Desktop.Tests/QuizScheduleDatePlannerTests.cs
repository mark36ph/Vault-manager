using System.Globalization;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizScheduleDatePlannerTests
{
    [Fact]
    public void FindNextOpenDate_ReturnsStartDate_WhenItIsFree()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTime(2026, 8, 28);
        var histories = new[]
        {
            History(youtube: Schedule(now, 2026, 8, 29, 9)),
        };

        var result = QuizScheduleDatePlanner.FindNextOpenDate(histories, start, now);

        Assert.Equal(start, result);
    }

    [Fact]
    public void FindNextOpenDate_SkipsDatesScheduledOnYouTubeOrFacebook()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTime(2026, 8, 28);
        var histories = new[]
        {
            History(youtube: Schedule(now, 2026, 8, 28, 9)),
            History(facebook: Schedule(now, 2026, 8, 29, 9)),
            History(youtube: Schedule(now, 2026, 8, 30, 9)),
        };

        var result = QuizScheduleDatePlanner.FindNextOpenDate(histories, start, now);

        Assert.Equal(new DateTime(2026, 8, 31), result);
    }

    [Fact]
    public void FindNextOpenDate_UsesFirstGapInsteadOfDateAfterLatestSchedule()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTime(2026, 8, 28);
        var histories = new[]
        {
            History(youtube: Schedule(now, 2026, 8, 28, 9)),
            History(youtube: Schedule(now, 2026, 8, 30, 9)),
        };

        var result = QuizScheduleDatePlanner.FindNextOpenDate(histories, start, now);

        Assert.Equal(new DateTime(2026, 8, 29), result);
    }

    [Fact]
    public void FindNextOpenDate_IgnoresSchedulesThatAreAlreadyPast()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTime(2026, 8, 28);
        var histories = new[]
        {
            History(youtube: now.AddHours(-2).ToString("O", CultureInfo.InvariantCulture)),
        };

        var result = QuizScheduleDatePlanner.FindNextOpenDate(histories, start, now);

        Assert.Equal(start, result);
    }

    private static QuizHistorySummary History(string youtube = "", string facebook = "") =>
        new(
            Id: 1,
            Title: "Quiz",
            Created: "",
            QuestionCount: 10,
            Categories: "General Knowledge",
            Format: "16:9",
            QuestionSeconds: 10,
            ShuffleAnswers: false,
            ProjectFolder: "",
            SeriesName: "General Knowledge Quiz",
            EpisodeNumber: 1,
            YouTubeTitle: "Quiz",
            YouTubeDescription: "",
            Hashtags: "",
            PinnedComment: "",
            PublishedOnYouTube: false,
            YouTubeUrl: "",
            YouTubeScheduledFor: youtube,
            FacebookScheduledFor: facebook);

    private static string Schedule(DateTimeOffset now, int year, int month, int day, int hour) =>
        new DateTimeOffset(year, month, day, hour, 0, 0, now.Offset)
            .ToString("O", CultureInfo.InvariantCulture);
}
