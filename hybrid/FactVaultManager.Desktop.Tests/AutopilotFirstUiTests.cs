using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotFirstUiTests
{
    [Theory]
    [InlineData(false, 0, "Healthy")]
    [InlineData(true, 0, "Working")]
    [InlineData(false, 2, "Needs you")]
    public void Health_UsesSimpleAutopilotStates(bool running, int manualTasks, string expected)
    {
        Assert.Equal(expected, AutopilotHomePlanner.Health(running, manualTasks));
    }

    [Fact]
    public void ScheduleCoverageDays_CountsThroughLatestFutureRelease()
    {
        var now = new DateTimeOffset(2026, 8, 29, 6, 0, 0, TimeSpan.FromHours(1));
        var releases = new[]
        {
            now.AddHours(3),
            now.AddDays(3).AddHours(3),
            now.AddDays(6).AddHours(3),
        };

        Assert.Equal(7, AutopilotHomePlanner.ScheduleCoverageDays(releases, now));
    }

    [Fact]
    public void ScheduleCoverageDays_ReturnsZeroWhenNothingFutureIsQueued()
    {
        var now = new DateTimeOffset(2026, 8, 29, 6, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(0, AutopilotHomePlanner.ScheduleCoverageDays(new[] { now.AddDays(-1) }, now));
    }

    [Theory]
    [InlineData("Ready", false)]
    [InlineData("N/A", false)]
    [InlineData("Scheduled", false)]
    [InlineData("Automatic", false)]
    [InlineData("Missing", true)]
    [InlineData("Needs attention", true)]
    [InlineData("Manual", true)]
    public void NeedsManualValue_OnlyFlagsOutstandingManualStates(string value, bool expected)
    {
        Assert.Equal(expected, AutopilotHomePlanner.NeedsManualValue(value));
    }

    [Fact]
    public void DueInstagramPromoCount_OnlyCountsNextDayPromosOncePostingTimeArrives()
    {
        var publishAt = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
        var due = AutopilotNeedsYouTaskPlanner.PromoDueAt(publishAt);
        var rows = new[]
        {
            Row(41, publishAt, "Ready promo", "Next day"),
            Row(42, publishAt, "Waiting promo", "Waiting"),
            Row(43, publishAt, "Already uploaded", "Uploaded"),
        };

        Assert.Equal(0, AutopilotHomePlanner.DueInstagramPromoCount(rows, due.AddMinutes(-1)));
        Assert.Equal(1, AutopilotHomePlanner.DueInstagramPromoCount(rows, due));
        Assert.Equal(1, AutopilotHomePlanner.DueInstagramPromoCount(rows, due.AddDays(2)));
    }

    [Fact]
    public void Build45Source_ContainsCompactDailyNavigationAndAdvancedFallback()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotFirstUi.cs");
        Assert.Contains("Factburst Autopilot", source, StringComparison.Ordinal);
        Assert.Contains("Generate + Fill Schedule", source, StringComparison.Ordinal);
        Assert.Contains("✦   Autopilot", source, StringComparison.Ordinal);
        Assert.Contains("+   Create", source, StringComparison.Ordinal);
        Assert.Contains("↗   Performance", source, StringComparison.Ordinal);
        Assert.Contains("▤   Library", source, StringComparison.Ordinal);
        Assert.Contains("⋯   Advanced", source, StringComparison.Ordinal);
        Assert.Contains("The detailed tools stay out of the way unless you need them", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeLaunchGuard_AlwaysSelectsExportBeforeStartingAutopilot()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotHomeLaunchGuard.cs");
        Assert.Contains("Generate + Fill Schedule", source, StringComparison.Ordinal);
        Assert.Contains("NavigateLegacy(\"Quizzes\", \"Create\")", source, StringComparison.Ordinal);
        Assert.Contains("SelectQuizWorkspacePage(\"export\")", source, StringComparison.Ordinal);
    }

    private static ScheduledReleaseReadinessRow Row(
        int historyId,
        DateTimeOffset publishAt,
        string quiz,
        string instagramPromo) =>
        new(
            historyId,
            publishAt,
            publishAt.ToString("O"),
            quiz,
            "General Knowledge",
            "Scheduled",
            "Ready",
            "Ready",
            "Ready",
            "Uploaded",
            "Uploaded",
            instagramPromo,
            "Set",
            "Prepared",
            8,
            8,
            "8/8 • Ready",
            "Ready for release",
            $"C:\\Projects\\{historyId}");

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
