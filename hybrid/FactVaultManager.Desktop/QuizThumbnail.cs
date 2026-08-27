using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace FactVaultManager.Desktop;

public sealed record QuizThumbnailSettings(
    string Headline,
    string Subtitle)
{
    public const int MaxHeadlineLength = 80;
    public const int MaxSubtitleLength = 120;

    public QuizThumbnailSettings Normalize()
    {
        var headline = (Headline ?? "").Trim();
        var subtitle = (Subtitle ?? "").Trim();
        if (headline.Length == 0)
            throw new ArgumentException("Thumbnail headline is required.", nameof(Headline));
        if (headline.Length > MaxHeadlineLength)
            throw new ArgumentException($"Thumbnail headline must be {MaxHeadlineLength} characters or fewer.", nameof(Headline));
        if (subtitle.Length == 0)
            throw new ArgumentException("Thumbnail subtitle is required.", nameof(Subtitle));
        if (subtitle.Length > MaxSubtitleLength)
            throw new ArgumentException($"Thumbnail subtitle must be {MaxSubtitleLength} characters or fewer.", nameof(Subtitle));
        return this with { Headline = headline, Subtitle = subtitle };
    }
}

public static class QuizThumbnailDefaults
{
    public static bool ShouldReplaceSubtitle(string? currentSubtitle, string? previousAutoSubtitle)
    {
        var current = (currentSubtitle ?? "").Trim();
        var previousAuto = (previousAutoSubtitle ?? "").Trim();
        return current.Length == 0 ||
               (previousAuto.Length > 0 &&
                string.Equals(current, previousAuto, StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(current, "General Knowledge Quiz", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "ICON", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "ICONS", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "ICON QUIZ", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "ICONS QUIZ", StringComparison.OrdinalIgnoreCase);
    }

    public static QuizThumbnailSettings Create(QuizPublishMetadata metadata, int questionCount, bool logoQuiz = false)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        if (questionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(questionCount));

        return new QuizThumbnailSettings(
            QuizThumbnailIntelligence.DefaultHook(questionCount, logoQuiz),
            logoQuiz
                ? "LOGOS"
                : QuizPublishMetadataGenerator.DisplayName(metadata.SeriesName).ToUpperInvariant()).Normalize();
    }
}

public sealed class QuizThumbnailRenderer
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int ShortsWidth = 1080;
    public const int ShortsHeight = 1920;
    public const string FileName = "Thumbnail.png";
    public const double LogoHeight = 70;
    public const double BottomRightLogoBottomMargin = 104;

    public static (int Width, int Height) Dimensions(bool vertical) =>
        vertical ? (ShortsWidth, ShortsHeight) : (Width, Height);

    public static string QuestionCountLabel(int count) =>
        $"{count} {(count == 1 ? "QUESTION" : "QUESTIONS")}";

    public static string BrandLabel() => "FACTBURST QUIZ";

    public BitmapSource RenderPreview(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        string? logoPath,
        bool vertical = false)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one quiz question is required for a thumbnail.", nameof(questions));
        thumbnail = thumbnail.Normalize();
        visual = visual.Normalize();

        var logoQuiz = string.Equals(visual.QuizType, QuizTypeCatalog.Logo, StringComparison.OrdinalIgnoreCase);
        var recommendation = QuizThumbnailIntelligence.Recommend(metadata, questions, logoQuiz);
        thumbnail = UpgradeLegacyAutomaticCopy(thumbnail, recommendation);

        var dimensions = Dimensions(vertical);
        var card = vertical
            ? BuildShortsThumbnail(metadata, questions, thumbnail, visual, logoPath, recommendation)
            : BuildLandscapeThumbnail(metadata, questions, thumbnail, visual, logoPath, recommendation);
        card.Measure(new Size(dimensions.Width, dimensions.Height));
        card.Arrange(new Rect(0, 0, dimensions.Width, dimensions.Height));
        card.UpdateLayout();

        var bitmap = new RenderTargetBitmap(dimensions.Width, dimensions.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }

    public string Write(
        string projectFolder,
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        string? logoPath,
        bool vertical = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var bitmap = RenderPreview(metadata, questions, thumbnail, visual, logoPath, vertical);
        var folder = IOPath.GetFullPath(projectFolder.Trim());
        Directory.CreateDirectory(folder);
        var path = IOPath.Combine(folder, FileName);
        var temporary = path + ".tmp";

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            encoder.Save(stream);
        File.Move(temporary, path, overwrite: true);

        if (!vertical)
        {
            QuizYouTubePackaging.Write(
                folder,
                metadata,
                questions,
                visual,
                logoPath,
                vertical: false);
        }
        return path;
    }

    internal static QuizThumbnailSettings UpgradeLegacyAutomaticCopy(
        QuizThumbnailSettings thumbnail,
        QuizThumbnailRecommendation recommendation)
    {
        thumbnail = thumbnail.Normalize();
        var headline = thumbnail.Headline;
        if (headline.StartsWith("CAN YOU GET ", StringComparison.OrdinalIgnoreCase) &&
            headline.EndsWith("?", StringComparison.Ordinal))
        {
            headline = recommendation.Hook;
        }
        return new QuizThumbnailSettings(headline, thumbnail.Subtitle).Normalize();
    }

    private static FrameworkElement BuildLandscapeThumbnail(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        string? logoPath,
        QuizThumbnailRecommendation recommendation)
    {
        _ = metadata;
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = BuildBackground(Width, Height, theme, vertical: false);
        var page = new Grid { Margin = new Thickness(56, 38, 52, 38) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brandRow = new Grid();
        brandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        brandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        brandRow.Children.Add(BuildBrandPill(theme, 25, 2));
        var badge = BuildPill(
            recommendation.Badge,
            theme.Countdown,
            fontSize: 22,
            borderThickness: 2,
            horizontalAlignment: HorizontalAlignment.Right);
        Grid.SetColumn(badge, 1);
        brandRow.Children.Add(badge);
        page.Children.Add(brandRow);

        var middle = new Grid { Margin = new Thickness(0, 20, 0, 16) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        Grid.SetRow(middle, 1);
        page.Children.Add(middle);

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Headline.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = LandscapeHeadlineFontSize(thumbnail.Headline.Length),
            FontWeight = FontWeights.Black,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 101,
            MaxWidth = 700,
            Effect = Glow(Colors.Black, 14, 0.8),
        });
        copy.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(218, 7, 13, 57)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 10, 22, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 20, 0, 0),
            Effect = Glow(theme.Countdown, 16, 0.7),
            Child = new TextBlock
            {
                Text = thumbnail.Subtitle.ToUpperInvariant(),
                Foreground = Brush(theme.Countdown),
                FontSize = 34,
                FontWeight = FontWeights.Black,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 650,
            },
        });
        middle.Children.Add(copy);

        var feature = BuildFeaturePanel(recommendation, theme, vertical: false);
        Grid.SetColumn(feature, 2);
        middle.Children.Add(feature);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = $"FEATURED: QUESTION {recommendation.QuestionNumber}",
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var countBadge = BuildCountBadge(questions.Count, theme, vertical: false);
        Grid.SetColumn(countBadge, 1);
        footer.Children.Add(countBadge);
        Grid.SetRow(footer, 2);
        page.Children.Add(footer);

        root.Children.Add(WithLogo(page, visual, logoPath));
        return root;
    }

    private static FrameworkElement BuildShortsThumbnail(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        string? logoPath,
        QuizThumbnailRecommendation recommendation)
    {
        _ = metadata;
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = BuildBackground(ShortsWidth, ShortsHeight, theme, vertical: true);
        var page = new Grid { Margin = new Thickness(64, 84, 64, 110) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        page.Children.Add(BuildBrandPill(theme, 34, 3, HorizontalAlignment.Center));

        var copy = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 80, 0, 50),
        };
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Headline.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = ShortsHeadlineFontSize(thumbnail.Headline.Length),
            FontWeight = FontWeights.Black,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 124,
            MaxWidth = 930,
            Effect = Glow(Colors.Black, 16, 0.82),
        });
        copy.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(222, 7, 13, 57)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(28, 14, 28, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 34, 0, 0),
            Effect = Glow(theme.Countdown, 20, 0.75),
            Child = new TextBlock
            {
                Text = thumbnail.Subtitle.ToUpperInvariant(),
                Foreground = Brush(theme.Countdown),
                FontSize = 48,
                FontWeight = FontWeights.Black,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 850,
            },
        });
        Grid.SetRow(copy, 1);
        page.Children.Add(copy);

        var feature = BuildFeaturePanel(recommendation, theme, vertical: true);
        Grid.SetRow(feature, 2);
        page.Children.Add(feature);

        var footer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 42, 0, 0),
        };
        footer.Children.Add(new TextBlock
        {
            Text = $"FEATURED: QUESTION {recommendation.QuestionNumber}",
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });
        footer.Children.Add(BuildCountBadge(questions.Count, theme, vertical: true));
        Grid.SetRow(footer, 3);
        page.Children.Add(footer);

        root.Children.Add(WithLogo(page, visual, logoPath, vertical: true));
        return root;
    }

    private static Grid BuildBackground(int width, int height, QuizVisualTheme theme, bool vertical)
    {
        var root = new Grid
        {
            Width = width,
            Height = height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.48),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            ClipToBounds = true,
        };

        var rays = new Canvas { IsHitTestVisible = false, Opacity = 0.30 };
        var centerX = vertical ? width / 2.0 : width * 0.80;
        var centerY = vertical ? height * 0.58 : height * 0.50;
        var rayLength = vertical ? 1550.0 : 920.0;
        for (var index = 0; index < 24; index++)
        {
            var angle = (Math.PI * 2.0 * index / 24.0) + 0.04;
            rays.Children.Add(new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX + Math.Cos(angle) * rayLength,
                Y2 = centerY + Math.Sin(angle) * rayLength,
                Stroke = (index % 3) switch
                {
                    0 => Brush(theme.Accent),
                    1 => Brush(theme.Countdown),
                    _ => Brush(theme.AccentSoft),
                },
                StrokeThickness = vertical ? (index % 2 == 0 ? 8 : 4) : (index % 2 == 0 ? 7 : 3),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        root.Children.Add(rays);

        root.Children.Add(new Ellipse
        {
            Width = vertical ? 1280 : 780,
            Height = vertical ? 1280 : 780,
            Fill = new RadialGradientBrush(
                Color.FromArgb(125, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                Colors.Transparent),
            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = vertical ? new Thickness(0) : new Thickness(0, 0, -260, 0),
            IsHitTestVisible = false,
        });
        return root;
    }

    private static Border BuildFeaturePanel(
        QuizThumbnailRecommendation recommendation,
        QuizVisualTheme theme,
        bool vertical)
    {
        var panel = new Border
        {
            Width = vertical ? 840 : 410,
            MinHeight = vertical ? 720 : 455,
            Background = new SolidColorBrush(Color.FromArgb(230, 7, 13, 57)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(vertical ? 5 : 4),
            CornerRadius = new CornerRadius(vertical ? 34 : 24),
            Padding = new Thickness(vertical ? 34 : 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(theme.Accent, vertical ? 30 : 22, 0.72),
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(BuildPill(
            recommendation.Badge,
            theme.Countdown,
            vertical ? 31 : 20,
            vertical ? 3 : 2,
            HorizontalAlignment.Center));

        var artwork = BuildFeaturedArtwork(recommendation, theme, vertical);
        artwork.Margin = new Thickness(0, vertical ? 28 : 20, 0, vertical ? 28 : 18);
        stack.Children.Add(artwork);

        stack.Children.Add(new TextBlock
        {
            Text = recommendation.Teaser,
            Foreground = Brushes.White,
            FontSize = vertical ? 42 : 27,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = vertical ? 760 : 360,
            MaxHeight = vertical ? 210 : 126,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "WHAT'S YOUR ANSWER?",
            Foreground = Brush(theme.Countdown),
            FontSize = vertical ? 29 : 18,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, vertical ? 24 : 16, 0, 0),
        });
        panel.Child = stack;
        return panel;
    }

    private static FrameworkElement BuildFeaturedArtwork(
        QuizThumbnailRecommendation recommendation,
        QuizVisualTheme theme,
        bool vertical)
    {
        var width = vertical ? 560.0 : 300.0;
        var height = vertical ? 330.0 : 210.0;
        if (recommendation.HasArtwork && TryLoadBitmap(recommendation.Question.ImagePath, out var bitmap))
        {
            return new Border
            {
                Width = width,
                Height = height,
                Background = Brushes.White,
                BorderBrush = Brush(theme.Countdown),
                BorderThickness = new Thickness(vertical ? 5 : 3),
                CornerRadius = new CornerRadius(vertical ? 26 : 18),
                Padding = new Thickness(vertical ? 22 : 14),
                Effect = Glow(theme.Countdown, vertical ? 22 : 16, 0.6),
                Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                },
            };
        }

        var glyph = new Grid
        {
            Width = width,
            Height = height,
        };
        glyph.Children.Add(new Ellipse
        {
            Width = vertical ? 300 : 180,
            Height = vertical ? 300 : 180,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(8, 18, 78), 0),
                    new(Color.FromRgb(31, 41, 135), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Stroke = Brush(theme.Countdown),
            StrokeThickness = vertical ? 9 : 6,
            Effect = Glow(theme.Countdown, vertical ? 28 : 20, 0.78),
        });
        glyph.Children.Add(new TextBlock
        {
            Text = "?",
            Foreground = Brushes.White,
            FontSize = vertical ? 205 : 128,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, vertical ? -24 : -16, 0, 0),
            Effect = Glow(Colors.Black, 10, 0.7),
        });
        return glyph;
    }

    private static bool TryLoadBitmap(string? path, out BitmapImage bitmap)
    {
        bitmap = null!;
        try
        {
            var full = IOPath.GetFullPath((path ?? "").Trim());
            if (!File.Exists(full))
                return false;
            var loaded = new BitmapImage();
            loaded.BeginInit();
            loaded.CacheOption = BitmapCacheOption.OnLoad;
            loaded.UriSource = new Uri(full, UriKind.Absolute);
            loaded.EndInit();
            loaded.Freeze();
            bitmap = loaded;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException or FileFormatException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load thumbnail artwork: {error.Message}");
            return false;
        }
    }

    private static Border BuildBrandPill(
        QuizVisualTheme theme,
        double fontSize,
        double borderThickness,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left) =>
        BuildPill(BrandLabel(), theme.Accent, fontSize, borderThickness, horizontalAlignment);

    private static Border BuildPill(
        string text,
        Color accent,
        double fontSize,
        double borderThickness,
        HorizontalAlignment horizontalAlignment)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 7, 13, 57)),
            BorderBrush = Brush(accent),
            BorderThickness = new Thickness(borderThickness),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(fontSize * 0.8, fontSize * 0.32, fontSize * 0.8, fontSize * 0.32),
            HorizontalAlignment = horizontalAlignment,
            Effect = Glow(accent, 16, 0.62),
            Child = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeights.Black,
                TextAlignment = TextAlignment.Center,
            },
        };
    }

    private static Border BuildCountBadge(int count, QuizVisualTheme theme, bool vertical)
    {
        return new Border
        {
            Background = Brush(theme.Accent),
            CornerRadius = new CornerRadius(vertical ? 18 : 12),
            Padding = vertical ? new Thickness(28, 14, 28, 14) : new Thickness(19, 9, 19, 9),
            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right,
            Effect = Glow(theme.Accent, vertical ? 18 : 12, 0.58),
            Child = new TextBlock
            {
                Text = QuestionCountLabel(count),
                Foreground = ColorBrightness(theme.Accent) > 145 ? Brushes.Black : Brushes.White,
                FontSize = vertical ? 34 : 22,
                FontWeight = FontWeights.Black,
            },
        };
    }

    private static FrameworkElement WithLogo(
        FrameworkElement content,
        QuizVisualRenderSettings visual,
        string? logoPath,
        bool vertical = false)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
            return content;

        var path = QuizBranding.ValidateLogoPath(logoPath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var position = QuizLogoPositionCatalog.Normalize(visual.LogoPosition);
        var top = position.StartsWith("Top", StringComparison.OrdinalIgnoreCase);
        var left = position.EndsWith("left", StringComparison.OrdinalIgnoreCase);
        var bottomRight = !top && !left;
        var edgeMargin = vertical ? 50 : 34;
        var bottomMargin = vertical ? 170 : BottomRightLogoBottomMargin;
        var margin = bottomRight
            ? new Thickness(edgeMargin, edgeMargin, edgeMargin, bottomMargin)
            : new Thickness(edgeMargin);
        var layout = new Grid();
        layout.Children.Add(content);
        layout.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Height = (vertical ? 105 : LogoHeight) * visual.LogoScale,
            MaxWidth = (vertical ? 320 : 250) * visual.LogoScale,
            Margin = margin,
            SnapsToDevicePixels = true,
            Effect = Glow(Colors.White, 10, 0.35),
        });
        return layout;
    }

    private static double LandscapeHeadlineFontSize(int length) => length switch
    {
        <= 18 => 116,
        <= 24 => 104,
        <= 32 => 92,
        <= 42 => 82,
        _ => 70,
    };

    private static double ShortsHeadlineFontSize(int length) => length switch
    {
        <= 18 => 120,
        <= 24 => 110,
        <= 32 => 100,
        <= 42 => 90,
        _ => 80,
    };

    private static DropShadowEffect Glow(Color color, double radius, double opacity) => new()
    {
        Color = color,
        BlurRadius = radius,
        ShadowDepth = 0,
        Opacity = opacity,
    };

    private static double ColorBrightness(Color color) =>
        (color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114);

    private static SolidColorBrush Brush(Color color) => new(color);
}

public sealed record QuizPublishChecklistItem(
    string Label,
    bool IsComplete,
    string Detail);

public static class QuizPublishChecklist
{
    public static IReadOnlyList<QuizPublishChecklistItem> Evaluate(
        int draftQuestionCount,
        bool youtubeTitleReady,
        bool descriptionReady,
        bool hashtagsReady,
        bool pinnedCommentReady,
        bool thumbnailReady,
        bool resolveExportReady,
        bool historyRecorded)
    {
        return
        [
            new("Quiz draft", draftQuestionCount > 0, draftQuestionCount > 0 ? $"{draftQuestionCount} questions selected" : "Build a draft"),
            new("YouTube title", youtubeTitleReady, youtubeTitleReady ? "Ready" : "Add a valid YouTube title"),
            new("Description", descriptionReady, descriptionReady ? "Ready" : "Add a valid description"),
            new("Hashtags", hashtagsReady, hashtagsReady ? "Ready" : "Add at least one valid hashtag"),
            new("Pinned comment", pinnedCommentReady, pinnedCommentReady ? "Ready" : "Add a valid pinned comment"),
            new("Thumbnail", thumbnailReady, thumbnailReady ? "1280×720 thumbnail ready" : "Generate the thumbnail preview"),
            new("Resolve export", resolveExportReady, resolveExportReady ? "Resolve/FCPXML package created" : "Created after a successful Resolve export"),
            new("Quiz History entry", historyRecorded, historyRecorded ? "Successful export recorded" : "Recorded after a successful Resolve export"),
        ];
    }

    public static IReadOnlyList<QuizPublishChecklistItem> Evaluate(
        int draftQuestionCount,
        bool metadataReady,
        bool thumbnailReady,
        bool preflightReady,
        bool exportSettingsReady,
        bool exportCompleted)
    {
        _ = preflightReady;
        _ = exportSettingsReady;
        return Evaluate(
            draftQuestionCount,
            metadataReady,
            metadataReady,
            metadataReady,
            metadataReady,
            thumbnailReady,
            exportCompleted,
            exportCompleted);
    }

    public static string Format(IReadOnlyList<QuizPublishChecklistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return string.Join(Environment.NewLine, items.Select(item => $"{(item.IsComplete ? "✓" : "○")} {item.Label} — {item.Detail}"));
    }
}
