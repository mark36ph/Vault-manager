using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FactVaultManager.Desktop;

public enum QuizYouTubeThumbnailLayout
{
    ScoreChallenge,
    ExpertChallenge,
    CategorySearch,
}

/// <summary>
/// Purpose-built landscape thumbnails for YouTube Test & Compare packaging.
/// Each layout tests a genuinely different click hypothesis rather than only swapping copy.
/// </summary>
public sealed class QuizYouTubePackagingThumbnailRenderer
{
    public const int Width = 1280;
    public const int Height = 720;

    public BitmapSource Render(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        QuizYouTubeThumbnailLayout layout,
        string? logoPath = null)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one quiz question is required for YouTube packaging.", nameof(questions));

        thumbnail = thumbnail.Normalize();
        visual = visual.Normalize();
        _ = logoPath; // Packaging uses one clear FACTBURST QUIZ brand mark only.

        var logoQuiz = string.Equals(visual.QuizType, QuizTypeCatalog.Logo, StringComparison.OrdinalIgnoreCase);
        var recommendation = QuizThumbnailIntelligence.Recommend(metadata, questions, logoQuiz);
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);

        var root = BuildBackground(theme, layout);
        root.Children.Add(layout switch
        {
            QuizYouTubeThumbnailLayout.ScoreChallenge => BuildScoreLayout(
                questions.Count,
                thumbnail,
                recommendation,
                theme),
            QuizYouTubeThumbnailLayout.ExpertChallenge => BuildExpertLayout(
                questions.Count,
                thumbnail,
                theme),
            QuizYouTubeThumbnailLayout.CategorySearch => BuildCategoryLayout(
                questions.Count,
                thumbnail,
                theme),
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown YouTube thumbnail layout."),
        });

        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static FrameworkElement BuildScoreLayout(
        int questionCount,
        QuizThumbnailSettings thumbnail,
        QuizThumbnailRecommendation recommendation,
        QuizVisualTheme theme)
    {
        var page = BuildPage();
        page.Children.Add(BuildHeader(theme, QuizThumbnailRenderer.QuestionCountLabel(questionCount)));

        var middle = new Grid { Margin = new Thickness(0, 24, 0, 18) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        Grid.SetRow(middle, 1);

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        copy.Children.Add(Headline(thumbnail.Headline, HeadlineSize(thumbnail.Headline, 88, 74, 64), 700));
        copy.Children.Add(BuildPill(
            thumbnail.Subtitle.ToUpperInvariant(),
            theme.Countdown,
            fontSize: 34,
            margin: new Thickness(0, 22, 0, 0)));
        middle.Children.Add(copy);

        var preview = BuildQuestionPreview(recommendation, theme);
        Grid.SetColumn(preview, 2);
        middle.Children.Add(preview);
        page.Children.Add(middle);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        page.Children.Add(footer);
        return page;
    }

    private static FrameworkElement BuildExpertLayout(
        int questionCount,
        QuizThumbnailSettings thumbnail,
        QuizVisualTheme theme)
    {
        var page = BuildPage();
        page.Children.Add(BuildHeader(theme, QuizThumbnailRenderer.QuestionCountLabel(questionCount)));

        var middle = new Grid { Margin = new Thickness(0, 22, 0, 14) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
        Grid.SetRow(middle, 1);

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 28, 0),
        };
        copy.Children.Add(Headline(thumbnail.Headline, HeadlineSize(thumbnail.Headline, 94, 78, 64), 760));
        middle.Children.Add(copy);

        var challenge = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        challenge.Children.Add(BuildQuestionMark(theme, 270, 136));
        challenge.Children.Add(BuildPill(
            thumbnail.Subtitle.ToUpperInvariant(),
            theme.Accent,
            fontSize: 30,
            margin: new Thickness(0, 22, 0, 0),
            center: true));
        Grid.SetColumn(challenge, 1);
        middle.Children.Add(challenge);
        page.Children.Add(middle);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        page.Children.Add(footer);
        return page;
    }

    private static FrameworkElement BuildCategoryLayout(
        int questionCount,
        QuizThumbnailSettings thumbnail,
        QuizVisualTheme theme)
    {
        var page = BuildPage();
        page.Children.Add(BuildHeader(theme, QuizThumbnailRenderer.QuestionCountLabel(questionCount)));

        var middle = new Grid { Margin = new Thickness(0, 20, 0, 14) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        Grid.SetRow(middle, 1);

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 30, 0),
        };
        copy.Children.Add(Headline(thumbnail.Headline, HeadlineSize(thumbnail.Headline, 100, 82, 66), 760));
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Subtitle.ToUpperInvariant(),
            Foreground = new SolidColorBrush(theme.Countdown),
            FontSize = 42,
            FontWeight = FontWeights.Black,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 16, 0, 0),
            Effect = Glow(Colors.Black, 11, 0.65),
        });
        copy.Children.Add(new TextBlock
        {
            Text = "TEST YOUR KNOWLEDGE",
            Foreground = Brushes.White,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4, 18, 0, 0),
            Opacity = 0.86,
        });
        middle.Children.Add(copy);

        var cluster = BuildQuestionCluster(theme);
        Grid.SetColumn(cluster, 1);
        middle.Children.Add(cluster);
        page.Children.Add(middle);

        var footer = new Grid();
        footer.Children.Add(BuildPill(
            "PLAY ALONG • KEEP SCORE",
            theme.Accent,
            fontSize: 24,
            margin: new Thickness(0)));
        Grid.SetRow(footer, 2);
        page.Children.Add(footer);
        return page;
    }

    private static Grid BuildPage()
    {
        var page = new Grid { Margin = new Thickness(56, 38, 52, 38) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return page;
    }

    private static FrameworkElement BuildHeader(QuizVisualTheme theme, string rightText)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(BuildPill(
            QuizThumbnailRenderer.BrandLabel(),
            theme.Accent,
            fontSize: 25,
            margin: new Thickness(0)));

        var badge = BuildPill(
            rightText.ToUpperInvariant(),
            theme.Countdown,
            fontSize: 22,
            margin: new Thickness(0));
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);
        return header;
    }

    private static FrameworkElement BuildQuestionPreview(
        QuizThumbnailRecommendation recommendation,
        QuizVisualTheme theme)
    {
        var panel = new Border
        {
            Width = 430,
            MinHeight = 410,
            Padding = new Thickness(28, 26, 28, 24),
            Background = new SolidColorBrush(Color.FromArgb(238, 5, 19, 66)),
            BorderBrush = new SolidColorBrush(theme.Accent),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(22),
            Effect = Glow(theme.Accent, 18, 0.62),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(BuildQuestionMark(theme, 160, 84));
        content.Children.Add(new TextBlock
        {
            Text = recommendation.Teaser,
            Foreground = Brushes.White,
            FontSize = 27,
            FontWeight = FontWeights.ExtraBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            Margin = new Thickness(0, 28, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = "WHAT'S YOUR ANSWER?",
            Foreground = new SolidColorBrush(theme.Countdown),
            FontSize = 20,
            FontWeight = FontWeights.Black,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        });
        panel.Child = content;
        return panel;
    }

    private static FrameworkElement BuildQuestionMark(QuizVisualTheme theme, double size, double fontSize)
    {
        var holder = new Grid
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        holder.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(224, 20, 37, 126)),
            Stroke = new SolidColorBrush(theme.Countdown),
            StrokeThickness = Math.Max(5, size / 38),
            Effect = Glow(theme.Countdown, 18, 0.72),
        });
        holder.Children.Add(new TextBlock
        {
            Text = "?",
            Foreground = Brushes.White,
            FontSize = fontSize,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -10, 0, 0),
            Effect = Glow(Colors.Black, 8, 0.7),
        });
        return holder;
    }

    private static FrameworkElement BuildQuestionCluster(QuizVisualTheme theme)
    {
        var canvas = new Canvas
        {
            Width = 410,
            Height = 390,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AddBubble(canvas, 205, 70, 190, 96, theme.Countdown);
        AddBubble(canvas, 22, 36, 142, 70, theme.Accent);
        AddBubble(canvas, 62, 245, 118, 58, theme.AccentSoft);
        return canvas;
    }

    private static void AddBubble(
        Canvas canvas,
        double left,
        double top,
        double size,
        double fontSize,
        Color stroke)
    {
        var bubble = new Grid { Width = size, Height = size };
        bubble.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(224, 9, 26, 84)),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 5,
            Effect = Glow(stroke, 18, 0.66),
        });
        bubble.Children.Add(new TextBlock
        {
            Text = "?",
            Foreground = Brushes.White,
            FontSize = fontSize,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -8, 0, 0),
        });
        Canvas.SetLeft(bubble, left);
        Canvas.SetTop(bubble, top);
        canvas.Children.Add(bubble);
    }

    private static Grid BuildFooter()
    {
        var footer = new Grid();
        footer.Children.Add(new TextBlock
        {
            Text = "PLAY ALONG • KEEP SCORE",
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return footer;
    }

    private static TextBlock Headline(string text, double fontSize, double maxWidth) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = Brushes.White,
        FontSize = fontSize,
        FontWeight = FontWeights.Black,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = fontSize * 1.03,
        MaxWidth = maxWidth,
        Effect = Glow(Colors.Black, 14, 0.78),
    };

    private static Border BuildPill(
        string text,
        Color accent,
        double fontSize,
        Thickness margin,
        bool center = false)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(222, 7, 13, 57)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(2.5),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(20, 8, 20, 8),
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Margin = margin,
            Effect = Glow(accent, 13, 0.62),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeights.Black,
                TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            },
        };
    }

    private static Grid BuildBackground(QuizVisualTheme theme, QuizYouTubeThumbnailLayout layout)
    {
        var root = new Grid
        {
            Width = Width,
            Height = Height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(5, 12, 52), 0),
                    new(Color.FromRgb(18, 34, 115), 0.52),
                    new(Color.FromRgb(69, 29, 135), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            ClipToBounds = true,
        };

        var centerX = layout switch
        {
            QuizYouTubeThumbnailLayout.ScoreChallenge => 1030.0,
            QuizYouTubeThumbnailLayout.ExpertChallenge => 930.0,
            _ => 990.0,
        };
        var centerY = layout == QuizYouTubeThumbnailLayout.CategorySearch ? 355.0 : 360.0;
        var rays = new Canvas { IsHitTestVisible = false, Opacity = 0.28 };
        for (var index = 0; index < 22; index++)
        {
            var angle = (Math.PI * 2 * index / 22.0) + 0.05;
            rays.Children.Add(new Line
            {
                X1 = centerX,
                Y1 = centerY,
                X2 = centerX + Math.Cos(angle) * 930,
                Y2 = centerY + Math.Sin(angle) * 930,
                Stroke = new SolidColorBrush((index % 3) switch
                {
                    0 => theme.Accent,
                    1 => theme.Countdown,
                    _ => theme.AccentSoft,
                }),
                StrokeThickness = index % 2 == 0 ? 7 : 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        root.Children.Add(rays);
        root.Children.Add(new Ellipse
        {
            Width = layout == QuizYouTubeThumbnailLayout.CategorySearch ? 850 : 760,
            Height = layout == QuizYouTubeThumbnailLayout.CategorySearch ? 850 : 760,
            Fill = new RadialGradientBrush(
                Color.FromArgb(118, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, -130, 0),
        });
        return root;
    }

    private static double HeadlineSize(string value, double shortSize, double mediumSize, double longSize)
    {
        var length = (value ?? string.Empty).Length;
        if (length <= 18)
            return shortSize;
        if (length <= 30)
            return mediumSize;
        return longSize;
    }

    private static DropShadowEffect Glow(Color color, double blur, double opacity) => new()
    {
        Color = color,
        BlurRadius = blur,
        ShadowDepth = 0,
        Opacity = opacity,
    };
}
