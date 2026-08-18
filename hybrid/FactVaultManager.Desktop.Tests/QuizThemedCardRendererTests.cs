using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThemedCardRendererTests
{
    [Fact]
    public void QuestionPreview_RendersApprovedLandscapeLayoutAtYouTubeSize()
    {
        var question = new QuizQuestion(
            1,
            "Which ancient wonder stood on Pharos?",
            "The Great Pyramid of Giza",
            "The Hanging Gardens of Babylon",
            "The Colossus of Rhodes",
            "The Lighthouse of Alexandria",
            3,
            "The Lighthouse of Alexandria stood on Pharos.",
            "History",
            "easy",
            "Test",
            0);
        var options = new QuizVideoBuildOptions(
            "Ancient Civilizations Quiz",
            QuestionSeconds: 8,
            AnswerSeconds: 3,
            Vertical: false,
            QuizLogoPath: "");

        var bitmap = new QuizThemedCardRenderer().RenderPreviewBitmap(
            question,
            options,
            new QuizVisualRenderSettings(),
            QuizPreviewCardKind.Countdown,
            number: 5,
            total: 10,
            countdownValue: 3);

        Assert.Equal(1920, bitmap.PixelWidth);
        Assert.Equal(1080, bitmap.PixelHeight);
    }
}
