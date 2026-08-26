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
    public void Planner_UsesOnlyThreeApprovedFullCanvasLooks()
    {
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            "dark|clean-frame",
            "bright|corner-glow",
            "game-show|stage-accent",
        };

        var looks = Enumerable.Range(0, 48)
            .Select(index => QuizVisualVariationPlanner.ForQuestions(Questions(10, startId: 100 + (index * 20))))
            .ToArray();

        Assert.All(looks, look => Assert.Contains($"{look.ThemeKey}|{look.LayoutKey}", approved));
        Assert.True(looks.Select(look => look.DisplayName).Distinct(StringComparer.Ordinal).Count() > 1);
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
    [InlineData("Clean Frame", "clean-frame")]
    [InlineData("Corner Glow", "corner-glow")]
    [InlineData("Stage Accent", "stage-accent")]
    [InlineData("Left Rail", "clean-frame")]
    [InlineData("Right Rail", "clean-frame")]
    [InlineData("unknown", "clean-frame")]
    public void LayoutCatalog_NormalizesOnlyFullCanvasLayouts(string value, string expected)
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
                var source = SolidBitmap(192, 108, 72, 48, 36, 255);
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

    [Theory]
    [InlineData("dark", 0, 204, 255)]
    [InlineData("bright", 46, 211, 255)]
    [InlineData("game-show", 171, 93, 255)]
    public void ApplyPreview_ThemesActualCardAccentPixels(string themeKey, byte expectedR, byte expectedG, byte expectedB)
    {
        Exception? renderError = null;
        (byte B, byte G, byte R, byte A) pixel = default;
        var thread = new Thread(() =>
        {
            try
            {
                var source = SolidBitmap(192, 108, 0, 0, 0, 0);
                source = PaintPixel(source, 96, 54, r: 0, g: 204, b: 255, a: 255);
                var rendered = QuizCardVariationPostProcessor.ApplyPreview(
                    source,
                    QuizVisualThemeCatalog.Resolve(themeKey),
                    "clean-frame");
                pixel = ReadPixel(rendered, 96, 54);
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

        Assert.Equal(expectedR, pixel.R);
        Assert.Equal(expectedG, pixel.G);
        Assert.Equal(expectedB, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void ApplyPreview_CanRemoveTinyChoicePromptRegionWithoutTouchingRightSide()
    {
        Exception? renderError = null;
        (byte B, byte G, byte R, byte A) left = default;
        (byte B, byte G, byte R, byte A) right = default;
        var thread = new Thread(() =>
        {
            try
            {
                var source = SolidBitmap(192, 108, 0, 0, 0, 0);
                source = PaintPixel(source, 30, 102, 255, 255, 255, 255);
                source = PaintPixel(source, 150, 102, 255, 255, 255, 255);
                var rendered = QuizCardVariationPostProcessor.ApplyPreview(
                    source,
                    QuizVisualThemeCatalog.Resolve("dark"),
                    "clean-frame",
                    hideChoicePrompt: true);
                left = ReadPixel(rendered, 30, 102);
                right = ReadPixel(rendered, 150, 102);
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

        Assert.Equal(0, left.A);
        Assert.Equal(255, right.A);
    }

    private static System.Windows.Media.Imaging.BitmapSource SolidBitmap(
        int width,
        int height,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = a;
        }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            width,
            height,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static System.Windows.Media.Imaging.BitmapSource PaintPixel(
        System.Windows.Media.Imaging.BitmapSource source,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        var offset = (y * stride) + (x * 4);
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = a;
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            width,
            height,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static (byte B, byte G, byte R, byte A) ReadPixel(
        System.Windows.Media.Imaging.BitmapSource source,
        int x,
        int y)
    {
        var bitmap = source.Format == System.Windows.Media.PixelFormats.Bgra32
            ? source
            : new System.Windows.Media.Imaging.FormatConvertedBitmap(
                source,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                0);
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
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
