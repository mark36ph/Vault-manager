namespace FactVaultManager.Desktop.Tests;

public sealed class FacebookAnalyticsTests
{
    [Fact]
    public async Task PageDiscovery_UsesVideoReelsEdge()
    {
        var handler = new FacebookGraphHandler();
        var service = new FacebookReelAnalyticsService(new HttpClient(handler));

        var page = await service.ListPageVideosAsync("page-token");

        Assert.Equal("123", page.PageId);
        Assert.Equal("Quiz Page", page.PageName);
        Assert.Single(page.Videos);
        Assert.Equal("456", page.Videos[0].VideoId);
        Assert.Contains(handler.Paths, path => path.Contains("/123/video_reels", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/123/videos", StringComparison.Ordinal));
    }

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
            {"id":"123456789","description":"Space Quiz","permalink_url":"https://www.facebook.com/reel/123456789","created_time":"2026-08-22T09:30:00+0000"}
            """;
        const string insights = """
            {"data":[{"name":"total_video_views","values":[{"value":450}]}]}
            """;

        var result = FacebookReelAnalyticsService.Parse(details, insights);

        Assert.Equal("123456789", result.VideoId);
        Assert.Equal("Space Quiz", result.Description);
        Assert.Equal(450, result.Views);
        Assert.Equal(0, result.Reactions);
        Assert.Equal(0, result.Comments);
        Assert.Equal(0, result.Shares);
        Assert.NotNull(result.PublishedAt);
    }

    [Fact]
    public void EdgeSummaryResponse_ParsesTotalCount()
    {
        Assert.Equal(14, FacebookReelAnalyticsService.ParseEdgeSummaryCount(
            """{"data":[],"summary":{"total_count":14}}"""));
    }

    [Fact]
    public void ShortMatcher_MatchesFacebookDescriptionToYouTubeTitle()
    {
        var history = History("Space Quiz", "Space", publishedOnFacebook: false) with
        {
            Id = 7,
            YouTubeTitle = "The Eiffel Tower Gets Taller Every Summer",
            FacebookUrl = "",
        };
        var videos = new[]
        {
            new FacebookPageVideo("111", "", "The Eiffel Tower Gets Taller Every Summer #facts", "https://www.facebook.com/reel/111", DateTime.UtcNow),
            new FacebookPageVideo("222", "Ocean facts", "A completely different quiz", "https://www.facebook.com/reel/222", DateTime.UtcNow),
        };

        var matches = FacebookShortMatcher.Match([history], videos);

        Assert.Equal("111", matches[7].VideoId);
    }

    [Fact]
    public void ShortMatcher_DoesNotGuessWhenTitlesDoNotMatch()
    {
        var history = History("Space Quiz", "Space", publishedOnFacebook: false) with
        {
            Id = 8,
            YouTubeTitle = "The Eiffel Tower Gets Taller Every Summer",
            FacebookUrl = "",
        };
        var videos = new[]
        {
            new FacebookPageVideo("222", "Ocean facts", "A completely different quiz", "https://www.facebook.com/reel/222", DateTime.UtcNow),
        };

        Assert.Empty(FacebookShortMatcher.Match([history], videos));
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

    private sealed class FacebookGraphHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            Paths.Add(path);
            var json = path.EndsWith("/me", StringComparison.Ordinal)
                ? """{"id":"123","name":"Quiz Page"}"""
                : """{"data":[{"id":"456","title":"Science Quiz","description":"Can You Get 1/1?","permalink_url":"https://www.facebook.com/reel/456","created_time":"2026-08-22T09:30:00+0000"}]}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
        }
    }
}
