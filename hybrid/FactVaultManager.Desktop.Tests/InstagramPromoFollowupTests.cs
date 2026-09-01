using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstagramPromoFollowupTests
{
    [Fact]
    public void RecentPublishedLongForm_IsEligibleForAutomaticWindow()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(1));
        var history = History(now.AddHours(-4));

        Assert.True(InstagramPromoFollowupPlanner.IsWithinWindow(
            history,
            now,
            InstagramPromoFollowupPlanner.AutomaticWindow));
        Assert.True(InstagramPromoFollowupPlanner.IsWithinWindow(
            history,
            now,
            InstagramPromoFollowupPlanner.NeedsYouWindow));
    }

    [Fact]
    public void OldPublishedQuiz_IsVisibleOnlyOutsideAutomaticWindow()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(1));
        var history = History(now.AddDays(-3));

        Assert.False(InstagramPromoFollowupPlanner.IsWithinWindow(
            history,
            now,
            InstagramPromoFollowupPlanner.AutomaticWindow));
        Assert.True(InstagramPromoFollowupPlanner.IsWithinWindow(
            history,
            now,
            InstagramPromoFollowupPlanner.NeedsYouWindow));
    }

    [Fact]
    public void FailedInstagramAttempt_UsesRetryCooldown()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var recent = Entry(now.AddMinutes(-10));
        var older = Entry(now.AddMinutes(-31));

        Assert.False(InstagramPromoFollowupPlanner.RetryAllowed(recent, now));
        Assert.True(InstagramPromoFollowupPlanner.RetryAllowed(older, now));
    }

    [Fact]
    public void AutopilotSource_VerifiesYouTubePublicAndUsesApprovedInstagramDestination()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.InstagramPromoFollowup.cs");

        Assert.Contains("remote.PrivacyStatus, \"public\"", source, StringComparison.Ordinal);
        Assert.Contains("ApprovedFacebookPageId", source, StringComparison.Ordinal);
        Assert.Contains("SocialPublishingAccountGuard.EnsureMatches", source, StringComparison.Ordinal);
        Assert.Contains("_instagramReelUpload.UploadReelAsync", source, StringComparison.Ordinal);
        Assert.Contains("PublicationContentKind.Promo", source, StringComparison.Ordinal);
        Assert.Contains("autopilot-instagram-promo", source, StringComparison.Ordinal);
        Assert.Contains("Start next task", source, StringComparison.Ordinal);
    }

    private static PublicationStateEntry Entry(DateTimeOffset lastAttempt) =>
        new(
            10,
            PublicationPlatform.Instagram,
            PublicationContentKind.Promo,
            PublicationStateStatus.Failed,
            "",
            "",
            "",
            "",
            "",
            "upload",
            "temporary failure",
            lastAttempt.ToString("O"),
            "test",
            lastAttempt.ToString("O"));

    private static QuizHistorySummary History(DateTimeOffset publishedAt) =>
        new(
            Id: 10,
            Title: "Science Quiz",
            Created: "2026-09-01 05:00:00",
            QuestionCount: 10,
            Categories: "Science",
            Format: "16:9",
            QuestionSeconds: 8,
            ShuffleAnswers: true,
            ProjectFolder: "C:/Factburst/Science-10",
            SeriesName: "Science Quiz",
            EpisodeNumber: 10,
            YouTubeTitle: "Science Quiz #010",
            YouTubeDescription: "Description",
            Hashtags: "#quiz",
            PinnedComment: "How many did you get right?",
            PublishedOnYouTube: true,
            YouTubeUrl: "https://www.youtube.com/watch?v=abcdefghij0",
            YouTubePrivacy: "private",
            YouTubeScheduledFor: publishedAt.ToString("O"));

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
