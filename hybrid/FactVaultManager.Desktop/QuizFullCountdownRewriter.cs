using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizFullCountdownRewriter
{
    public static void Apply(
        NativeTimeline timeline,
        IReadOnlyList<QuizQuestion> questions,
        string projectFolder,
        QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.ShowCountdown || options.QuestionSeconds <= 3)
            return;

        var visual = LoadVisualSettings(projectFolder);
        var renderer = new QuizThemedCardRenderer();

        foreach (var track in timeline.Tracks.Where(track => track.Kind == NativeTimelineTrackKind.Video))
        {
            var leadClips = track.Clips
                .Where(clip => string.Equals(MetadataText(clip, "quiz_card"), "question", StringComparison.Ordinal))
                .ToList();

            foreach (var lead in leadClips)
            {
                if (string.IsNullOrWhiteSpace(lead.Source) || !File.Exists(lead.Source))
                    continue;

                var questionIdText = MetadataText(lead, "question_id");
                if (!int.TryParse(questionIdText, out var questionId))
                    continue;
                var questionIndex = questions.ToList().FindIndex(question => question.Id == questionId);
                if (questionIndex < 0)
                    continue;
                var question = questions[questionIndex];

                var scene = timeline.Scenes.FirstOrDefault(candidate =>
                    candidate.ClipIds.Contains(lead.Id) ||
                    string.Equals(MetadataText(candidate, "question_id"), questionIdText, StringComparison.Ordinal));

                track.Clips.Remove(lead);
                scene?.ClipIds.Remove(lead.Id);

                var cursor = lead.Start;
                for (var remaining = options.QuestionSeconds; remaining >= 4; remaining--)
                {
                    var countdownPath = BuildCountdownStill(
                        renderer,
                        lead.Source,
                        question,
                        questionIndex + 1,
                        questions.Count,
                        remaining,
                        options,
                        visual);
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

    private static string BuildCountdownStill(
        QuizThemedCardRenderer renderer,
        string source,
        QuizQuestion question,
        int number,
        int total,
        int remaining,
        QuizVideoBuildOptions options,
        QuizVisualRenderSettings visual)
    {
        var fullSource = Path.GetFullPath(source);
        var directory = Path.GetDirectoryName(fullSource)!;
        var destination = Path.Combine(directory, $"full_countdown_{question.Id}_{remaining}.png");
        if (File.Exists(destination))
            File.Delete(destination);

        var bitmap = renderer.RenderPreviewBitmap(
            question,
            options,
            visual,
            QuizPreviewCardKind.Countdown,
            number,
            total,
            remaining);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return destination;
    }

    private static QuizVisualRenderSettings LoadVisualSettings(string projectFolder)
    {
        try
        {
            var quizPath = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
            if (!File.Exists(quizPath))
                return new QuizVisualRenderSettings();

            using var document = JsonDocument.Parse(File.ReadAllText(quizPath));
            var root = document.RootElement;
            var theme = root.TryGetProperty("theme", out var themeElement)
                ? themeElement.GetString() ?? "dark"
                : "dark";
            var logoPosition = root.TryGetProperty("logo_position", out var positionElement)
                ? positionElement.GetString() ?? "Bottom right"
                : "Bottom right";
            var logoScale = root.TryGetProperty("logo_scale", out var scaleElement) && scaleElement.TryGetDouble(out var parsedScale)
                ? parsedScale
                : 1.0;
            return new QuizVisualRenderSettings(theme, logoPosition, logoScale).Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read quiz visual settings for countdown: {error.Message}");
            return new QuizVisualRenderSettings();
        }
    }

    private static string MetadataText(NativeTimelineClip clip, string key) =>
        clip.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";

    private static string MetadataText(NativeTimelineScene scene, string key) =>
        scene.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";
}
