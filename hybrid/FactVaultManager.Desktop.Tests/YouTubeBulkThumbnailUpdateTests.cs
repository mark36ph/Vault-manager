namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeBulkThumbnailUpdateTests
{
    [Fact]
    public void Candidate_RequiresPublishedLongFormYouTubeVideo()
    {
        Assert.True(YouTubeBulkThumbnailUpdatePlanner.IsCandidate(
            History("16:9") with
            {
                PublishedOnYouTube = true,
                YouTubeUrl = "https://www.youtube.com/watch?v=video_123",
            }));

        Assert.False(YouTubeBulkThumbnailUpdatePlanner.IsCandidate(
            History("9:16") with
            {
                PublishedOnYouTube = true,
                YouTubeUrl = "https://www.youtube.com/shorts/video_123",
            }));

        Assert.False(YouTubeBulkThumbnailUpdatePlanner.IsCandidate(History("16:9")));
    }

    [Fact]
    public void Resolve_ReturnsVideoIdAndExistingThumbnail()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"factburst-bulk-thumb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var thumbnail = Path.Combine(folder, "Thumbnail.png");
        File.WriteAllBytes(thumbnail, [137, 80, 78, 71]);
        try
        {
            var history = History("16:9") with
            {
                ProjectFolder = folder,
                PublishedOnYouTube = true,
                YouTubeUrl = "https://www.youtube.com/watch?v=video_123",
            };

            var target = YouTubeBulkThumbnailUpdatePlanner.Resolve(history);

            Assert.Equal(history.Id, target.HistoryId);
            Assert.Equal("video_123", target.VideoId);
            Assert.Equal(thumbnail, target.ThumbnailPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsMissingThumbnailWithoutStoppingOtherBatchItems()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"factburst-bulk-thumb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var history = History("16:9") with
            {
                ProjectFolder = folder,
                PublishedOnYouTube = true,
                YouTubeUrl = "https://youtu.be/video_123",
            };

            var error = Assert.Throws<FileNotFoundException>(() =>
                YouTubeBulkThumbnailUpdatePlanner.Resolve(history));

            Assert.Contains("Thumbnail.png", error.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static QuizHistorySummary History(string format) => new(
        1,
        "Film Quiz",
        "2026-08-26",
        10,
        "Film",
        format,
        8,
        false,
        "C:\\Quiz",
        "Film Quiz",
        1,
        "Can You Get 10/10? | Film Quiz #001",
        "Description",
        "#Quiz",
        "Pinned",
        false,
        "");
}
