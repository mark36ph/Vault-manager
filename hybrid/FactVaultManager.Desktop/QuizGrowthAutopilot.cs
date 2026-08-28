using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizGrowthPlaylistPlanner
{
    public static string Category(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return QuizYouTubeAnalytics.CategoryName(history).Trim();
    }

    public static string Description(string category)
    {
        category = (category ?? "").Trim();
        if (category.Length == 0)
            throw new ArgumentException("Category is required.", nameof(category));
        return $"Play the Factburst {category} quiz series and see how high you can score. New quizzes are added automatically.";
    }

    public static YouTubePlaylistItem? FindExisting(
        string category,
        IEnumerable<YouTubePlaylistItem> playlists)
    {
        category = (category ?? "").Trim();
        if (category.Length == 0)
            throw new ArgumentException("Category is required.", nameof(category));
        ArgumentNullException.ThrowIfNull(playlists);

        var canonical = YouTubeCategoryPlaylistPlanner.PlaylistTitle(category);
        return playlists.FirstOrDefault(playlist =>
            string.Equals(playlist.Title.Trim(), canonical, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(playlist.Title.Trim(), category + " Quiz", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(playlist.Title.Trim(), category, StringComparison.OrdinalIgnoreCase));
    }

    public static string VideoId(string url)
    {
        url = (url ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "";

        var host = uri.Host.TrimEnd('.');
        if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        if (!string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
            return "";

        if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "";
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "v", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]).Trim();
        }
        return "";
    }
}

public static class QuizGrowthEndScreen
{
    public const double SafeSeconds = 15.0;

    public static void Apply(NativeTimeline timeline, string projectFolder, QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (options.Vertical)
            return;

        timeline.Validate();
        var outro = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .SelectMany(track => track.Clips)
            .Where(clip => clip.Kind == NativeTimelineClipKind.Image)
            .Where(clip => clip.Metadata.TryGetValue("quiz_card", out var card) &&
                string.Equals(Convert.ToString(card), "outro", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(clip => clip.Start)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Quiz timeline has no outro card for the growth end screen.");

        foreach (var track in timeline.Tracks.Where(track => track.Kind == NativeTimelineTrackKind.Video))
        {
            track.Clips.RemoveAll(clip =>
                clip.Metadata.TryGetValue("quiz_card", out var card) &&
                string.Equals(Convert.ToString(card), "outro_spin", StringComparison.OrdinalIgnoreCase));
        }

        var cardsFolder = IOPath.Combine(IOPath.GetFullPath(projectFolder), "Cards");
        Directory.CreateDirectory(cardsFolder);
        var path = IOPath.Combine(cardsFolder, "999_outro_growth_end_screen.png");
        Render(path, options);

        outro.Source = path;
        outro.Name = "Quiz Growth End Screen";
        outro.Duration = SafeSeconds;
        outro.Metadata["end_screen_safe"] = true;
        outro.Metadata["end_screen_safe_seconds"] = SafeSeconds;

        timeline.Metadata["growth_end_screen"] = true;
        timeline.Metadata["growth_end_screen_safe_seconds"] = SafeSeconds;
        timeline.Metadata["outro_spin_frames"] = 0;
        timeline.Validate();
    }

    private static void Render(string destination, QuizVideoBuildOptions options)
    {
        var root = new Grid
        {
            Width = options.Width,
            Height = options.Height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(10, 22, 55), 0),
                    new(Color.FromRgb(24, 36, 83), 0.55),
                    new(Color.FromRgb(6, 13, 34), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(100, 58, 100, 0),
        };
        stack.Children.Add(new TextBlock
        {
            Text = "KEEP PLAYING",
            Foreground = Brushes.White,
            FontSize = 54,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Choose your next Factburst quiz",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 255)),
            FontSize = 25,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        root.Children.Add(stack);

        var slots = new Grid
        {
            Margin = new Thickness(175, 235, 175, 175),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        slots.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        slots.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        slots.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(slots);

        slots.Children.Add(Slot("NEXT QUIZ"));
        var playlist = Slot("QUIZ PLAYLIST");
        Grid.SetColumn(playlist, 2);
        slots.Children.Add(playlist);

        if (!string.IsNullOrWhiteSpace(options.QuizLogoPath) && File.Exists(options.QuizLogoPath))
        {
            var logo = new Image
            {
                Source = LoadBitmap(options.QuizLogoPath),
                Stretch = Stretch.Uniform,
                Width = 230,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 48),
                Opacity = 0.92,
            };
            root.Children.Add(logo);
        }

        RenderCard(root, destination, options.Width, options.Height);
    }

    private static Border Slot(string label)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 202, 45)),
            FontWeight = FontWeights.Bold,
            FontSize = 20,
            Margin = new Thickness(8, 0, 0, 10),
        });
        var empty = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(91, 142, 255)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(16),
        };
        Grid.SetRow(empty, 1);
        grid.Children.Add(empty);
        return new Border { Child = grid };
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
