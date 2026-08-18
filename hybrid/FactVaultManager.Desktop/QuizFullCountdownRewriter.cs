using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizFullCountdownRewriter
{
    public static void Apply(NativeTimeline timeline, QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.ShowCountdown || options.QuestionSeconds <= 3)
            return;

        foreach (var track in timeline.Tracks.Where(track => track.Kind == NativeTimelineTrackKind.Video))
        {
            var leadClips = track.Clips
                .Where(clip => string.Equals(MetadataText(clip, "quiz_card"), "question", StringComparison.Ordinal))
                .ToList();

            foreach (var lead in leadClips)
            {
                if (string.IsNullOrWhiteSpace(lead.Source) || !File.Exists(lead.Source))
                    continue;

                var questionId = MetadataText(lead, "question_id");
                var scene = timeline.Scenes.FirstOrDefault(candidate =>
                    candidate.ClipIds.Contains(lead.Id) ||
                    string.Equals(MetadataText(candidate, "question_id"), questionId, StringComparison.Ordinal));

                track.Clips.Remove(lead);
                scene?.ClipIds.Remove(lead.Id);

                var cursor = lead.Start;
                for (var remaining = options.QuestionSeconds; remaining >= 4; remaining--)
                {
                    var countdownPath = BuildCountdownStill(lead.Source, remaining, options);
                    var replacement = track.AddClip(new NativeTimelineClip
                    {
                        Kind = NativeTimelineClipKind.Image,
                        Start = cursor,
                        Duration = 1,
                        Source = countdownPath,
                        Name = $"{lead.Name} Countdown {remaining}",
                        Metadata = new Dictionary<string, object?>(lead.Metadata)
                        {
                            ["quiz_card"] = "countdown",
                            ["seconds_remaining"] = remaining,
                        },
                    });
                    scene?.ClipIds.Add(replacement.Id);
                    cursor += 1;
                }
            }
        }

        timeline.Validate();
    }

    public static IReadOnlyList<int> Values(int questionSeconds) =>
        questionSeconds <= 0
            ? Array.Empty<int>()
            : Enumerable.Range(1, questionSeconds).Reverse().ToArray();

    private static string BuildCountdownStill(string source, int remaining, QuizVideoBuildOptions options)
    {
        var fullSource = Path.GetFullPath(source);
        var directory = Path.GetDirectoryName(fullSource)!;
        var destination = Path.Combine(directory, $"full_countdown_{remaining}_{Path.GetFileName(fullSource)}");
        if (File.Exists(destination))
            return destination;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(fullSource, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var root = new Grid { Width = options.Width, Height = options.Height };
        root.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });

        var countdownColor = ResolveCountdownColor(directory);
        var header = new Grid
        {
            Margin = options.Vertical
                ? new Thickness(76, 100, 76, 0)
                : new Thickness(120, 58, 120, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var countdown = new TextBlock
        {
            Text = remaining.ToString(),
            Foreground = new SolidColorBrush(countdownColor),
            FontSize = options.Vertical ? 54 : 44,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(countdown, 1);
        header.Children.Add(countdown);
        root.Children.Add(header);

        root.Measure(new Size(options.Width, options.Height));
        root.Arrange(new Rect(0, 0, options.Width, options.Height));
        root.UpdateLayout();
        var rendered = new RenderTargetBitmap(options.Width, options.Height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return destination;
    }

    private static Color ResolveCountdownColor(string cardsDirectory)
    {
        try
        {
            var projectFolder = Directory.GetParent(cardsDirectory)?.FullName;
            var quizPath = projectFolder is null ? null : Path.Combine(projectFolder, "quiz.json");
            if (!string.IsNullOrWhiteSpace(quizPath) && File.Exists(quizPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(quizPath));
                if (document.RootElement.TryGetProperty("theme", out var themeElement))
                {
                    var themeKey = themeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(themeKey))
                        return QuizVisualThemeCatalog.Resolve(themeKey).Countdown;
                }
            }
        }
        catch (JsonException)
        {
        }

        return QuizVisualThemeCatalog.Resolve("dark").Countdown;
    }

    private static string MetadataText(NativeTimelineClip clip, string key) =>
        clip.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";

    private static string MetadataText(NativeTimelineScene scene, string key) =>
        scene.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";
}
