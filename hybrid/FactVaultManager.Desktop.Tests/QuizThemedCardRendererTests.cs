using System.Runtime.ExceptionServices;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThemedCardRendererTests
{
    [Fact]
    public void QuestionCounter_HasComfortablePaddingInBothLayouts()
    {
        var vertical = QuizThemedCardRenderer.QuestionBadgeLayout(vertical: true);
        var landscape = QuizThemedCardRenderer.QuestionBadgeLayout(vertical: false);

        Assert.Equal(220, vertical.Width);
        Assert.Equal(80, vertical.Height);
        Assert.Equal(new System.Windows.Thickness(24, 10, 24, 10), vertical.Padding);
        Assert.Equal(210, landscape.Width);
        Assert.Equal(90, landscape.Height);
        Assert.Equal(new System.Windows.Thickness(18, 6, 18, 6), landscape.Padding);
    }

    [Theory]
    [InlineData("Logos", "LOGOS")]
    [InlineData("Logos Quiz", "LOGOS")]
    [InlineData("Logo Quiz", "LOGOS")]
    public void LogoQuizDisplay_UsesLogosTerminology(string title, string expected)
    {
        Assert.Equal(expected, QuizThemedCardRenderer.LogoQuizDisplayName(title));
        Assert.Equal("LOGOS 1 / 10", QuizThemedCardRenderer.LogoCounterText(1, 10));
    }

    [Fact]
    public void LongQuestionText_ScalesDownToFitLandscapePanel()
    {
        Exception? renderError = null;
        double naturalHeight = 0;
        double viewportHeight = 0;
        System.Windows.Controls.StretchDirection stretchDirection = default;

        var thread = new Thread(() =>
        {
            try
            {
                var fitted = QuizThemedCardRenderer.BuildFittedQuestionText(
                    "Who is closely associated with the development of movable-type printing in 15th-century Europe?",
                    fontSize: 54,
                    maxWidth: 1090);

                fitted.Measure(new System.Windows.Size(1120, 142));
                fitted.Arrange(new System.Windows.Rect(0, 0, 1120, 142));
                fitted.UpdateLayout();

                naturalHeight = ((System.Windows.Controls.TextBlock)fitted.Child).DesiredSize.Height;
                viewportHeight = fitted.RenderSize.Height;
                stretchDirection = fitted.StretchDirection;
            }
            catch (Exception error)
            {
                renderError = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (renderError is not null)
            ExceptionDispatchInfo.Capture(renderError).Throw();

        Assert.True(naturalHeight > viewportHeight);
        Assert.Equal(142.0, viewportHeight);
        Assert.Equal(System.Windows.Controls.StretchDirection.DownOnly, stretchDirection);
    }

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

        Exception? renderError = null;
        var pixelWidth = 0;
        var pixelHeight = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var bitmap = new QuizThemedCardRenderer().RenderPreviewBitmap(
                    question,
                    options,
                    new QuizVisualRenderSettings(),
                    QuizPreviewCardKind.Countdown,
                    number: 5,
                    total: 10,
                    countdownValue: 3);
                pixelWidth = bitmap.PixelWidth;
                pixelHeight = bitmap.PixelHeight;
            }
            catch (Exception error)
            {
                renderError = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (renderError is not null)
            ExceptionDispatchInfo.Capture(renderError).Throw();

        Assert.Equal(1920, pixelWidth);
        Assert.Equal(1080, pixelHeight);
    }

    [Fact]
    public void LogoQuizPreflight_RequiresAnImageWithoutChangingStandardQuizRules()
    {
        var question = Question(imagePath: "");
        var options = new QuizVideoBuildOptions("Logo Quiz");

        Assert.Empty(QuizPreflight.Analyze([question], options, QuizTypeCatalog.Standard));
        Assert.Contains(
            QuizPreflight.Analyze([question], options, QuizTypeCatalog.Logo),
            issue => issue.Severity == QuizPreflightSeverity.Error && issue.QuestionId == question.Id);
    }

    [Fact]
    public void LogoArtwork_RendersAsTheImageWithoutASurroundingPanel()
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"factvault-logo-artwork-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z2S8AAAAASUVORK5CYII="));
        try
        {
            Exception? renderError = null;
            double maxWidth = 0;
            double maxHeight = 0;
            System.Windows.HorizontalAlignment horizontalAlignment = default;
            var hasParent = true;
            var thread = new Thread(() =>
            {
                try
                {
                    var artwork = QuizThemedCardRenderer.BuildLogoArtwork(imagePath, vertical: false);
                    maxWidth = artwork.MaxWidth;
                    maxHeight = artwork.MaxHeight;
                    horizontalAlignment = artwork.HorizontalAlignment;
                    hasParent = artwork.Parent is not null;
                }
                catch (Exception error) { renderError = error; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (renderError is not null)
                ExceptionDispatchInfo.Capture(renderError).Throw();

            Assert.Equal(520, maxWidth);
            Assert.Equal(250, maxHeight);
            Assert.Equal(System.Windows.HorizontalAlignment.Center, horizontalAlignment);
            Assert.False(hasParent);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public void LogoQuestionPreview_RendersFeaturedImageAndBrandLogoAtVerticalShortsSize()
    {
        var featuredImagePath = Path.Combine(Path.GetTempPath(), $"factvault-featured-logo-{Guid.NewGuid():N}.png");
        var brandLogoPath = Path.Combine(Path.GetTempPath(), $"factvault-brand-logo-{Guid.NewGuid():N}.png");
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z2S8AAAAASUVORK5CYII=");
        File.WriteAllBytes(featuredImagePath, imageBytes);
        File.WriteAllBytes(brandLogoPath, imageBytes);

        try
        {
            var question = Question(featuredImagePath);
            var options = new QuizVideoBuildOptions(
                "Logo Quiz",
                QuestionSeconds: 8,
                AnswerSeconds: 3,
                Vertical: true,
                QuizLogoPath: brandLogoPath);
            Exception? renderError = null;
            var pixelWidth = 0;
            var pixelHeight = 0;
            var thread = new Thread(() =>
            {
                try
                {
                    var bitmap = new QuizThemedCardRenderer().RenderPreviewBitmap(
                        question,
                        options,
                        new QuizVisualRenderSettings(QuizType: QuizTypeCatalog.Logo),
                        QuizPreviewCardKind.Question,
                        number: 1,
                        total: 10);
                    pixelWidth = bitmap.PixelWidth;
                    pixelHeight = bitmap.PixelHeight;
                }
                catch (Exception error)
                {
                    renderError = error;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (renderError is not null)
                ExceptionDispatchInfo.Capture(renderError).Throw();

            Assert.Equal(1080, pixelWidth);
            Assert.Equal(1920, pixelHeight);
        }
        finally
        {
            File.Delete(featuredImagePath);
            File.Delete(brandLogoPath);
        }
    }

    private static QuizQuestion Question(string imagePath) => new(
        42,
        "Which company uses this logo?",
        "Company A",
        "Company B",
        "Company C",
        "Company D",
        1,
        "Company B uses this logo.",
        "Technology",
        "easy",
        "Test",
        0,
        true,
        imagePath);
}
