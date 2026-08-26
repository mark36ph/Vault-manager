using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThumbnailRenderingTests
{
    [StaFact]
    public void LandscapeThumbnail_RendersAtYouTubeDimensionsWithIntelligentQuestion()
    {
        var questions = new[]
        {
            Question(1, "easy", "Which planet is closest to the Sun?"),
            Question(10, "insane", "Which planet has the shortest day in the Solar System?"),
        };
        var metadata = QuizPublishMetadataGenerator.Generate("Space Quiz", 1, questions, vertical: false);
        var thumbnail = QuizThumbnailDefaults.Create(metadata, questions.Length);

        BitmapSource image = new QuizThumbnailRenderer().RenderPreview(
            metadata,
            questions,
            thumbnail,
            new QuizVisualRenderSettings("dark", "Bottom right", 1.0, QuizTypeCatalog.Standard),
            logoPath: "",
            vertical: false);

        Assert.Equal(1280, image.PixelWidth);
        Assert.Equal(720, image.PixelHeight);
    }

    [StaFact]
    public void ShortsThumbnail_RendersAtVerticalDimensions()
    {
        var questions = new[] { Question(1, "easy", "Which planet is closest to the Sun?") };
        var metadata = QuizPublishMetadataGenerator.Generate("Space Quiz", 1, questions, vertical: true);
        var thumbnail = QuizThumbnailDefaults.Create(metadata, questions.Length);

        var image = new QuizThumbnailRenderer().RenderPreview(
            metadata,
            questions,
            thumbnail,
            new QuizVisualRenderSettings("dark", "Bottom right", 1.0, QuizTypeCatalog.Standard),
            logoPath: "",
            vertical: true);

        Assert.Equal(1080, image.PixelWidth);
        Assert.Equal(1920, image.PixelHeight);
    }

    private static QuizQuestion Question(int id, string difficulty, string question) => new(
        id,
        question,
        "Mercury",
        "Venus",
        "Earth",
        "Jupiter",
        0,
        "Explanation",
        "Space",
        difficulty,
        "Test",
        0);
}
