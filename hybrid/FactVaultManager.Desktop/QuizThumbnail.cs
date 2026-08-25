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
            $"CAN YOU GET {questionCount}/{questionCount}?",
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

        var dimensions = Dimensions(vertical);
        var card = BuildThumbnail(metadata, questions, thumbnail, visual, logoPath, vertical);
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
        return path;
    }

    private static FrameworkElement BuildThumbnail(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        string? logoPath,
        bool vertical)
    {
        if (vertical)
            return BuildShortsThumbnail(metadata, questions, thumbnail, visual, logoPath);

        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = new Grid
        {
            Width = Width,
            Height = Height,
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

        var rays = new Canvas { IsHitTestVisible = false, Opacity = 0.33 };
        var centerX = 1040.0;
        var centerY = 366.0;
        for (var index = 0; index < 24; index++)
        {
            var angle = (Math.PI * 2.0 * index / 24.0) + 0.04;
            var length = 900.0;
            rays.Children.Add(new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX + Math.Cos(angle) * length,
                Y2 = centerY + Math.Sin(angle) * length,
                Stroke = (index % 3) switch
                {
                    0 => Brush(theme.Accent),
                    1 => Brush(theme.Countdown),
                    _ => Brush(theme.AccentSoft),
                },
                StrokeThickness = index % 2 == 0 ? 7 : 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        root.Children.Add(rays);

        root.Children.Add(new Ellipse
        {
            Width = 760,
            Height = 760,
            Fill = new RadialGradientBrush(
                Color.FromArgb(130, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, -250, 0),
            IsHitTestVisible = false,
        });

        var page = new Grid { Margin = new Thickness(62, 42, 58, 42) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var eyebrow = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 7, 13, 57)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(22, 9, 22, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Glow(theme.Accent, 18, 0.65),
        };
        eyebrow.Child = new TextBlock
        {
            Text = BrandLabel(),
            Foreground = Brushes.White,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
        };
        page.Children.Add(eyebrow);

        var middle = new Grid { Margin = new Thickness(0, 24, 0, 18) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        Grid.SetRow(middle, 1);
        page.Children.Add(middle);

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 30, 0),
        };
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Headline.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = HeadlineFontSize(thumbnail.Headline.Length),
            FontWeight = FontWeights.Black,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 102,
            MaxWidth = 775,
            Effect = Glow(Colors.Black, 12, 0.7),
        });
        var topic = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 7, 13, 57)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 10, 22, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 20, 0, 0),
            Effect = Glow(theme.Countdown, 16, 0.7),
        };
        topic.Child = new TextBlock
        {
            Text = thumbnail.Subtitle.ToUpperInvariant(),
            Foreground = Brush(theme.Countdown),
            FontSize = 38,
            FontWeight = FontWeights.Black,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 730,
        };
        copy.Children.Add(topic);
        middle.Children.Add(copy);

        var challenge = new Grid
        {
            Width = 340,
            Height = 340,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Glow(theme.Accent, 28, 0.85),
        };
        challenge.Children.Add(new Ellipse
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(8, 18, 78), 0),
                    new(Color.FromRgb(20, 32, 112), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Stroke = Brush(theme.Accent),
            StrokeThickness = 10,
        });
        var score = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        score.Children.Add(new TextBlock
        {
            Text = $"{questions.Count}/{questions.Count}",
            Foreground = Brushes.White,
            FontSize = questions.Count >= 100 ? 78 : 96,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        score.Children.Add(new TextBlock
        {
            Text = "CAN YOU DO IT?",
            Foreground = Brush(theme.Countdown),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -6, 0, 0),
        });
        challenge.Children.Add(score);
        Grid.SetColumn(challenge, 1);
        middle.Children.Add(challenge);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var countBadge = new Border
        {
            Background = Brush(theme.Accent),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(19, 9, 19, 9),
            Effect = Glow(theme.Accent, 12, 0.55),
        };
        countBadge.Child = new TextBlock
        {
            Text = QuestionCountLabel(questions.Count),
            Foreground = ColorBrightness(theme.Accent) > 145 ? Brushes.Black : Brushes.White,
            FontSize = 22,
            FontWeight = FontWeights.Black,
        };
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
        string? logoPath)
    {
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = new Grid
        {
            Width = ShortsWidth,
            Height = ShortsHeight,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.5),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            ClipToBounds = true,
        };

        var rays = new Canvas { IsHitTestVisible = false, Opacity = 0.33 };
        const double centerX = ShortsWidth / 2.0;
        const double centerY = 1080;
        for (var index = 0; index < 24; index++)
        {
            var angle = (Math.PI * 2.0 * index / 24.0) + 0.04;
            const double length = 1450;
            rays.Children.Add(new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX + Math.Cos(angle) * length,
                Y2 = centerY + Math.Sin(angle) * length,
                Stroke = (index % 3) switch
                {
                    0 => Brush(theme.Accent),
                    1 => Brush(theme.Countdown),
                    _ => Brush(theme.AccentSoft),
                },
                StrokeThickness = index % 2 == 0 ? 8 : 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        root.Children.Add(rays);

        root.Children.Add(new Ellipse
        {
            Width = 1250,
            Height = 1250,
            Fill = new RadialGradientBrush(
                Color.FromArgb(125, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        });

        var page = new Grid { Margin = new Thickness(68, 90, 68, 105) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var eyebrow = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 7, 13, 57)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(30, 14, 30, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Glow(theme.Accent, 22, 0.7),
            Child = new TextBlock
            {
                Text = BrandLabel(),
                Foreground = Brushes.White,
                FontSize = 34,
                FontWeight = FontWeights.Bold,
            },
        };
        page.Children.Add(eyebrow);

        var copy = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 105, 0, 55),
        };
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Headline.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = Math.Min(112, HeadlineFontSize(thumbnail.Headline.Length)),
            FontWeight = FontWeights.Black,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 125,
            MaxWidth = 930,
            Effect = Glow(Colors.Black, 14, 0.75),
        });
        copy.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 7, 13, 57)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(28, 14, 28, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 38, 0, 0),
            Effect = Glow(theme.Countdown, 20, 0.75),
            Child = new TextBlock
            {
                Text = thumbnail.Subtitle.ToUpperInvariant(),
                Foreground = Brush(theme.Countdown),
                FontSize = 50,
                FontWeight = FontWeights.Black,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 850,
            },
        });
        Grid.SetRow(copy, 1);
        page.Children.Add(copy);

        var challenge = new Grid
        {
            Width = 620,
            Height = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Glow(theme.Accent, 36, 0.9),
        };
        challenge.Children.Add(new Ellipse
        {
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(8, 18, 78), 0),
                    new(Color.FromRgb(20, 32, 112), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Stroke = Brush(theme.Accent),
            StrokeThickness = 15,
        });
        var score = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        score.Children.Add(new TextBlock
        {
            Text = $"{questions.Count}/{questions.Count}",
            Foreground = Brushes.White,
            FontSize = questions.Count >= 100 ? 130 : 168,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        score.Children.Add(new TextBlock
        {
            Text = "CAN YOU DO IT?",
            Foreground = Brush(theme.Countdown),
            FontSize = 42,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -8, 0, 0),
        });
        challenge.Children.Add(score);
        Grid.SetRow(challenge, 2);
        page.Children.Add(challenge);

        var countBadge = new Border
        {
            Background = Brush(theme.Accent),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(28, 14, 28, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Glow(theme.Accent, 18, 0.6),
            Child = new TextBlock
            {
                Text = QuestionCountLabel(questions.Count),
                Foreground = ColorBrightness(theme.Accent) > 145 ? Brushes.Black : Brushes.White,
                FontSize = 34,
                FontWeight = FontWeights.Black,
            },
        };
        Grid.SetRow(countBadge, 3);
        page.Children.Add(countBadge);

        root.Children.Add(WithLogo(page, visual, logoPath, vertical: true));
        return root;
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

    private static double HeadlineFontSize(int length) => length switch
    {
        <= 22 => 112,
        <= 34 => 100,
        <= 48 => 86,
        <= 62 => 74,
        _ => 64,
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
