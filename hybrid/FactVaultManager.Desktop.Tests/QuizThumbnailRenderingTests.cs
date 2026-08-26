using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThumbnailRenderingTests
{
    [Fact]
    public void LandscapeThumbnail_RendersAtYouTubeDimensionsWithIntelligentQuestion()
    {
        var questions = new[]
        {
            Question(1, "easy", "Which planet is closest to the Sun?"),
            Question(10, "insane", "Which planet has the shortest day in the Solar System?"),
        };
        var metadata = QuizPublishMetadataGenerator.Generate("Space Quiz", 1, questions, vertical: false);
        var thumbnail = QuizThumbnailDefaults.Create(metadata, questions.Length);

        var image = OnSta(() => new QuizThumbnailRenderer().RenderPreview(
            metadata,
            questions,
            thumbnail,
            new QuizVisualRenderSettings("dark", "Bottom right", 1.0, QuizTypeCatalog.Standard),
            logoPath: "",
            vertical: false));

        Assert.Equal(1280, image.PixelWidth);
        Assert.Equal(720, image.PixelHeight);
    }

    [Fact]
    public void ShortsThumbnail_RendersAtVerticalDimensions()
    {
        var questions = new[] { Question(1, "easy", "Which planet is closest to the Sun?") };
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Space Quiz",
            1,
            questions,
            vertical: true,
            fullQuizUrl: "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var thumbnail = QuizThumbnailDefaults.Create(metadata, questions.Length);

        BitmapSource image = OnSta(() => new QuizThumbnailRenderer().RenderPreview(
            metadata,
            questions,
            thumbnail,
            new QuizVisualRenderSettings("dark", "Bottom right", 1.0, QuizTypeCatalog.Standard),
            logoPath: "",
            vertical: true));

        Assert.Equal(1080, image.PixelWidth);
        Assert.Equal(1920, image.PixelHeight);
    }

    private static T OnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception caught)
            {
                error = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new InvalidOperationException("STA thumbnail render failed.", error);
        return result;
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
