using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizCardVariationPostProcessor
{
    public static QuizVisualVariation? Apply(
        NativeTimeline timeline,
        string projectFolder,
        IReadOnlyList<QuizQuestion> questions,
        QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        timeline.Validate();

        if (!QuizVisualVariationPlanner.Applies(options.Vertical, QuizTypeFor(questions)))
            return null;

        var planned = QuizVisualVariationPlanner.ForQuestions(questions);
        var theme = ReadTheme(projectFolder);
        var variation = planned with { ThemeKey = theme.Key };
        if (timeline.Metadata.TryGetValue("quiz_visual_variation_applied", out var applied) && applied is true)
            return variation;

        var root = Path.GetFullPath(projectFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var sources = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .SelectMany(track => track.Clips)
            .Where(clip => clip.Kind == NativeTimelineClipKind.Image)
            .Select(clip => clip.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => Path.GetFullPath(source!))
            .Where(source => source.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Where(source => string.Equals(Path.GetExtension(source), ".png", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var source in sources)
            ApplyToFile(source, options.Width, options.Height, theme, variation.LayoutKey);

        timeline.Metadata["quiz_visual_variation_applied"] = true;
        timeline.Metadata["quiz_visual_variation_theme"] = variation.ThemeKey;
        timeline.Metadata["quiz_visual_variation_layout"] = variation.LayoutKey;
        WriteMetadata(projectFolder, variation);
        timeline.Validate();
        return variation;
    }

    public static BitmapSource ApplyPreview(
        BitmapSource source,
        QuizVisualTheme theme,
        string layoutKey)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(theme);
        return RenderVariation(source, source.PixelWidth, source.PixelHeight, theme, QuizCardLayoutCatalog.Resolve(layoutKey));
    }

    private static void ApplyToFile(
        string sourcePath,
        int width,
        int height,
        QuizVisualTheme theme,
        string layoutKey)
    {
        var bitmap = LoadBitmap(sourcePath);
        if (bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            return;

        var rendered = RenderVariation(bitmap, width, height, theme, QuizCardLayoutCatalog.Resolve(layoutKey));
        var temporary = sourcePath + ".variation.tmp.png";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            encoder.Save(stream);
        File.Move(temporary, sourcePath, overwrite: true);
    }

    private static BitmapSource RenderVariation(
        BitmapSource source,
        int width,
        int height,
        QuizVisualTheme theme,
        QuizCardLayoutProfile layout)
    {
        var accent = theme.Accent;
        var secondary = theme.Countdown;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var full = new Rect(0, 0, width, height);
            if (layout.RailSide == QuizCardRailSide.None)
            {
                drawing.DrawImage(source, full);
                DrawFrame(drawing, full, layout.EdgeInset, accent, secondary);
            }
            else
            {
                var cardWidth = width * layout.CardScale;
                var cardHeight = height * layout.CardScale;
                var top = (height - cardHeight) / 2.0;
                var left = layout.RailSide == QuizCardRailSide.Left
                    ? width - cardWidth - layout.EdgeInset
                    : layout.EdgeInset;
                var cardRect = new Rect(left, top, cardWidth, cardHeight);
                drawing.DrawImage(source, cardRect);
                DrawFrame(drawing, cardRect, 0, accent, secondary);
                DrawRail(drawing, width, height, layout, accent, secondary);
            }
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static void DrawFrame(
        DrawingContext drawing,
        Rect bounds,
        double inset,
        Color accent,
        Color secondary)
    {
        var frame = new Rect(
            bounds.X + inset,
            bounds.Y + inset,
            Math.Max(1, bounds.Width - (inset * 2)),
            Math.Max(1, bounds.Height - (inset * 2)));
        drawing.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(205, accent.R, accent.G, accent.B)), 7),
            frame);
        var inner = new Rect(
            frame.X + 10,
            frame.Y + 10,
            Math.Max(1, frame.Width - 20),
            Math.Max(1, frame.Height - 20));
        drawing.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(120, secondary.R, secondary.G, secondary.B)), 2),
            inner);
    }

    private static void DrawRail(
        DrawingContext drawing,
        int width,
        int height,
        QuizCardLayoutProfile layout,
        Color accent,
        Color secondary)
    {
        var left = layout.RailSide == QuizCardRailSide.Left
            ? layout.EdgeInset
            : width - layout.EdgeInset - layout.RailWidth;
        var rail = new Rect(left, layout.EdgeInset, layout.RailWidth, height - (layout.EdgeInset * 2));
        var fill = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(210, accent.R, accent.G, accent.B), 0),
                new(Color.FromArgb(142, secondary.R, secondary.G, secondary.B), 0.55),
                new(Color.FromArgb(205, accent.R, accent.G, accent.B), 1),
            },
            new Point(0, 0),
            new Point(0, 1));
        drawing.DrawRoundedRectangle(fill, null, rail, 20, 20);

        var lineX = layout.RailSide == QuizCardRailSide.Left
            ? rail.Right + 12
            : rail.Left - 12;
        drawing.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(220, accent.R, accent.G, accent.B)), 5),
            new Point(lineX, layout.EdgeInset + 12),
            new Point(lineX, height - layout.EdgeInset - 12));
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string QuizTypeFor(IReadOnlyList<QuizQuestion> questions) =>
        questions.Count > 0 && questions.All(question =>
            QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            ? QuizTypeCatalog.Logo
            : QuizTypeCatalog.Standard;

    private static QuizVisualTheme ReadTheme(string projectFolder)
    {
        var path = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
        try
        {
            if (!File.Exists(path))
                return QuizVisualThemeCatalog.Resolve("dark");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.TryGetProperty("theme", out var theme) && theme.ValueKind == JsonValueKind.String
                ? QuizVisualThemeCatalog.Resolve(theme.GetString())
                : QuizVisualThemeCatalog.Resolve("dark");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz variation theme: {error.Message}");
            return QuizVisualThemeCatalog.Resolve("dark");
        }
    }

    private static void WriteMetadata(string projectFolder, QuizVisualVariation variation)
    {
        var path = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
        try
        {
            if (!File.Exists(path))
                return;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null)
                return;
            root["visual_variation"] = true;
            root["visual_variation_theme"] = variation.ThemeKey;
            root["layout"] = variation.LayoutKey;
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not write quiz variation metadata: {error.Message}");
        }
    }
}
