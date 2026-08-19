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
            History("16:9", 10, published: true),
            History("9:16", 1),
            History("9:16", 1),
        ]);

        Assert.Equal(1, statistics.Videos);
        Assert.Equal(2, statistics.Shorts);
        Assert.Equal(1, statistics.Published);
        Assert.Equal(12, statistics.QuestionsUsed);
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

    private static QuizHistorySummary History(string format, int questionCount, bool published = false) => new(
        1,
        "Quiz",
        "2026-08-19 12:00:00",
        questionCount,
        "Music",
        format,
        8,
        false,
        "",
        "Music Quiz",
        1,
        "",
        "",
        "",
        "",
        published,
        "");

}
