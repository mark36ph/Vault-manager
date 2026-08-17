using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPublishingTests
{
    [Fact]
    public void Generate_BuildsSeriesEpisodeAndShortsMetadata()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "World Knowledge",
            12,
            [Question(1, "Geography"), Question(2, "Science")],
            vertical: true);

        Assert.Equal("World Knowledge", metadata.SeriesName);
        Assert.Equal(12, metadata.EpisodeNumber);
        Assert.Equal("#012", metadata.EpisodeLabel);
        Assert.Contains("World Knowledge #012", metadata.YouTubeTitle);
        Assert.Contains("2 Question", metadata.YouTubeTitle);
        Assert.Contains("Geography, Science", metadata.Description);
        Assert.Contains("#Geography", metadata.Hashtags);
        Assert.Contains("#Science", metadata.Hashtags);
        Assert.Contains("#Shorts", metadata.Hashtags);
        Assert.Contains("2", metadata.PinnedComment);
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
