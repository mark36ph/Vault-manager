using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    public static QuizThumbnailSettings Create(QuizPublishMetadata metadata, int questionCount)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        if (questionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(questionCount));

        return new QuizThumbnailSettings(
            $"CAN YOU SCORE {questionCount}/{questionCount}?",
            $"{metadata.SeriesName} {metadata.EpisodeLabel}").Normalize();
    }
}

public sealed class QuizThumbnailRenderer
{
    public const int Width = 1280;
    public const int Height = 720;

    public BitmapSource RenderPreview(
        QuizPublishMetadata metadata,
        IReadOnlyList<QuizQuestion> questions,
        QuizThumbnailSettings thumbnail,
        QuizVisualRenderSettings visual,
        string? logoPath)
    {
        metadata = QuizPublishMetadataGenerator.Validate(metadata);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one quiz question is required for a thumbnail.", nameof(questions));
        thumbnail = thumbnail.Normalize();
        visual = visual.Normalize();

        var card = BuildThumbnail(metadata, questions, thumbnail, visual, logoPath);
        card.Measure(new Size(Width, Height));
        card.Arrange(new Rect(0, 0, Width, Height));
        card.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
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
        string? logoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var bitmap = RenderPreview(metadata, questions, thumbnail, visual, logoPath);
        var folder = Path.GetFullPath(projectFolder.Trim());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Thumbnail.jpg");
        var temporary = path + ".tmp";

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
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
        string? logoPath)
    {
        var theme = QuizVisualThemeCatalog.Resolve(visual.ThemeKey);
        var root = new Border
        {
            Width = Width,
            Height = Height,
            Background = Brush(theme.Background),
        };

        var page = new Grid { Margin = new Thickness(62, 48, 62, 48) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var eyebrow = new Border
        {
            Background = Brush(theme.Panel),
            BorderBrush = Brush(theme.PanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(18, 8, 18, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        eyebrow.Child = new TextBlock
        {
            Text = $"{metadata.SeriesName.ToUpperInvariant()}  {metadata.EpisodeLabel}",
            Foreground = Brush(theme.Accent),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
        };
        page.Children.Add(eyebrow);

        var middle = new Grid { Margin = new Thickness(0, 28, 0, 26) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        Grid.SetRow(middle, 1);
        page.Children.Add(middle);

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 40, 0),
        };
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Headline,
            Foreground = Brush(theme.Text),
            FontSize = HeadlineFontSize(thumbnail.Headline.Length),
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 94,
            MaxWidth = 790,
        });
        copy.Children.Add(new TextBlock
        {
            Text = thumbnail.Subtitle,
            Foreground = Brush(theme.AccentSoft),
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 22, 0, 0),
            MaxWidth = 780,
        });
        middle.Children.Add(copy);

        var challenge = new Grid
        {
            Width = 320,
            Height = 320,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        challenge.Children.Add(new Border
        {
            Background = Brush(theme.Panel),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(8),
            CornerRadius = new CornerRadius(160),
        });
        challenge.Children.Add(new TextBlock
        {
            Text = "?",
            Foreground = Brush(theme.Accent),
            FontSize = 210,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -20, 0, 0),
        });
        Grid.SetColumn(challenge, 1);
        middle.Children.Add(challenge);

        var categories = questions
            .Select(question => (question.Category ?? "").Trim())
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var categoryText = categories.Length == 0 ? "MIXED TRIVIA" : string.Join("  •  ", categories.Select(value => value.ToUpperInvariant()));
        footer.Children.Add(new TextBlock
        {
            Text = categoryText,
            Foreground = Brush(theme.Muted),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var countBadge = new Border
        {
            Background = Brush(theme.Accent),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 9, 18, 9),
        };
        countBadge.Child = new TextBlock
        {
            Text = $"{questions.Count} QUESTIONS",
            Foreground = Brush(theme.Background),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
        };
        Grid.SetColumn(countBadge, 1);
        footer.Children.Add(countBadge);
        Grid.SetRow(footer, 2);
        page.Children.Add(footer);

        root.Child = WithLogo(page, visual, logoPath);
        return root;
    }

    private static FrameworkElement WithLogo(
        FrameworkElement content,
        QuizVisualRenderSettings visual,
        string? logoPath)
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
        var layout = new Grid();
        layout.Children.Add(content);
        layout.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Height = 62 * visual.LogoScale,
            MaxWidth = 240 * visual.LogoScale,
            Margin = new Thickness(34),
            SnapsToDevicePixels = true,
        });
        return layout;
    }

    private static double HeadlineFontSize(int length) => length switch
    {
        <= 24 => 104,
        <= 40 => 90,
        <= 58 => 78,
        _ => 66,
    };

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
        bool metadataReady,
        bool thumbnailReady,
        bool preflightReady,
        bool exportSettingsReady,
        bool exportCompleted)
    {
        return
        [
            new("Quiz draft", draftQuestionCount > 0, draftQuestionCount > 0 ? $"{draftQuestionCount} questions selected" : "Build a draft"),
            new("YouTube metadata", metadataReady, metadataReady ? "Title, description, hashtags and pinned comment ready" : "Generate or complete metadata"),
            new("Thumbnail", thumbnailReady, thumbnailReady ? "1280×720 thumbnail preview ready" : "Generate the thumbnail preview"),
            new("Preflight", preflightReady, preflightReady ? "No blocking layout errors" : "Resolve layout errors before export"),
            new("Export settings", exportSettingsReady, exportSettingsReady ? "Resolve destination and title ready" : "Check the project folder and export title"),
            new("Upload package", exportCompleted, exportCompleted ? "Resolve export, metadata and thumbnail created" : "Created after a successful Resolve export"),
        ];
    }

    public static string Format(IReadOnlyList<QuizPublishChecklistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return string.Join(Environment.NewLine, items.Select(item => $"{(item.IsComplete ? "✓" : "○")} {item.Label} — {item.Detail}"));
    }
}
