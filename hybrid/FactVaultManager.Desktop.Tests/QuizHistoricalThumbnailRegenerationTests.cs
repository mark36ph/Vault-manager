using System.Text.Json;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizHistoricalThumbnailRegenerationTests
{
    [Fact]
    public void BuildPlan_UsesSavedQuestionOrderAndVisualMetadata()
    {
        using var folder = new TemporaryFolder();
        WriteQuizJson(folder.Path, new
        {
            title = "Space Challenge",
            theme = "game-show",
            logo_position = "Top left",
            logo_scale = 1.25,
            quiz_type = "Standard",
            questions = new object[]
            {
                SavedQuestion(4, 1, "Which planet is closest to the Sun?", "easy"),
                SavedQuestion(10, 2, "Which planet has the shortest day in the Solar System?", "insane"),
            },
        });
        var history = History(folder.Path, format: "16:9", questionCount: 2, series: "Space Quiz");

        var plan = QuizHistoricalThumbnailRegenerator.BuildPlan(
            history,
            [
                new QuizHistoryQuestion(1, 4, "Old text 1", "Space", "easy"),
                new QuizHistoryQuestion(2, 10, "Old text 2", "Space", "insane"),
            ],
            _ => null);

        Assert.False(plan.Vertical);
        Assert.Equal("game-show", plan.Visual.ThemeKey);
        Assert.Equal("Top left", plan.Visual.LogoPosition);
        Assert.Equal(1.25, plan.Visual.LogoScale);
        Assert.Equal(10, plan.Recommendation.Question.Id);
        Assert.Equal(2, plan.Recommendation.QuestionNumber);
        Assert.Equal("FINAL BOSS QUESTION", plan.Thumbnail.Headline);
        Assert.Equal("Which planet has the shortest day in the Solar System?", plan.Questions[1].Question);
    }

    [Fact]
    public void BuildPlan_EnrichesMissingSavedLogoArtworkFromQuestionBank()
    {
        using var folder = new TemporaryFolder();
        var artwork = System.IO.Path.Combine(folder.Path, "logo.png");
        File.WriteAllBytes(artwork, [1, 2, 3]);
        WriteQuizJson(folder.Path, new
        {
            title = "Logos",
            quiz_type = "Logo",
            questions = new object[]
            {
                SavedQuestion(99, 1, "Which company uses this logo?", "insane", category: "Logos"),
            },
        });
        var bank = Question(99, "Which company uses this logo?", "Logos", "insane", artwork);
        var history = History(folder.Path, format: "16:9", questionCount: 1, series: "Logos Quiz", categories: "Logos");

        var plan = QuizHistoricalThumbnailRegenerator.BuildPlan(
            history,
            [new QuizHistoryQuestion(1, 99, bank.Question, "Logos", "insane")],
            id => id == 99 ? bank : null);

        Assert.Equal(QuizTypeCatalog.Logo, plan.Visual.QuizType);
        Assert.Equal(artwork, plan.Questions.Single().ImagePath);
        Assert.True(plan.Recommendation.HasArtwork);
        Assert.Equal("NAME THIS LOGO", plan.Thumbnail.Headline);
    }

    [Fact]
    public void BuildPlan_FallsBackToQuizHistoryWhenProjectJsonIsMissing()
    {
        using var folder = new TemporaryFolder();
        var history = History(folder.Path, format: "16:9", questionCount: 1, series: "History Quiz", categories: "History");

        var plan = QuizHistoricalThumbnailRegenerator.BuildPlan(
            history,
            [new QuizHistoryQuestion(1, 321, "Who was the first Roman emperor?", "History", "hard")],
            _ => null);

        var question = Assert.Single(plan.Questions);
        Assert.Equal(321, question.Id);
        Assert.Equal("Who was the first Roman emperor?", question.Question);
        Assert.Equal("History", question.Category);
        Assert.Equal("hard", question.Difficulty);
        Assert.Equal("HARDER THAN IT LOOKS", plan.Thumbnail.Headline);
    }

    [Fact]
    public void BuildPlan_PrefersSavedPublishingMetadataOverBlankHistoryFields()
    {
        using var folder = new TemporaryFolder();
        WriteQuizJson(folder.Path, new
        {
            questions = new object[]
            {
                SavedQuestion(7, 1, "Which element has atomic number 79?", "insane", category: "Science"),
            },
        });
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "Publish Metadata.json"), JsonSerializer.Serialize(new
        {
            series = "Science Challenge",
            episode = 42,
            youtube_title = "Science Challenge #042",
            description = "Saved description",
            hashtags = "#Quiz #Science",
            pinned_comment = "Saved pinned comment",
        }));
        var history = History(folder.Path, format: "16:9", questionCount: 1, series: "", categories: "Science") with
        {
            YouTubeTitle = "",
            YouTubeDescription = "",
            Hashtags = "",
            PinnedComment = "",
        };

        var plan = QuizHistoricalThumbnailRegenerator.BuildPlan(
            history,
            [new QuizHistoryQuestion(1, 7, "Which element has atomic number 79?", "Science", "insane")],
            _ => null);

        Assert.Equal("Science Challenge", plan.Metadata.SeriesName);
        Assert.Equal(42, plan.Metadata.EpisodeNumber);
        Assert.Equal("Science Challenge #042", plan.Metadata.YouTubeTitle);
        Assert.Equal("#Quiz #Science", plan.Metadata.Hashtags);
    }

    [Theory]
    [InlineData("16:9", true)]
    [InlineData("9:16", false)]
    public void BatchEligibility_IncludesOnlyLongFormVideos(string format, bool expected)
    {
        var history = History("C:\\Quiz", format, 1, "Science Quiz");

        Assert.Equal(expected, QuizHistoricalThumbnailRegenerator.IsBatchEligible(history));
    }

    private static object SavedQuestion(
        int id,
        int number,
        string question,
        string difficulty,
        string category = "Space") => new
        {
            number,
            id,
            question,
            answers = new[] { "Mercury", "Venus", "Earth", "Jupiter" },
            correct_index = 0,
            explanation = "Explanation",
            category,
            difficulty,
        };

    private static QuizQuestion Question(int id, string text, string category, string difficulty, string imagePath = "") => new(
        id,
        text,
        "Answer A",
        "Answer B",
        "Answer C",
        "Answer D",
        0,
        "Explanation",
        category,
        difficulty,
        "Test",
        0,
        true,
        imagePath);

    private static QuizHistorySummary History(
        string projectFolder,
        string format,
        int questionCount,
        string series,
        string categories = "Space") => new(
        1,
        "Quiz",
        "2026-08-26 12:00:00",
        questionCount,
        categories,
        format,
        8,
        false,
        projectFolder,
        series,
        1,
        "Quiz title",
        "Quiz description",
        "#Quiz #Trivia",
        "Share your score",
        false,
        "",
        0,
        0,
        "");

    private static void WriteQuizJson(string folder, object payload) =>
        File.WriteAllText(System.IO.Path.Combine(folder, "quiz.json"), JsonSerializer.Serialize(payload));

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FactVaultThumbTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
