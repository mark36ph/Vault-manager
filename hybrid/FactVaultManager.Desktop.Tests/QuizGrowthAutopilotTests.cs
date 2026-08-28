using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizGrowthAutopilotTests
{
    [Fact]
    public void FindExisting_ReusesCanonicalAndLegacyCategoryPlaylists()
    {
        var playlists = new[]
        {
            new YouTubePlaylistItem("science", "Science Quizzes", "", "public", 3),
            new YouTubePlaylistItem("history", "History Quiz", "", "public", 2),
        };

        Assert.Equal("science", QuizGrowthPlaylistPlanner.FindExisting("Science", playlists)!.Id);
        Assert.Equal("history", QuizGrowthPlaylistPlanner.FindExisting("History", playlists)!.Id);
        Assert.Null(QuizGrowthPlaylistPlanner.FindExisting("Space", playlists));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123", "abc123")]
    [InlineData("https://youtu.be/xyz987", "xyz987")]
    [InlineData("https://www.youtube.com/shorts/short42", "short42")]
    [InlineData("https://example.com/watch?v=nope", "")]
    public void VideoId_ParsesSupportedYouTubeUrls(string url, string expected)
    {
        Assert.Equal(expected, QuizGrowthPlaylistPlanner.VideoId(url));
    }

    [Fact]
    public void GrowthEndScreen_ReservesFifteenSeconds()
    {
        Assert.Equal(15.0, QuizGrowthEndScreen.SafeSeconds);
    }
}
