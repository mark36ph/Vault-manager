using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotScheduleTargetTests
{
    [Theory]
    [InlineData(7, 7)]
    [InlineData(14, 14)]
    [InlineData(21, 21)]
    [InlineData(30, 30)]
    [InlineData(9, 14)]
    public void NormalizeTargetDays_UsesStableChoices(int input, int expected)
    {
        Assert.Equal(expected, AutopilotScheduleTargetPlanner.NormalizeTargetDays(input));
    }

    [Fact]
    public void MissingScheduledQuizzes_TwelveFutureQuizzesRefillsFourteenWithTwo()
    {
        var now = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.FromHours(1));
        var history = Enumerable.Range(1, 12)
            .Select(index => History(100 + index, now.AddDays(index).ToString("O")))
            .ToArray();

        Assert.Equal(12, AutopilotScheduleTargetPlanner.ScheduledQuizCount(history, now));
        var missing = AutopilotScheduleTargetPlanner.MissingScheduledQuizzes(history, 14, now);
        Assert.Equal(2, missing);
        Assert.Equal(2, AutopilotScheduleTargetPlanner.BatchSizeForMissingDays(missing));
    }

    [Fact]
    public void ScheduledQuizCount_IgnoresPastShortAndNotUploadedRecords()
    {
        var now = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.FromHours(1));
        var history = new[]
        {
            History(101, now.AddDays(1).ToString("O")),
            History(102, now.AddDays(-1).ToString("O")),
            History(103, now.AddDays(2).ToString("O"), format: "9:16"),
            History(104, now.AddDays(3).ToString("O"), published: false),
            History(105, "not-a-date"),
        };

        Assert.Equal(1, AutopilotScheduleTargetPlanner.ScheduledQuizCount(history, now));
        Assert.Equal(6, AutopilotScheduleTargetPlanner.MissingScheduledQuizzes(history, 7, now));
    }

    [Fact]
    public void ScheduledQuizCount_IsInventoryBasedRatherThanCalendarCoverage()
    {
        var now = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.FromHours(1));
        var sameDate = now.AddDays(2).Date.AddHours(9);
        var history = new[]
        {
            History(101, sameDate.ToString("O")),
            History(102, sameDate.AddMinutes(30).ToString("O")),
        };

        Assert.Equal(2, AutopilotScheduleTargetPlanner.ScheduledQuizCount(history, now));
        Assert.Equal(5, AutopilotScheduleTargetPlanner.MissingScheduledQuizzes(history, 7, now));
        Assert.Equal(5, AutopilotScheduleTargetPlanner.MissingScheduleDays(history, 7, now));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(7, 7)]
    [InlineData(30, 20)]
    public void BatchSizeForMissingDays_UsesExistingSafeBatchRange(int missing, int expected)
    {
        Assert.Equal(expected, AutopilotScheduleTargetPlanner.BatchSizeForMissingDays(missing));
    }

    [Fact]
    public void ShouldAutoFill_RespectsExplicitOffAndFillsAnyMissingInventoryWhenOn()
    {
        var preferences = new AutopilotSchedulePreferences { TargetDays = 14, AutoFillEnabled = false };
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 5, false, now));
        preferences.AutoFillEnabled = true;
        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 0, false, now));
        Assert.True(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 1, false, now));
        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 5, true, now));
        Assert.True(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 5, false, now));
    }

    [Fact]
    public void ShouldAutoFill_UsesShortDuplicateStartGuardThenRetriesPromptly()
    {
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var preferences = new AutopilotSchedulePreferences
        {
            AutoFillEnabled = true,
            LastAutomaticFillUtc = now.AddMinutes(-1),
        };
        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 4, false, now));
        Assert.True(AutopilotScheduleTargetPlanner.AutomaticFillRetryRemaining(preferences, now) > TimeSpan.Zero);

        preferences.LastAutomaticFillUtc = now.AddMinutes(-3);
        Assert.True(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 4, false, now));
        Assert.Equal(TimeSpan.Zero, AutopilotScheduleTargetPlanner.AutomaticFillRetryRemaining(preferences, now));
    }

    [Fact]
    public void CancelledAutomaticLaunch_ClearsArmedBatchAndTrustedPreflight()
    {
        AutopilotBatchCountRequest.Arm(2);
        AutopilotTrustedPublishingPreflight.Arm();

        AutopilotBatchCountRequest.Cancel();
        AutopilotTrustedPublishingPreflight.Cancel();

        Assert.False(AutopilotBatchCountRequest.TryConsume(out _));
        Assert.False(AutopilotTrustedPublishingPreflight.TryConsume());
    }

    [Fact]
    public void DefaultPreferences_KeepFourteenScheduledAndAutopilotOn()
    {
        var preferences = new AutopilotSchedulePreferences();
        Assert.Equal(14, preferences.TargetDays);
        Assert.True(preferences.AutoFillEnabled);
    }

    [Fact]
    public void Build147Source_UsesScheduledQuizInventoryAndPromptContinuousChecks()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotScheduleTarget.cs");
        Assert.Contains("AUTOPILOT ON", source, StringComparison.Ordinal);
        Assert.Contains("AUTOPILOT OFF", source, StringComparison.Ordinal);
        Assert.Contains("Keep at least ", source, StringComparison.Ordinal);
        Assert.Contains(" quizzes scheduled", source, StringComparison.Ordinal);
        Assert.Contains("MissingScheduledQuizzes", source, StringComparison.Ordinal);
        Assert.Contains("ScheduledQuizCount", source, StringComparison.Ordinal);
        Assert.Contains("GetFutureScheduledYouTubeQuizHistory", source, StringComparison.Ordinal);
        Assert.Contains("Fill schedule now", source, StringComparison.Ordinal);
        Assert.Contains("Generate + Schedule Quiz Batch", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotBatchCountRequest.TryConsume", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMinutes(1)", source, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(5))", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded += async", source, StringComparison.Ordinal);
        Assert.Contains("ApprovedYouTubeChannelId", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotTrustedPublishingPreflight.Arm", source, StringComparison.Ordinal);
        Assert.Contains("_fullAutopilotTimer?.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("_fullAutopilotTimer?.Start()", source, StringComparison.Ordinal);
        Assert.Contains("await RunFullAutopilotAsync()", source, StringComparison.Ordinal);
        Assert.Contains("retry within a minute", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build147Source_DoesNotLetRetiredRecoveryStateBlockScheduleTopup()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotScheduleTarget.cs");

        Assert.DoesNotContain("AutopilotRecoveryStateStore.Load", source, StringComparison.Ordinal);
        Assert.DoesNotContain("youtubeHealth?.State", source, StringComparison.Ordinal);
        Assert.Contains("trusted publishing preflight", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build147Source_DoesNotBurnRetryCooldownWhenProductionFailsToStart()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotScheduleTarget.cs");
        var raise = source.IndexOf("_quizAutopilotPrimaryButton.RaiseEvent", StringComparison.Ordinal);
        var startedCheck = source.IndexOf("productionStarted = _quizBatchAutomationRunning || _quizBatchRenderRunning", StringComparison.Ordinal);
        var failedGuard = source.IndexOf("if (!productionStarted)", startedCheck, StringComparison.Ordinal);
        var stamp = source.IndexOf("preferences.LastAutomaticFillUtc = DateTime.UtcNow", StringComparison.Ordinal);

        Assert.True(raise >= 0, "The tested production button should still be used.");
        Assert.True(startedCheck > raise, "The launch must verify that the batch pipeline actually started.");
        Assert.True(failedGuard > startedCheck, "A failed start must take the no-cooldown path.");
        Assert.True(stamp > failedGuard, "The retry cooldown must be stamped only after the successful-start guard.");
        Assert.Contains("AutopilotBatchCountRequest.Cancel()", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotTrustedPublishingPreflight.Cancel()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedPreflight_StillRequiresApprovedMatchingYouTubeBatch()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.SocialPublishingPreflight.cs");
        Assert.Contains("trustedAutopilotBatch", source, StringComparison.Ordinal);
        Assert.Contains("destinations == SocialUploadDestination.YouTube", source, StringComparison.Ordinal);
        Assert.Contains("itemCount >= 2", source, StringComparison.Ordinal);
        Assert.Contains("settings.ApprovedYouTubeChannelId.Length > 0", source, StringComparison.Ordinal);
        Assert.Contains("SocialPublishingAccountGuard.EnsureMatches", source, StringComparison.Ordinal);
    }

    private static QuizHistorySummary History(
        int id,
        string scheduledFor,
        string format = "16:9",
        bool published = true) =>
        new(
            Id: id,
            Title: "Science Quiz",
            Created: "2026-09-01 05:00:00",
            QuestionCount: 10,
            Categories: "Science",
            Format: format,
            QuestionSeconds: 8,
            ShuffleAnswers: true,
            ProjectFolder: $"C:/Factburst/Science-{id}",
            SeriesName: "Science Quiz",
            EpisodeNumber: id,
            YouTubeTitle: $"Science Quiz #{id}",
            YouTubeDescription: "Description",
            Hashtags: "#quiz",
            PinnedComment: "How many did you get right?",
            PublishedOnYouTube: published,
            YouTubeUrl: published ? $"https://www.youtube.com/watch?v=abcdefgh{id % 10:0}" : "",
            YouTubeFirstCommentId: "",
            YouTubePrivacy: published ? "private" : "",
            YouTubeScheduledFor: scheduledFor);

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
