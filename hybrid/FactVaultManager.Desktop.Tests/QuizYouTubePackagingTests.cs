using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizYouTubePackagingTests
{
    [Fact]
    public void BuildVariants_CreatesThreeDistinctFilmPackages()
    {
        var questions = Enumerable.Range(1, 10)
            .Select(index => Question(index, "Film"))
            .ToList();
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Film Quiz",
            4,
            questions,
            vertical: false);

        var variants = QuizYouTubePackaging.BuildVariants(metadata, questions);

        Assert.Equal(3, variants.Count);
        Assert.Equal(3, variants.Select(item => item.Key).Distinct().Count());
        Assert.Equal(3, variants.Select(item => item.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, variants.Select(item => item.ThumbnailFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, variants.Select(item => item.Layout).Distinct().Count());
        Assert.All(variants, item => Assert.InRange(item.Title.Length, 1, QuizPublishMetadataGenerator.MaxTitleLength));

        Assert.Equal(QuizYouTubeThumbnailLayout.ScoreChallenge, variants[0].Layout);
        Assert.Equal("CAN YOU GET 10/10?", variants[0].Thumbnail.Headline);
        Assert.Equal("FILM", variants[0].Thumbnail.Subtitle);

        Assert.Equal(QuizYouTubeThumbnailLayout.ExpertChallenge, variants[1].Layout);
        Assert.Equal("ONLY MOVIE EXPERTS", variants[1].Thumbnail.Headline);
        Assert.Equal("PROVE IT", variants[1].Thumbnail.Subtitle);

        Assert.Equal(QuizYouTubeThumbnailLayout.CategorySearch, variants[2].Layout);
        Assert.Equal("MOVIE QUIZ", variants[2].Thumbnail.Headline);
        Assert.Equal("10 QUESTION CHALLENGE", variants[2].Thumbnail.Subtitle);
    }

    [Fact]
    public void BuildVariants_UsesLogoSpecificPackaging()
    {
        var questions = Enumerable.Range(1, 6)
            .Select(index => Question(index, "Logos"))
            .ToList();
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Logos Quiz",
            2,
            questions,
            vertical: false,
            logoQuiz: true);

        var variants = QuizYouTubePackaging.BuildVariants(metadata, questions);

        Assert.Equal("ONLY LOGO EXPERTS", variants[1].Thumbnail.Headline);
        Assert.Equal("LOGO QUIZ", variants[2].Thumbnail.Headline);
        Assert.Equal("6 QUESTION CHALLENGE", variants[2].Thumbnail.Subtitle);
        Assert.Contains("Logos", variants[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildVariants_UsesCompactExpertLabelForGeneralKnowledge()
    {
        var questions = Enumerable.Range(1, 10)
            .Select(index => Question(index, "General Knowledge"))
            .ToList();
        var metadata = QuizPublishMetadataGenerator.Generate(
            "General Knowledge Quiz",
            7,
            questions,
            vertical: false);

        var variants = QuizYouTubePackaging.BuildVariants(metadata, questions);

        Assert.Equal("ONLY TRIVIA EXPERTS", variants[1].Thumbnail.Headline);
        Assert.Equal("GENERAL KNOWLEDGE QUIZ", variants[2].Thumbnail.Headline);
    }

    private static QuizQuestion Question(int id, string category) => new(
        id,
        $"Sample {category} question {id}?",
        "Answer A",
        "Answer B",
        "Answer C",
        "Answer D",
        0,
        "Sample explanation.",
        category,
        "hard",
        "test",
        0,
        true,
        "");
}
