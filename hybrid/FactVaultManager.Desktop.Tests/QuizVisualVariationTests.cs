using System.Runtime.ExceptionServices;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizVisualVariationTests
{
    [Fact]
    public void Planner_IsDeterministicForTheSameDraft()
    {
        var questions = Questions(10, startId: 10);

        var first = QuizVisualVariationPlanner.ForQuestions(questions);
        var second = QuizVisualVariationPlanner.ForQuestions(questions);

        Assert.Equal(first, second);
        Assert.Contains(first.ThemeKey, QuizVisualVariationPlanner.AutomaticThemeKeys);
        Assert.Contains(first.LayoutKey, QuizVisualVariationPlanner.AutomaticLayoutKeys);
    }

    [Fact]
    public void Planner_ProducesMoreThanOneApprovedLookAcrossDifferentDrafts()
    {
        var looks = Enumerable.Range(0, 24)
            .Select(index => QuizVisualVariationPlanner.ForQuestions(Questions(10, startId: 100 + (index * 20))))
            .Select(variation => variation.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(looks.Length > 1);
        Assert.All(looks, look => Assert.Contains(" • ", look));
    }

    [Theory]
    [InlineData(false, QuizTypeCatalog.Standard, true)]
    [InlineData(true, QuizTypeCatalog.Standard, false)]
    [InlineData(false, QuizTypeCatalog.Logo, false)]
    [InlineData(true, QuizTypeCatalog.Logo, false)]
    public void Applies_OnlyToLandscapeStandardQuizzes(bool vertical, string quizType, bool expected)
    {
        Assert.Equal(expected, QuizVisualVariationPlanner.Applies(vertical, quizType));
    }

    [Theory]
    [InlineData("Classic Frame", "classic-frame")]
    [InlineData("Left Rail", "left-rail")]
    [InlineData("Right Rail", "right-rail")]
    [InlineData("unknown", "classic-frame")]
    public void LayoutCatalog_NormalizesSupportedLayouts(string value, string expected)
    {
        Assert.Equal(expected, QuizCardLayoutCatalog.Normalize(value));
    }

    [Fact]
    public void ApplyPreview_PreservesCanvasSizeForEveryAutomaticLayout()
    {
        Exception? renderError = null;
        var sizes = new List<(int Width, int Height)>();
        var thread = new Thread(() =>
        {
            try
            {
                const int width = 192;
                const int height = 108;
                var stride = width * 4;
                var pixels = new byte[stride * height];
                for (var offset = 0; offset < pixels.Length; offset += 4)
                {
                    pixels[offset] = 36;
                    pixels[offset + 1] = 48;
                    pixels[offset + 2] = 72;
                    pixels[offset + 3] = 255;
                }
                var source = System.Windows.Media.Imaging.BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);
                source.Freeze();
                var theme = QuizVisualThemeCatalog.Resolve("game-show");

                foreach (var layout in QuizVisualVariationPlanner.AutomaticLayoutKeys)
                {
                    var rendered = QuizCardVariationPostProcessor.ApplyPreview(source, theme, layout);
                    sizes.Add((rendered.PixelWidth, rendered.PixelHeight));
                }
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

        Assert.Equal(QuizVisualVariationPlanner.AutomaticLayoutKeys.Count, sizes.Count);
        Assert.All(sizes, size =>
        {
            Assert.Equal(192, size.Width);
            Assert.Equal(108, size.Height);
        });
    }

    private static IReadOnlyList<QuizQuestion> Questions(int count, int startId) =>
        Enumerable.Range(0, count)
            .Select(index => new QuizQuestion(
                startId + index,
                $"Question {index + 1}?",
                "A",
                "B",
                "C",
                "D",
                0,
                "Explanation",
                index % 2 == 0 ? "Space" : "Technology",
                QuizDifficultyCatalog.StorageValues[index % QuizDifficultyCatalog.StorageValues.Count],
                "Test",
                TimesUsed: index,
                IsEnabled: true))
            .ToArray();
}
