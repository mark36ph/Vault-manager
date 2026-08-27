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
        Assert.All(variants, item => Assert.InRange(item.Title.Length, 1, QuizPublishMetadataGenerator.MaxTitleLength));
        Assert.Contains("10/10", variants[0].Thumbnail.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ONLY EXPERTS", variants[1].Thumbnail.Headline);
        Assert.Equal("MOVIE IQ TEST", variants[2].Thumbnail.Headline);
    }

    [Fact]
    public void BuildVariants_UsesLogoSpecificCategoryChallenge()
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

        Assert.Equal("NAME THESE LOGOS", variants[2].Thumbnail.Headline);
        Assert.Contains("Logos", variants[0].Title, StringComparison.OrdinalIgnoreCase);
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
