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

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(248, 15, 23, 42)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(options.Vertical ? 18 : 14),
            Width = options.Vertical ? 240 : 190,
            Height = options.Vertical ? 82 : 64,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = options.Vertical ? new Thickness(0, 110, 76, 0) : new Thickness(0, 66, 120, 0),
            Child = new TextBlock
            {
                Text = $"{remaining} SEC",
                Foreground = new SolidColorBrush(Color.FromRgb(254, 240, 138)),
                FontSize = options.Vertical ? 42 : 34,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        root.Children.Add(badge);

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

    private static string MetadataText(NativeTimelineClip clip, string key) =>
        clip.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";

    private static string MetadataText(NativeTimelineScene scene, string key) =>
        scene.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";
}
