using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizCardVariationPostProcessor
{
    private static readonly Color OriginalA = Color.FromRgb(0, 204, 255);
    private static readonly Color OriginalB = Color.FromRgb(204, 70, 255);
    private static readonly Color OriginalC = Color.FromRgb(255, 202, 45);
    private static readonly Color OriginalD = Color.FromRgb(70, 235, 115);
    private static readonly Color OriginalPanel = Color.FromRgb(8, 14, 62);
    private static readonly Color OriginalPanel2 = Color.FromRgb(13, 18, 78);
    private static readonly Color OriginalInner = Color.FromRgb(7, 12, 54);
    private static readonly Color OriginalTimer = Color.FromRgb(29, 39, 104);

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
        {
            var hideChoicePrompt = Path.GetFileName(source)
                .EndsWith("_question.png", StringComparison.OrdinalIgnoreCase);
            ApplyToFile(
                source,
                options.Width,
                options.Height,
                theme,
                variation.LayoutKey,
                hideChoicePrompt);
        }

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
        string layoutKey,
        bool hideChoicePrompt = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(theme);
        return RenderVariation(
            source,
            source.PixelWidth,
            source.PixelHeight,
            theme,
            QuizCardLayoutCatalog.Resolve(layoutKey),
            hideChoicePrompt);
    }

    private static void ApplyToFile(
        string sourcePath,
        int width,
        int height,
        QuizVisualTheme theme,
        string layoutKey,
        bool hideChoicePrompt)
    {
        var bitmap = LoadBitmap(sourcePath);
        if (bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            return;

        var rendered = RenderVariation(
            bitmap,
            width,
            height,
            theme,
            QuizCardLayoutCatalog.Resolve(layoutKey),
            hideChoicePrompt);
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
        QuizCardLayoutProfile layout,
        bool hideChoicePrompt)
    {
        var palette = PaletteFor(theme);
        var recolored = RecolorCard(source, theme, palette, hideChoicePrompt);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var full = new Rect(0, 0, width, height);
            drawing.DrawImage(recolored, full);
            switch (layout.FrameStyle)
            {
                case QuizCardFrameStyle.CornerGlow:
                    DrawCornerAccents(
                        drawing,
                        width,
                        height,
                        layout.EdgeInset,
                        palette.FramePrimary,
                        palette.FrameSecondary);
                    break;
                case QuizCardFrameStyle.StageAccent:
                    DrawStageAccents(
                        drawing,
                        width,
                        height,
                        layout.EdgeInset,
                        palette.FramePrimary,
                        palette.FrameSecondary);
                    break;
                default:
                    DrawCleanFrame(drawing, full, layout.EdgeInset, palette.FramePrimary);
                    break;
            }
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static BitmapSource RecolorCard(
        BitmapSource source,
        QuizVisualTheme theme,
        QuizPalette palette,
        bool hideChoicePrompt)
    {
        var bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var recolor = !string.Equals(theme.Key, "dark", StringComparison.OrdinalIgnoreCase);
        var recolorStartY = (int)Math.Round(height * 0.14);
        var clearTop = (int)Math.Round(height * 0.92);
        var clearLeft = (int)Math.Round(width * 0.10);
        var clearRight = (int)Math.Round(width * 0.52);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 4);
                if (hideChoicePrompt && y >= clearTop && x >= clearLeft && x <= clearRight)
                {
                    pixels[offset + 3] = 0;
                    continue;
                }

                if (!recolor || y < recolorStartY || pixels[offset + 3] == 0)
                    continue;

                var current = Color.FromRgb(
                    pixels[offset + 2],
                    pixels[offset + 1],
                    pixels[offset]);
                if (!TryMapColor(current, palette, out var mapped))
                    continue;

                pixels[offset] = mapped.B;
                pixels[offset + 1] = mapped.G;
                pixels[offset + 2] = mapped.R;
            }
        }

        var result = BitmapSource.Create(
            width,
            height,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static bool TryMapColor(Color current, QuizPalette palette, out Color mapped)
    {
        var mappings = new (Color Source, Color Target, int Threshold)[]
        {
            (OriginalA, palette.A, 78),
            (OriginalB, palette.B, 78),
            (OriginalC, palette.C, 78),
            (OriginalD, palette.D, 72),
            (OriginalPanel, palette.Panel, 42),
            (OriginalPanel2, palette.Panel2, 42),
            (OriginalInner, palette.Inner, 38),
            (OriginalTimer, palette.Timer, 48),
        };

        var bestDistance = int.MaxValue;
        var bestTarget = current;
        foreach (var mapping in mappings)
        {
            var distance = DistanceSquared(current, mapping.Source);
            if (distance > mapping.Threshold * mapping.Threshold || distance >= bestDistance)
                continue;
            bestDistance = distance;
            bestTarget = mapping.Target;
        }

        mapped = bestTarget;
        return bestDistance != int.MaxValue;
    }

    private static int DistanceSquared(Color left, Color right)
    {
        var red = left.R - right.R;
        var green = left.G - right.G;
        var blue = left.B - right.B;
        return (red * red) + (green * green) + (blue * blue);
    }

    private static QuizPalette PaletteFor(QuizVisualTheme theme) =>
        theme.Key switch
        {
            "bright" => new QuizPalette(
                A: Color.FromRgb(46, 211, 255),
                B: Color.FromRgb(70, 130, 255),
                C: Color.FromRgb(88, 188, 255),
                D: Color.FromRgb(88, 226, 210),
                Panel: Color.FromRgb(7, 27, 66),
                Panel2: Color.FromRgb(9, 35, 80),
                Inner: Color.FromRgb(5, 23, 61),
                Timer: Color.FromRgb(24, 72, 123),
                FramePrimary: Color.FromRgb(46, 211, 255),
                FrameSecondary: Color.FromRgb(70, 130, 255)),
            "game-show" => new QuizPalette(
                A: Color.FromRgb(171, 93, 255),
                B: Color.FromRgb(239, 87, 186),
                C: Color.FromRgb(255, 202, 45),
                D: Color.FromRgb(255, 144, 72),
                Panel: Color.FromRgb(33, 12, 69),
                Panel2: Color.FromRgb(46, 16, 84),
                Inner: Color.FromRgb(27, 9, 58),
                Timer: Color.FromRgb(92, 41, 112),
                FramePrimary: Color.FromRgb(255, 202, 45),
                FrameSecondary: Color.FromRgb(171, 93, 255)),
            _ => new QuizPalette(
                OriginalA,
                OriginalB,
                OriginalC,
                OriginalD,
                OriginalPanel,
                OriginalPanel2,
                OriginalInner,
                OriginalTimer,
                OriginalA,
                OriginalC),
        };

    private static void DrawCleanFrame(
        DrawingContext drawing,
        Rect bounds,
        double inset,
        Color accent)
    {
        var frame = new Rect(
            bounds.X + inset,
            bounds.Y + inset,
            Math.Max(1, bounds.Width - (inset * 2)),
            Math.Max(1, bounds.Height - (inset * 2)));
        drawing.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B)), 3),
            frame);
    }

    private static void DrawCornerAccents(
        DrawingContext drawing,
        int width,
        int height,
        double inset,
        Color accent,
        Color secondary)
    {
        var length = Math.Min(width, height) * 0.11;
        var primary = new Pen(new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B)), 5);
        var soft = new Pen(new SolidColorBrush(Color.FromArgb(90, secondary.R, secondary.G, secondary.B)), 2);
        var left = inset;
        var top = inset;
        var right = width - inset;
        var bottom = height - inset;

        DrawCorner(drawing, new Point(left, top), 1, 1, length, primary, soft);
        DrawCorner(drawing, new Point(right, top), -1, 1, length, primary, soft);
        DrawCorner(drawing, new Point(left, bottom), 1, -1, length, primary, soft);
        DrawCorner(drawing, new Point(right, bottom), -1, -1, length, primary, soft);
    }

    private static void DrawCorner(
        DrawingContext drawing,
        Point origin,
        int horizontalDirection,
        int verticalDirection,
        double length,
        Pen primary,
        Pen soft)
    {
        drawing.DrawLine(
            primary,
            origin,
            new Point(origin.X + (length * horizontalDirection), origin.Y));
        drawing.DrawLine(
            primary,
            origin,
            new Point(origin.X, origin.Y + (length * verticalDirection)));

        const double offset = 9;
        var softOrigin = new Point(
            origin.X + (offset * horizontalDirection),
            origin.Y + (offset * verticalDirection));
        drawing.DrawLine(
            soft,
            softOrigin,
            new Point(softOrigin.X + ((length * 0.62) * horizontalDirection), softOrigin.Y));
        drawing.DrawLine(
            soft,
            softOrigin,
            new Point(softOrigin.X, softOrigin.Y + ((length * 0.62) * verticalDirection)));
    }

    private static void DrawStageAccents(
        DrawingContext drawing,
        int width,
        int height,
        double inset,
        Color accent,
        Color secondary)
    {
        var center = width / 2.0;
        var half = width * 0.19;
        var primary = new Pen(new SolidColorBrush(Color.FromArgb(175, accent.R, accent.G, accent.B)), 4);
        var soft = new Pen(new SolidColorBrush(Color.FromArgb(95, secondary.R, secondary.G, secondary.B)), 2);
        var top = inset;
        var bottom = height - inset;

        drawing.DrawLine(primary, new Point(center - half, top), new Point(center + half, top));
        drawing.DrawLine(primary, new Point(center - half, bottom), new Point(center + half, bottom));
        drawing.DrawLine(soft, new Point(center - (half * 0.62), top + 9), new Point(center + (half * 0.62), top + 9));
        drawing.DrawLine(soft, new Point(center - (half * 0.62), bottom - 9), new Point(center + (half * 0.62), bottom - 9));
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

    private readonly record struct QuizPalette(
        Color A,
        Color B,
        Color C,
        Color D,
        Color Panel,
        Color Panel2,
        Color Inner,
        Color Timer,
        Color FramePrimary,
        Color FrameSecondary);
}
