namespace FactVaultManager.Desktop.Tests;

public sealed class FacebookBulkThumbnailUpdateTests
{
    [Fact]
    public void Candidate_RequiresPublishedFacebookShortWithSavedReelLink()
    {
        Assert.True(FacebookBulkThumbnailUpdatePlanner.IsCandidate(
            History(1, "9:16") with
            {
                PublishedOnFacebook = true,
                FacebookUrl = "https://www.facebook.com/reel/1051847137549312",
            }));

        Assert.False(FacebookBulkThumbnailUpdatePlanner.IsCandidate(
            History(2, "16:9") with
            {
                PublishedOnFacebook = true,
                FacebookUrl = "https://www.facebook.com/reel/1051847137549312",
            }));

        Assert.False(FacebookBulkThumbnailUpdatePlanner.IsCandidate(History(3, "9:16")));
    }

    [Fact]
    public void Resolve_ReturnsReelIdAndExistingThumbnail()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"factburst-fb-bulk-thumb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var thumbnail = Path.Combine(folder, "Thumbnail.png");
        File.WriteAllBytes(thumbnail, [137, 80, 78, 71]);
        try
        {
            var history = History(7, "9:16") with
            {
                ProjectFolder = folder,
                PublishedOnFacebook = true,
                FacebookUrl = "https://www.facebook.com/reel/1051847137549312",
            };

            var target = FacebookBulkThumbnailUpdatePlanner.Resolve(history);

            Assert.Equal(history.Id, target.HistoryId);
            Assert.Equal("1051847137549312", target.VideoId);
            Assert.Equal(thumbnail, target.ThumbnailPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsMissingShortThumbnailWithoutBlockingTheBatch()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"factburst-fb-bulk-thumb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var history = History(8, "9:16") with
            {
                ProjectFolder = folder,
                PublishedOnFacebook = true,
                FacebookUrl = "https://facebook.com/reels/1051847137549312/",
            };

            var error = Assert.Throws<FileNotFoundException>(() =>
                FacebookBulkThumbnailUpdatePlanner.Resolve(history));

            Assert.Contains("Thumbnail.png", error.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void PublishedRefreshPlan_SplitsYouTubeLongFormAndFacebookShorts()
    {
        var youtube = History(11, "16:9") with
        {
            PublishedOnYouTube = true,
            YouTubeUrl = "https://www.youtube.com/watch?v=video_123",
        };
        var facebook = History(12, "9:16") with
        {
            PublishedOnFacebook = true,
            FacebookUrl = "https://www.facebook.com/reel/1051847137549312",
        };
        var unpublished = History(13, "16:9");

        var plan = PublishedThumbnailRefreshPlan.Build([youtube, facebook, unpublished]);

        Assert.Equal(youtube.Id, Assert.Single(plan.YouTubeHistories).Id);
        Assert.Equal(facebook.Id, Assert.Single(plan.FacebookHistories).Id);
    }

    private static QuizHistorySummary History(int id, string format) => new(
        id,
        "Film Quiz",
        "2026-08-26",
        format == "16:9" ? 10 : 1,
        "Film",
        format,
        8,
        false,
        "C:\\Quiz",
        "Film Quiz",
        1,
        format == "16:9" ? "Can You Get 10/10? | Film Quiz #001" : "Can You Get 1/1? | Film Quiz #001",
        "Description",
        "#Quiz",
        "Pinned",
        false,
        "");
}
