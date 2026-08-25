namespace FactVaultManager.Desktop.Tests;

public sealed class QuizHistoryPublicationTests
{
    [Theory]
    [InlineData("2026-08-19 10:36:35", "19-08-2026")]
    [InlineData("2026-08-19 10:36", "19-08-2026")]
    [InlineData("2026-08-19", "19-08-2026")]
    public void HistoryDate_UsesDayMonthYearDisplay(string stored, string expected)
    {
        Assert.Equal(expected, QuizHistoryDate.Format(stored));
    }

    [Theory]
    [InlineData("9:16", "Short")]
    [InlineData("16:9", "Video")]
    [InlineData("", "Video")]
    public void HistoryVideoType_UsesFriendlyLabels(string format, string expected)
    {
        Assert.Equal(expected, QuizHistoryVideoType.DisplayName(format));
    }

    [Fact]
    public void HistoryStatistics_CountsVideosShortsAndQuestionUses()
    {
        var statistics = QuizHistoryStatistics.Calculate(
        [
            History("16:9", 10, published: true, views: 120, likes: 8, series: "Music Quiz"),
            History("9:16", 1, published: true, views: 30, likes: 2, series: "Space Quiz"),
            History("9:16", 1, views: 999, likes: 999, series: "History Quiz"),
        ]);

        Assert.Equal(1, statistics.Videos);
        Assert.Equal(2, statistics.Shorts);
        Assert.Equal(2, statistics.Published);
        Assert.Equal(12, statistics.QuestionsUsed);
        Assert.Equal(150, statistics.Views);
        Assert.Equal(10, statistics.Likes);
        Assert.Equal("Music", statistics.TopCategory);
    }

    [Fact]
    public void HistoryStatistics_DoesNotCountFutureSchedulesAsPublished()
    {
        var scheduled = History("9:16", 1, published: true) with
        {
            YouTubeScheduledFor = DateTimeOffset.Now.AddHours(1).ToString("O"),
        };

        Assert.Equal(0, QuizHistoryStatistics.Calculate([scheduled]).Published);
    }

    [Theory]
    [InlineData("https://youtu.be/qJAMsHFhlDA")]
    [InlineData("https://www.youtube.com/watch?v=qJAMsHFhlDA")]
    [InlineData("https://m.youtube.com/watch?v=qJAMsHFhlDA")]
    public void NormalizeUrl_AcceptsYouTubeVideoLinks(string value)
    {
        Assert.Equal(value, QuizYouTubePublication.NormalizeUrl($"  {value}  "));
    }

    [Theory]
    [InlineData("http://youtu.be/qJAMsHFhlDA")]
    [InlineData("https://vimeo.com/123")]
    [InlineData("https://youtube.com.evil.example/watch?v=123")]
    [InlineData("not a link")]
    public void NormalizeUrl_RejectsNonYouTubeOrInsecureLinks(string value)
    {
        Assert.Throws<ArgumentException>(() => QuizYouTubePublication.NormalizeUrl(value));
    }

    [Fact]
    public void NormalizeUrl_AllowsPublicationWithoutLink()
    {
        Assert.Equal("", QuizYouTubePublication.NormalizeUrl("  "));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1,234", 1234)]
    public void AnalyticsMetric_AcceptsNonNegativeWholeNumbers(string value, long expected)
    {
        Assert.Equal(expected, QuizYouTubeAnalytics.ParseMetric(value, "Views"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("unknown")]
    public void AnalyticsMetric_RejectsInvalidValues(string value)
    {
        Assert.Throws<ArgumentException>(() => QuizYouTubeAnalytics.ParseMetric(value, "Views"));
    }

    [Fact]
    public void UploadDate_StoresIsoAndDisplaysDayMonthYear()
    {
        var date = new DateTime(2026, 8, 22);

        Assert.Equal("2026-08-22", QuizYouTubeAnalytics.NormalizeUploadDate(date));
        Assert.Equal("22-08-2026", QuizYouTubeAnalytics.FormatUploadDate("2026-08-22"));
    }

    [Fact]
    public void PublicationDisplay_DistinguishesScheduledPrivateAndPublishedVideos()
    {
        var scheduled = History("9:16", 1, published: true) with
        {
            YouTubePrivacy = "private",
            YouTubeScheduledFor = DateTimeOffset.Now.AddHours(2).ToString("O"),
        };
        var privateVideo = History("9:16", 1, published: true) with { YouTubePrivacy = "private" };
        var publicVideo = History("9:16", 1, published: true) with { YouTubePrivacy = "public" };

        Assert.StartsWith("Scheduled ", scheduled.YouTubePublicationDisplay);
        Assert.Equal("Private", privateVideo.YouTubePublicationDisplay);
        Assert.Equal("Published", publicVideo.YouTubePublicationDisplay);
    }

    [Fact]
    public void FullVideos_MarkShortOnlyPlatformsAsNotApplicable()
    {
        var video = History("16:9", 10) with
        {
            FacebookUrl = "https://www.facebook.com/reel/123",
            InstagramUrl = "https://www.instagram.com/reel/test/",
        };

        Assert.Equal("N/A", video.FacebookPublicationDisplay);
        Assert.Equal("N/A", video.InstagramPublicationDisplay);
        Assert.False(video.FacebookPlatformLinkAvailable);
        Assert.False(video.InstagramPlatformLinkAvailable);
    }

    [Fact]
    public void UploadedPlatform_WithSavedUrl_IsAvailableAsALink()
    {
        var shortVideo = History("9:16", 1, published: true) with
        {
            YouTubeUrl = "https://www.youtube.com/watch?v=test123",
            PublishedOnFacebook = true,
            FacebookUrl = "https://www.facebook.com/reel/123",
            PublishedOnInstagram = true,
            InstagramUrl = "https://www.instagram.com/reel/test/",
        };

        Assert.True(shortVideo.YouTubePlatformLinkAvailable);
        Assert.True(shortVideo.FacebookPlatformLinkAvailable);
        Assert.True(shortVideo.InstagramPlatformLinkAvailable);
    }

    [Fact]
    public void FirstCommentDisplay_WaitsForScheduledPublicationThenBecomesReady()
    {
        var scheduled = History("9:16", 1, published: true) with
        {
            PinnedComment = "How did you score?",
            YouTubeScheduledFor = DateTimeOffset.Now.AddHours(2).ToString("O"),
        };
        var ready = scheduled with { YouTubeScheduledFor = DateTimeOffset.Now.AddMinutes(-1).ToString("O") };

        Assert.Equal("Waiting for publication", scheduled.FirstCommentDisplay);
        Assert.Equal("Ready to post", ready.FirstCommentDisplay);
    }

    [Theory]
    [InlineData("https://youtu.be/qJAMsHFhlDA", "qJAMsHFhlDA")]
    [InlineData("https://www.youtube.com/watch?v=qJAMsHFhlDA&t=15", "qJAMsHFhlDA")]
    [InlineData("https://youtube.com/shorts/qJAMsHFhlDA", "qJAMsHFhlDA")]
    [InlineData("https://www.youtube.com/embed/qJAMsHFhlDA", "qJAMsHFhlDA")]
    public void AnalyticsVideoId_SupportsCommonYouTubeLinks(string url, string expected)
    {
        Assert.Equal(expected, YouTubeVideoAnalyticsService.TryGetVideoId(url));
    }

    [Theory]
    [InlineData("https://vimeo.com/qJAMsHFhlDA")]
    [InlineData("https://youtube.com.evil.example/watch?v=qJAMsHFhlDA")]
    [InlineData("not a link")]
    public void AnalyticsVideoId_RejectsInvalidLinks(string url)
    {
        Assert.Null(YouTubeVideoAnalyticsService.TryGetVideoId(url));
    }

    [Fact]
    public void AnalyticsResponse_ParsesPublicStatisticsAndPublishDate()
    {
        const string json = """
            {"items":[{"id":"qJAMsHFhlDA","snippet":{"publishedAt":"2026-08-22T09:30:00Z","channelId":"UC123","title":"History Quiz"},"statistics":{"viewCount":"1234","likeCount":"56","commentCount":"7"}}]}
            """;

        var result = Assert.Single(YouTubeVideoAnalyticsService.ParseResponse(json));
        Assert.Equal("qJAMsHFhlDA", result.VideoId);
        Assert.Equal(1234, result.Views);
        Assert.Equal(56, result.Likes);
        Assert.Equal(7, result.Comments);
        Assert.Equal("UC123", result.ChannelId);
        Assert.Equal("History Quiz", result.Title);
        Assert.Equal(new DateTime(2026, 8, 22, 9, 30, 0, DateTimeKind.Utc), result.PublishedAt);
    }

    [Fact]
    public void ChannelResponse_ParsesPublicTotals()
    {
        const string json = """
            {"items":[{"id":"UC123","snippet":{"title":"Factburst Quiz"},"statistics":{"viewCount":"9876","subscriberCount":"321","hiddenSubscriberCount":false,"videoCount":"42"}}]}
            """;

        var result = Assert.IsType<YouTubeChannelAnalytics>(
            YouTubeVideoAnalyticsService.ParseChannelResponse(json));
        Assert.Equal("Factburst Quiz", result.Title);
        Assert.Equal(9876, result.Views);
        Assert.Equal(321, result.Subscribers);
        Assert.Equal(42, result.Videos);
    }

    [Theory]
    [InlineData(1000, 40, 10, 5.0)]
    [InlineData(0, 40, 10, 0.0)]
    public void EngagementRate_UsesLikesAndComments(long views, long likes, long comments, double expected)
    {
        Assert.Equal(expected, YouTubeAnalyticsMetrics.EngagementRate(views, likes, comments), 6);
    }

    [Theory]
    [InlineData(720, 715, 720)]
    [InlineData(720, 725, 725)]
    [InlineData(-1, -5, 0)]
    public void AnalyticsRefresh_PreservesTheHighestKnownMetric(long stored, long fetched, long expected)
    {
        Assert.Equal(expected, YouTubeAnalyticsMetrics.PreserveHighest(stored, fetched));
    }

    [Fact]
    public void NextQuizRecommendation_BalancesTypeCategoryAndRecentUploads()
    {
        var recommendation = YouTubeNextQuizPlanner.Recommend(
        [
            History("16:9", 10, published: true, views: 100, series: "Music Quiz", categories: "Music"),
            History("16:9", 10, published: true, views: 80, series: "Space Quiz", categories: "Space"),
        ],
        ["Music", "Space", "Film"]);

        Assert.Equal("Film", recommendation.Category);
        Assert.Equal("Short", recommendation.VideoType);
        Assert.Equal("Film Quiz — Short", recommendation.Display);
    }

    [Fact]
    public void NextQuizRecommendation_StartsWithGeneralKnowledgeVideo()
    {
        var recommendation = YouTubeNextQuizPlanner.Recommend(
            [],
            ["Science", "General Knowledge", "History"]);

        Assert.Equal("General Knowledge", recommendation.Category);
        Assert.Equal("Video", recommendation.VideoType);
    }

    [Fact]
    public void NextQuizRecommendation_ExcludesIconsAndParanormal()
    {
        var recommendation = YouTubeNextQuizPlanner.Recommend(
            [],
            ["Logos", "Paranormal", "History"]);

        Assert.Equal("History", recommendation.Category);
        Assert.Equal("Video", recommendation.VideoType);
    }

    [Fact]
    public void NextQuizRecommendation_UsesFallbackWhenOnlyExcludedCategoriesAreAvailable()
    {
        var recommendation = YouTubeNextQuizPlanner.Recommend(
            [],
            ["Logos", "Paranormal"]);

        Assert.Equal("General Knowledge", recommendation.Category);
        Assert.Equal("Video", recommendation.VideoType);
    }

    private static QuizHistorySummary History(
        string format,
        int questionCount,
        bool published = false,
        long views = 0,
        long likes = 0,
        string series = "Music Quiz",
        string categories = "Music") => new(
        1,
        "Quiz",
        "2026-08-19 12:00:00",
        questionCount,
        categories,
        format,
        8,
        false,
        "",
        series,
        1,
        "",
        "",
        "",
        "",
        published,
        "",
        views,
        likes,
        "");

}
