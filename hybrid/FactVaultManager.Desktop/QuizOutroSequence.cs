using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FactVaultManager.Desktop;

public static class QuizOutroSequence
{
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

        if (timeline.Metadata.TryGetValue("outro_sequence_applied", out var applied) &&
            applied is bool alreadyApplied && alreadyApplied)
            return;

        var videoTrack = timeline.Tracks.FirstOrDefault(track => track.Kind == NativeTimelineTrackKind.Video)
            ?? throw new InvalidOperationException("Quiz timeline has no video track.");
        var outro = videoTrack.Clips
            .Where(clip => clip.Kind == NativeTimelineClipKind.Image)
            .Where(clip => clip.Metadata.TryGetValue("quiz_card", out var card) &&
                string.Equals(Convert.ToString(card), "outro", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(clip => clip.Start)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Quiz timeline has no outro card.");

        if (string.IsNullOrWhiteSpace(outro.Source) || !File.Exists(outro.Source))
            throw new InvalidOperationException("Quiz outro card has no image source.");

        var cardsFolder = IOPath.Combine(IOPath.GetFullPath(projectFolder), "Cards");
        Directory.CreateDirectory(cardsFolder);

        var enhancedOutroPath = IOPath.Combine(cardsFolder, "999_outro_youtube.png");
        RenderEnhancedOutro(outro.Source, enhancedOutroPath, options);
        outro.Source = enhancedOutroPath;
        outro.Name = "Quiz Outro YouTube CTA";

        var spinStart = outro.End;
        for (var index = 0; index < SpinFrameCount; index++)
        {
            var progress = index / (double)(SpinFrameCount - 1);
            var eased = progress * progress * (3.0 - (2.0 * progress));
            var angle = 360.0 * eased;
            var scale = 1.0 - (0.92 * eased);
            var framePath = IOPath.Combine(cardsFolder, $"999_outro_spin_{index:00}.png");
            RenderSpinFrame(options, framePath, angle, scale);
            videoTrack.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Image,
                Start = spinStart + (index * SpinFrameSeconds),
                Duration = SpinFrameSeconds,
                Source = framePath,
                Name = "Quiz Logo Spin Out",
                Metadata = new()
                {
                    ["quiz_card"] = "outro_spin",
                    ["spin_frame"] = index,
                },
            });
        }

        timeline.Metadata["outro_sequence_applied"] = true;
        timeline.Metadata["outro_spin_frames"] = SpinFrameCount;
        timeline.Validate();
    }

    private static void RenderEnhancedOutro(string source, string destination, QuizVideoBuildOptions options)
    {
        var sourceBitmap = LoadBitmap(source);
        var root = new Grid { Width = options.Width, Height = options.Height };
        root.Children.Add(new Image { Source = sourceBitmap, Stretch = Stretch.Fill });

        var youtube = new Border
        {
            Width = options.Vertical ? 220 : 190,
            Height = options.Vertical ? 150 : 128,
            CornerRadius = new CornerRadius(options.Vertical ? 34 : 28),
            Background = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, options.Vertical ? 250 : 145, 0, 0),
        };
        var play = new Polygon
        {
            Fill = Brushes.White,
            Points = new PointCollection
            {
                new(0, 0),
                new(options.Vertical ? 68 : 58, options.Vertical ? 42 : 36),
                new(0, options.Vertical ? 84 : 72),
            },
            Stretch = Stretch.Uniform,
            Width = options.Vertical ? 70 : 60,
            Height = options.Vertical ? 84 : 72,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        youtube.Child = play;
        root.Children.Add(youtube);

        RenderCard(root, destination, options.Width, options.Height);
    }

    private static void RenderSpinFrame(
        QuizVideoBuildOptions options,
        string destination,
        double angle,
        double scale)
    {
        var root = new Grid
        {
            Width = options.Width,
            Height = options.Height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(18, 32, 52), 0),
                    new(Color.FromRgb(8, 18, 35), 0.55),
                    new(Color.FromRgb(2, 8, 18), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
        };

        if (!string.IsNullOrWhiteSpace(options.QuizLogoPath))
        {
            var logoPath = QuizBranding.ValidateLogoPath(options.QuizLogoPath);
            var logo = new Image
            {
                Source = LoadBitmap(logoPath),
                Stretch = Stretch.Uniform,
                Width = options.Width * (options.Vertical ? 0.90 : 0.66),
                Height = options.Height * (options.Vertical ? 0.44 : 0.46),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(scale, scale));
            transforms.Children.Add(new RotateTransform(angle));
            logo.RenderTransform = transforms;
            root.Children.Add(logo);
        }

        RenderCard(root, destination, options.Width, options.Height);
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(IOPath.GetFullPath(path), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static void RenderCard(FrameworkElement card, string destination, int width, int height)
    {
        Directory.CreateDirectory(IOPath.GetDirectoryName(destination)!);
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
