using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizOpeningSequence
{
    public const double IntroSeconds = 2.0;
    public const int StartCountdownSeconds = 3;
    public const int SpinFrameCount = 36;
    public const double SpinFrameSeconds = 1.0 / 30.0;

    public static void RenderAndApply(
        NativeTimeline timeline,
        string projectFolder,
        QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        timeline.Validate();

        if (timeline.Metadata.TryGetValue("opening_sequence_applied", out var applied) &&
            applied is bool alreadyApplied && alreadyApplied)
            return;

        var cardsFolder = Path.Combine(Path.GetFullPath(projectFolder), "Cards");
        Directory.CreateDirectory(cardsFolder);
        var theme = ResolveTheme(projectFolder);

        var spinPaths = new List<string>();
        for (var index = 0; index < SpinFrameCount; index++)
        {
            var progress = index / (double)(SpinFrameCount - 1);
            var eased = 1.0 - Math.Pow(1.0 - progress, 3);
            var angle = -240.0 * (1.0 - eased);
            var scale = 0.55 + (0.45 * eased);
            const double opacity = 1.0;
            var path = Path.Combine(cardsFolder, $"000_intro_spin_{index:00}.png");
            RenderCard(BuildIntroCard(options, theme, angle, scale, opacity), path, options.Width, options.Height);
            spinPaths.Add(path);
        }

        var countdownStyle = $"opening-large-v2|{theme.Accent}|{theme.Countdown}|{theme.AccentSoft}|{theme.Text}";
        var countdownPaths = new Dictionary<int, string>();
        for (var value = options.OpeningCountdownSeconds; value >= 1; value--)
        {
            var path = Path.Combine(cardsFolder, $"000_start_{value}.png");
            var cachePath = QuizSharedAssetCache.OpeningCountdownPath(
                value,
                options.Width,
                options.Height,
                options.Vertical,
                options.QuizLogoPath,
                countdownStyle);
            cachePath = QuizSharedAssetCache.GetOrCreate(cachePath, temporary =>
                RenderCard(BuildCountdownCard(options, theme, value), temporary, options.Width, options.Height));
            QuizSharedAssetCache.CopyToProject(cachePath, path);
            countdownPaths[value] = path;
        }

        QuizOpeningTimelinePlanner.Apply(timeline, spinPaths, countdownPaths, options.OpeningCountdownSeconds);
        QuizOutroSequence.RenderAndApply(timeline, projectFolder, options);
        timeline.Metadata["opening_sequence_applied"] = true;
        timeline.Metadata["opening_countdown_seconds"] = options.OpeningCountdownSeconds;
        timeline.Metadata["opening_spin_frames"] = SpinFrameCount;
        timeline.Validate();
    }

    private static QuizVisualTheme ResolveTheme(string projectFolder)
    {
        var path = Path.Combine(projectFolder, "quiz.json");
        if (File.Exists(path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("theme", out var themeElement))
                {
                    var key = themeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                        return QuizVisualThemeCatalog.Resolve(key);
                }
            }
            catch (JsonException)
            {
            }
        }
        return QuizVisualThemeCatalog.Resolve("dark");
    }

    private static FrameworkElement BuildIntroCard(
        QuizVideoBuildOptions options,
        QuizVisualTheme theme,
        double angle,
        double scale,
        double opacity)
    {
        var root = CardRoot(options, theme, transparent: true);
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = CardMargin(options),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Opacity = opacity,
        };
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(scale, scale));
        transforms.Children.Add(new RotateTransform(angle));
        content.RenderTransform = transforms;

        AddCenteredLogo(content, options, options.Vertical ? 340 : 285, options.Vertical ? 900 : 820);
        content.Children.Add(new Border
        {
            Background = Brush(Color.FromArgb(110, 8, 15, 65)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(options.Vertical ? 40 : 34, options.Vertical ? 15 : 12, options.Vertical ? 40 : 34, options.Vertical ? 15 : 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, options.Vertical ? 20 : 14, 0, options.Vertical ? 28 : 18),
            Child = new TextBlock
            {
                Text = "QUIZ TIME",
                Foreground = Brush(theme.Accent),
                FontSize = options.Vertical ? 50 : 40,
                FontWeight = FontWeights.Bold,
            },
        });
        content.Children.Add(new TextBlock
        {
            Text = options.Title,
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 96 : 90,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = options.Width * 0.88,
        });
        content.Children.Add(new TextBlock
        {
            Text = "ARE YOU READY?",
            Foreground = Brush(theme.AccentSoft),
            FontSize = options.Vertical ? 46 : 38,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, options.Vertical ? 28 : 20, 0, 0),
        });
        root.Child = content;
        return root;
    }

    private static FrameworkElement BuildCountdownCard(
        QuizVideoBuildOptions options,
        QuizVisualTheme theme,
        int value)
    {
        var root = CardRoot(options, theme, transparent: true);
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = CardMargin(options),
        };
        AddCenteredLogo(content, options, options.Vertical ? 280 : 250, options.Vertical ? 760 : 730);
        content.Children.Add(new Border
        {
            Background = Brush(Color.FromArgb(135, 8, 15, 65)),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(options.Vertical ? 34 : 30, options.Vertical ? 12 : 10, options.Vertical ? 34 : 30, options.Vertical ? 12 : 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, options.Vertical ? 22 : 16, 0, options.Vertical ? 16 : 10),
            Child = new TextBlock
            {
                Text = "FIRST QUESTION STARTS IN",
                Foreground = Brushes.White,
                FontSize = options.Vertical ? 42 : 34,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
            },
        });
        content.Children.Add(new Border
        {
            Width = options.Vertical ? 330 : 260,
            Height = options.Vertical ? 330 : 260,
            Background = Brush(Color.FromArgb(155, 8, 15, 65)),
            BorderBrush = Brush(theme.Countdown),
            BorderThickness = new Thickness(options.Vertical ? 12 : 9),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = value.ToString(),
                Foreground = Brush(theme.Countdown),
                FontSize = options.Vertical ? 225 : 180,
                FontWeight = FontWeights.Black,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        content.Children.Add(new TextBlock
        {
            Text = "GET READY!",
            Foreground = Brushes.White,
            FontSize = options.Vertical ? 58 : 48,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, options.Vertical ? 18 : 12, 0, 0),
        });
        root.Child = content;
        return root;
    }

    private static Border CardRoot(QuizVideoBuildOptions options, QuizVisualTheme theme, bool transparent = false)
    {
        var gradient = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Blend(theme.Background, theme.Accent, 0.18), 0),
                new(theme.Background, 0.48),
                new(Blend(theme.Background, Colors.Black, 0.30), 1),
            },
            new Point(0, 0),
            new Point(1, 1));
        return new Border
        {
            Width = options.Width,
            Height = options.Height,
            Background = transparent ? Brushes.Transparent : gradient,
            BorderBrush = Brush(Color.FromArgb(95, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
            BorderThickness = new Thickness(options.Vertical ? 10 : 7),
        };
    }

    private static void AddCenteredLogo(StackPanel content, QuizVideoBuildOptions options, double height, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(options.QuizLogoPath))
            return;
        var logoPath = QuizBranding.ValidateLogoPath(options.QuizLogoPath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        content.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            Height = height,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            SnapsToDevicePixels = true,
        });
    }

    private static Thickness CardMargin(QuizVideoBuildOptions options) => options.Vertical
        ? new Thickness(64, 72, 64, 72)
        : new Thickness(96, 38, 96, 38);

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(left.R + ((right.R - left.R) * amount)),
            (byte)Math.Round(left.G + ((right.G - left.G) * amount)),
            (byte)Math.Round(left.B + ((right.B - left.B) * amount)));
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private static void RenderCard(FrameworkElement card, string destination, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        card.Measure(new Size(width, height));
        card.Arrange(new Rect(0, 0, width, height));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}

public static class QuizOpeningTimelinePlanner
{
    public static void Apply(
        NativeTimeline timeline,
        IReadOnlyList<string> spinFramePaths,
        IReadOnlyDictionary<int, string> countdownPaths,
        int countdownSeconds = QuizOpeningSequence.StartCountdownSeconds)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(spinFramePaths);
        ArgumentNullException.ThrowIfNull(countdownPaths);
        timeline.Validate();
        if (countdownSeconds is < 0 or > QuizOpeningSequence.StartCountdownSeconds)
            throw new ArgumentOutOfRangeException(nameof(countdownSeconds));
        if (spinFramePaths.Count != QuizOpeningSequence.SpinFrameCount)
            throw new ArgumentException($"Expected {QuizOpeningSequence.SpinFrameCount} intro spin frames.", nameof(spinFramePaths));
        for (var value = countdownSeconds; value >= 1; value--)
        {
            if (!countdownPaths.ContainsKey(value))
                throw new ArgumentException($"Missing opening countdown card {value}.", nameof(countdownPaths));
        }

        var videoTrack = timeline.Tracks.FirstOrDefault(track => track.Kind == NativeTimelineTrackKind.Video)
            ?? throw new InvalidOperationException("Quiz timeline has no video track.");
        var intro = videoTrack.Clips.FirstOrDefault(clip =>
            clip.Kind == NativeTimelineClipKind.Image &&
            clip.Metadata.TryGetValue("quiz_card", out var card) &&
            string.Equals(Convert.ToString(card), "intro", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Quiz timeline has no intro card.");
        if (string.IsNullOrWhiteSpace(intro.Source))
            throw new InvalidOperationException("Quiz intro card has no image source.");

        var introStart = intro.Start;
        var introEnd = intro.End;
        var addedSeconds = countdownSeconds;
        foreach (var track in timeline.Tracks)
        {
            foreach (var clip in track.Clips.Where(clip => !ReferenceEquals(clip, intro) && clip.Start >= introEnd - 0.0001))
                clip.Start += addedSeconds;
        }
        foreach (var scene in timeline.Scenes.Where(scene => scene.Start >= introEnd - 0.0001))
            scene.Start += addedSeconds;

        videoTrack.Clips.Remove(intro);
        var cursor = introStart;
        foreach (var path in spinFramePaths)
        {
            videoTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = cursor,
                Duration = QuizOpeningSequence.SpinFrameSeconds,
                Source = path,
                Name = "Quiz Intro Spin",
                Metadata = new() { ["quiz_card"] = "intro_spin" },
            });
            cursor += QuizOpeningSequence.SpinFrameSeconds;
        }

        var holdSeconds = Math.Max(0, intro.Duration - (QuizOpeningSequence.SpinFrameCount * QuizOpeningSequence.SpinFrameSeconds));
        if (holdSeconds > 0)
        {
            videoTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = cursor,
                Duration = holdSeconds,
                Source = spinFramePaths[^1],
                Name = "Quiz Intro",
                Metadata = new() { ["quiz_card"] = "intro" },
            });
        }
        cursor = introEnd;

        for (var value = countdownSeconds; value >= 1; value--)
        {
            videoTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = cursor,
                Duration = 1,
                Source = countdownPaths[value],
                Name = $"Quiz Start Countdown {value}",
                Metadata = new()
                {
                    ["quiz_card"] = "start_countdown",
                    ["seconds_remaining"] = value,
                },
            });
            cursor += 1;
        }

        timeline.Validate();
    }
}
