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
    public void MissingScheduleDays_CountsOnlyUncoveredTargetDates()
    {
        var now = new DateTimeOffset(2026, 8, 29, 6, 0, 0, TimeSpan.FromHours(1));
        var history = new[]
        {
            History(101, now.AddDays(1).Date.AddHours(9).ToString("O")),
            History(102, now.AddDays(2).Date.AddHours(9).ToString("O")),
            History(103, now.AddDays(4).Date.AddHours(9).ToString("O")),
            History(104, now.AddDays(20).Date.AddHours(9).ToString("O")),
        };

        Assert.Equal(4, AutopilotScheduleTargetPlanner.MissingScheduleDays(history, 7, now));
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
    public void ShouldAutoFill_IsOptInAndRequiresAtLeastTwoMissingDays()
    {
        var preferences = new AutopilotSchedulePreferences { TargetDays = 14, AutoFillEnabled = false };
        var now = new DateTime(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);

        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 5, false, now));
        preferences.AutoFillEnabled = true;
        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 1, false, now));
        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 5, true, now));
        Assert.True(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 5, false, now));
    }

    [Fact]
    public void ShouldAutoFill_UsesTwentyMinuteDuplicateStartGuard()
    {
        var now = new DateTime(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);
        var preferences = new AutopilotSchedulePreferences
        {
            AutoFillEnabled = true,
            LastAutomaticFillUtc = now.AddMinutes(-10),
        };
        Assert.False(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 4, false, now));
        preferences.LastAutomaticFillUtc = now.AddMinutes(-21);
        Assert.True(AutopilotScheduleTargetPlanner.ShouldAutoFill(preferences, 4, false, now));
    }

    [Fact]
    public void DefaultPreferences_DoNotUnexpectedlyStartProductionAfterUpgrade()
    {
        var preferences = new AutopilotSchedulePreferences();
        Assert.Equal(14, preferences.TargetDays);
        Assert.False(preferences.AutoFillEnabled);
    }

    [Fact]
    public void Build47Source_AutoClosesCountDialogAndChecksScheduleEveryThirtyMinutes()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotScheduleTarget.cs");
        Assert.Contains("Generate + Fill Schedule", source, StringComparison.Ordinal);
        Assert.Contains("Generate + Schedule Quiz Batch", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotBatchCountRequest.TryConsume", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMinutes(30)", source, StringComparison.Ordinal);
        Assert.Contains("ApprovedYouTubeChannelId", source, StringComparison.Ordinal);
        Assert.Contains("AutopilotTrustedPublishingPreflight.Arm", source, StringComparison.Ordinal);
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

    private static QuizHistorySummary History(int id, string scheduledFor) =>
        new(
            Id: id,
            Title: "Science Quiz",
            Created: "2026-08-29 05:00:00",
            QuestionCount: 10,
            Categories: "Science",
            Format: "16:9",
            QuestionSeconds: 8,
            ShuffleAnswers: true,
            ProjectFolder: $"C:/Factburst/Science-{id}",
            SeriesName: "Science Quiz",
            EpisodeNumber: id,
            YouTubeTitle: $"Science Quiz #{id}",
            YouTubeDescription: "Description",
            Hashtags: "#quiz",
            PinnedComment: "How many did you get right?",
            PublishedOnYouTube: true,
            YouTubeUrl: $"https://www.youtube.com/watch?v=abcdefgh{id % 10:0}",
            YouTubeFirstCommentId: "",
            YouTubePrivacy: "private",
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
