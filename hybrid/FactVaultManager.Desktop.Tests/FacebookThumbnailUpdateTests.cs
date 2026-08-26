namespace FactVaultManager.Desktop.Tests;

public sealed class FacebookThumbnailUpdateTests
{
    [Fact]
    public void Target_ResolvesTheSavedPublishedFacebookReel()
    {
        var history = History("9:16") with
        {
            PublishedOnFacebook = true,
            FacebookUrl = "https://www.facebook.com/reel/1051847137549312",
        };

        var target = FacebookThumbnailUpdatePlanner.Resolve(history);

        Assert.Equal("1051847137549312", target.VideoId);
        Assert.Equal(history.FacebookUrl, target.Url);
    }

    [Fact]
    public void Target_RejectsLongFormVideosBecauseFacebookPublishingIsShortsOnly()
    {
        var history = History("16:9") with
        {
            PublishedOnFacebook = true,
            FacebookUrl = "https://www.facebook.com/reel/1051847137549312",
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            FacebookThumbnailUpdatePlanner.Resolve(history));

        Assert.Contains("Shorts only", error.Message);
    }

    [Fact]
    public void Target_RejectsAShortThatHasNotBeenPublishedToFacebook()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            FacebookThumbnailUpdatePlanner.Resolve(History("9:16")));

        Assert.Contains("Upload this Short to Facebook", error.Message);
    }

    [Fact]
    public void Target_RejectsASavedFacebookLinkWithoutAReelVideoId()
    {
        var history = History("9:16") with
        {
            PublishedOnFacebook = true,
            FacebookUrl = "https://www.facebook.com/FactburstQuiz",
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            FacebookThumbnailUpdatePlanner.Resolve(history));

        Assert.Contains("numeric Reel video ID", error.Message);
    }

    private static QuizHistorySummary History(string format) => new(
        1,
        "Film Quiz",
        "2026-08-26",
        1,
        "Film",
        format,
        8,
        false,
        "C:\\Quiz",
        "Film Quiz",
        1,
        "Can You Get 1/1? | Film Quiz #001",
        "Description",
        "#Quiz",
        "Pinned",
        false,
        "");
}
