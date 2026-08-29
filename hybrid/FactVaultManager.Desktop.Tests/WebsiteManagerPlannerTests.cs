using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class WebsiteManagerPlannerTests
{
    [Fact]
    public void SummaryCountsLiveUpcomingQuestionsAndMissingSchedule()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var website = new[]
        {
            new FactburstWebsiteQuizSummary("quiz-a", "published", now.AddDays(-1).ToString("O"), now.ToString("O"), 10),
            new FactburstWebsiteQuizSummary("quiz-b", "published", now.AddDays(2).ToString("O"), now.ToString("O"), 12),
            new FactburstWebsiteQuizSummary("quiz-c", "draft", now.AddDays(-2).ToString("O"), now.ToString("O"), 8),
        };

        var summary = FactburstWebsiteManagerPlanner.Build(
            website,
            new[] { "quiz-a", "quiz-b", "quiz-missing" },
            now);

        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Live);
        Assert.Equal(1, summary.Upcoming);
        Assert.Equal(30, summary.Questions);
        Assert.Equal(3, summary.Scheduled);
        Assert.Equal(1, summary.MissingScheduled);
    }

    [Fact]
    public void DuplicateScheduledSlugsAreCountedOnce()
    {
        var summary = FactburstWebsiteManagerPlanner.Build(
            [new FactburstWebsiteQuizSummary("quiz-a", "published", "", "", 10)],
            new[] { "quiz-a", "QUIZ-A", "quiz-b" },
            DateTimeOffset.UtcNow);

        Assert.Equal(2, summary.Scheduled);
        Assert.Equal(1, summary.MissingScheduled);
    }
}
