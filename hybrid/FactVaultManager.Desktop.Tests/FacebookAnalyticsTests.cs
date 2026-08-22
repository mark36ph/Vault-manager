namespace FactVaultManager.Desktop.Tests;

public sealed class FacebookAnalyticsTests
{
    [Theory]
    [InlineData("https://www.facebook.com/reel/123456789", "123456789")]
    [InlineData("https://facebook.com/reels/123456789/", "123456789")]
    [InlineData("https://m.facebook.com/videos/123456789", "123456789")]
    public void ReelId_SupportsCanonicalFacebookLinks(string url, string expected)
    {
        Assert.Equal(expected, FacebookReelAnalyticsService.TryGetReelId(url));
    }

    [Theory]
    [InlineData("https://facebook.com/reel/not-a-number")]
    [InlineData("https://youtube.com/shorts/123456789")]
    [InlineData("not a link")]
    public void ReelId_RejectsUnsupportedLinks(string url)
    {
        Assert.Null(FacebookReelAnalyticsService.TryGetReelId(url));
    }

    [Theory]
    [InlineData("https://www.facebook.com/reel/123456789")]
    [InlineData("https://fb.watch/abc123/")]
    public void NormalizeUrl_AcceptsFacebookLinks(string url)
    {
        Assert.Equal(url, QuizFacebookPublication.NormalizeUrl($"  {url}  "));
    }

    [Fact]
    public void AnalyticsResponse_ParsesReelMetrics()
    {
        const string details = """
            {"id":"123456789","description":"Space Quiz","permalink_url":"https://www.facebook.com/reel/123456789","created_time":"2026-08-22T09:30:00+0000","reactions":{"summary":{"total_count":14}},"comments":{"summary":{"total_count":3}},"sharedposts":{"summary":{"total_count":2}}}
            """;
        const string insights = """
            {"data":[{"name":"total_video_views","values":[{"value":450}]}]}
            """;

        var result = FacebookReelAnalyticsService.Parse(details, insights);

        Assert.Equal("123456789", result.VideoId);
        Assert.Equal("Space Quiz", result.Description);
        Assert.Equal(450, result.Views);
        Assert.Equal(14, result.Reactions);
        Assert.Equal(3, result.Comments);
        Assert.Equal(2, result.Shares);
        Assert.NotNull(result.PublishedAt);
    }

    [Fact]
    public void NextShortRecommendation_UsesLeastPublishedFacebookCategory()
    {
        var recommendation = FacebookNextShortPlanner.Recommend(
        [
            History("Music Quiz", "Music", publishedOnFacebook: true),
            History("Space Quiz", "Space", publishedOnFacebook: true),
        ],
        ["Music", "Space", "Film"]);

        Assert.Equal("Film", recommendation.Category);
        Assert.Equal("Film Quiz — Short", recommendation.Display);
    }

    private static QuizHistorySummary History(string series, string categories, bool publishedOnFacebook) => new(
        1, "Quiz", "2026-08-22 12:00:00", 1, categories, "9:16", 8, false, "",
        series, 1, "", "", "", "", false, "", 0, 0, "",
        publishedOnFacebook, "https://www.facebook.com/reel/123456789");
}
