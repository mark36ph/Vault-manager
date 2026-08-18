using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPublishingTests
{
    [Fact]
    public void Generate_BuildsViewerFocusedSeriesEpisodeAndShortsMetadata()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "World Knowledge",
            12,
            [Question(1, "Geography"), Question(2, "Science")],
            vertical: true);

        Assert.Equal("World Knowledge", metadata.SeriesName);
        Assert.Equal(12, metadata.EpisodeNumber);
        Assert.Equal("#012", metadata.EpisodeLabel);
        Assert.Equal("Can You Get 2/2? | World Knowledge #012", metadata.YouTubeTitle);
        Assert.Contains("Test your knowledge with 2 questions in World Knowledge #012.", metadata.Description);
        Assert.Contains("Can you get 2/2?", metadata.Description);
        Assert.Equal("#Quiz #Trivia #WorldKnowledge #Shorts", metadata.Hashtags);
        Assert.Contains("How did you score?", metadata.PinnedComment);
        Assert.Contains("Share your score out of 2", metadata.PinnedComment);
        Assert.Contains("Can anyone get 2/2?", metadata.PinnedComment);
    }

    [Fact]
    public void Generate_GeneralKnowledgeUsesThreeFocusedHashtagsAndSimpleDescription()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "General Knowledge Quiz",
            1,
            [Question(1, "Science"), Question(2, "History"), Question(3, "Sports")],
            vertical: false);

        Assert.Equal("Can You Get 3/3? | General Knowledge Quiz #001", metadata.YouTubeTitle);
        Assert.Equal("#Quiz #Trivia #GeneralKnowledge", metadata.Hashtags);
        Assert.DoesNotContain("Categories:", metadata.Description);
        Assert.EndsWith("#Quiz #Trivia #GeneralKnowledge", metadata.Description);
    }

    [Fact]
    public void Generate_SingleCategoryUsesThatCategoryAsThirdHashtag()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Science Quiz",
            4,
            [Question(1, "Science"), Question(2, "Science")],
            vertical: false);

        Assert.Equal("#Quiz #Trivia #Science", metadata.Hashtags);
    }

    [Fact]
    public void Validate_NormalizesHashtagsAndRejectsBadEpisode()
    {
        var valid = QuizPublishMetadataGenerator.Validate(new QuizPublishMetadata(
            "General Knowledge Quiz",
            3,
            "General Knowledge Quiz #003",
            "Test your knowledge and share your score.",
            "quiz  trivia #General-Knowledge",
            "How many did you get right?"));

        Assert.Equal("#quiz #trivia #GeneralKnowledge", valid.Hashtags);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuizPublishMetadataGenerator.Validate(valid with { EpisodeNumber = 0 }));
    }

    [Fact]
    public void Generate_StaysWithinYouTubeTitleLimit()
    {
        var series = new string('Q', 100);
        var metadata = QuizPublishMetadataGenerator.Generate(
            series,
            999,
            [Question(1, "General Knowledge")],
            vertical: false);

        Assert.InRange(metadata.YouTubeTitle.Length, 1, QuizPublishMetadataGenerator.MaxTitleLength);
    }

    [Fact]
    public void Write_CreatesPortablePublishingFilesAndJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var metadata = QuizPublishMetadataGenerator.Generate(
                "Science Challenge",
                7,
                [Question(1, "Science")],
                vertical: false);

            var jsonPath = QuizPublishMetadataFiles.Write(root, metadata);

            Assert.True(File.Exists(Path.Combine(root, "YouTube Title.txt")));
            Assert.True(File.Exists(Path.Combine(root, "Description.txt")));
            Assert.True(File.Exists(Path.Combine(root, "Hashtags.txt")));
            Assert.True(File.Exists(Path.Combine(root, "Pinned Comment.txt")));
            Assert.True(File.Exists(jsonPath));

            var json = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
            Assert.Equal("Science Challenge", json?["series"]?.GetValue<string>());
            Assert.Equal(7, json?["episode"]?.GetValue<int>());
            Assert.Equal(metadata.YouTubeTitle, json?["youtube_title"]?.GetValue<string>());
            Assert.Equal(metadata.PinnedComment, json?["pinned_comment"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static QuizQuestion Question(int id, string category) => new(
        id,
        $"Question {id}?",
        "Answer A",
        "Answer B",
        "Answer C",
        "Answer D",
        0,
        "Explanation",
        category,
        "medium",
        "Test",
        0);
}
