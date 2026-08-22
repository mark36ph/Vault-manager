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
            {"items":[{"id":"qJAMsHFhlDA","snippet":{"publishedAt":"2026-08-22T09:30:00Z"},"statistics":{"viewCount":"1234","likeCount":"56"}}]}
            """;

        var result = Assert.Single(YouTubeVideoAnalyticsService.ParseResponse(json));
        Assert.Equal("qJAMsHFhlDA", result.VideoId);
        Assert.Equal(1234, result.Views);
        Assert.Equal(56, result.Likes);
        Assert.Equal(new DateTime(2026, 8, 22, 9, 30, 0, DateTimeKind.Utc), result.PublishedAt);
    }

    private static QuizHistorySummary History(
        string format,
        int questionCount,
        bool published = false,
        long views = 0,
        long likes = 0,
        string series = "Music Quiz") => new(
        1,
        "Quiz",
        "2026-08-19 12:00:00",
        questionCount,
        "Music",
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
