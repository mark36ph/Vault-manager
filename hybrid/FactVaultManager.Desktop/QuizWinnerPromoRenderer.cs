using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public static class QuizWinnerPromoPaths
{
    public const string FolderName = "Winner";

    public static string Folder(string projectFolder) =>
        Path.Combine(QuizPromoShortPaths.Folder(projectFolder), FolderName);

    public static string Video(string projectFolder, int number) =>
        Path.Combine(Folder(projectFolder), $"Factburst_Winner_Promo_{number:00}.mp4");
}

public static class QuizWinnerPromoPlanner
{
    public static IReadOnlyList<QuizPromoShortPlan> CreateVariants(
        NativeTimeline timeline,
        double sourceVideoDuration,
        double endCardDuration,
        int count = 3)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        count = Math.Clamp(count, 1, 3);
        var primary = QuizPromoShortPlanner.Create(timeline, sourceVideoDuration, endCardDuration);
        var candidates = timeline.Scenes
            .Where(scene => MetadataInt(scene, "question_id") > 0)
            .Where(scene => scene.Start < sourceVideoDuration)
            .GroupBy(scene => MetadataInt(scene, "question_id"))
            .Select(group => group.OrderBy(scene => scene.Start).First())
            .Where(scene => MetadataInt(scene, "question_id") != primary.QuestionId)
            .OrderByDescending(scene => DifficultyRank(MetadataText(scene, "difficulty")))
            .ThenByDescending(scene => scene.Start)
            .ToList();

        var plans = new List<QuizPromoShortPlan>();
        foreach (var scene in candidates)
        {
            var available = sourceVideoDuration - scene.Start;
            var duration = Math.Min(scene.Duration, Math.Min(available, QuizPromoShortPlanner.MaximumDuration - endCardDuration));
            if (duration < 3) continue;
            plans.Add(new QuizPromoShortPlan(
                scene.Start,
                duration,
                endCardDuration,
                scene.Title,
                MetadataInt(scene, "question_id")));
            if (plans.Count >= count) break;
        }
        return plans;
    }

    private static int DifficultyRank(string difficulty) => difficulty.Trim().ToLowerInvariant() switch
    {
        "insane" => 4,
        "hard" => 3,
        "medium" => 2,
        "easy" => 1,
        _ => 0,
    };

    private static string MetadataText(NativeTimelineScene scene, string key) =>
        scene.Metadata.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""
            : "";

    private static int MetadataInt(NativeTimelineScene scene, string key)
    {
        var text = MetadataText(scene, key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}

public sealed record QuizWinnerPromoRenderedVariant(
    int Number,
    int QuestionId,
    string SceneTitle,
    string VideoPath);

public sealed class QuizWinnerPromoRenderer
{
    private const string CardsFolderName = "Cards";

    public async Task<IReadOnlyList<QuizWinnerPromoRenderedVariant>> CreateAsync(
        string sourceVideo,
        string projectFolder,
        string title,
        string sourceVideoUrl,
        string openAiApiKey,
        string quizLogoPath,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        sourceVideo = Path.GetFullPath(sourceVideo ?? "");
        if (!File.Exists(sourceVideo))
            throw new FileNotFoundException("The rendered long-form video is required for Winner promos.", sourceVideo);
        projectFolder = Path.GetFullPath(projectFolder ?? "");
        var timeline = new NativeProjectTimelineStore(projectFolder).Load();
        if (timeline.Width <= timeline.Height)
            throw new InvalidOperationException("Winner promotional Shorts require a landscape full quiz.");

        var outputFolder = QuizWinnerPromoPaths.Folder(projectFolder);
        Directory.CreateDirectory(outputFolder);
        var script = QuizPromoShortScript.DefaultCallToAction;
        string ctaAudio;
        progress?.Invoke("Winner Autopilot: generating the shared Fable call to action...");
        using (var speech = new NativeQuizSpeechProvider(openAiApiKey, voice: "fable"))
            ctaAudio = await speech.GeneratePromoCallToActionAsync(script, outputFolder, cancellationToken);

        var media = new NativeFfmpegTimelineService();
        var ctaDuration = await media.MediaDurationAsync(ctaAudio, cancellationToken);
        var endCardDuration = QuizPromoShortRenderer.EndCardDurationFor(ctaDuration);
        var sourceDuration = await media.MediaDurationAsync(sourceVideo, cancellationToken);
        var plans = QuizWinnerPromoPlanner.CreateVariants(timeline, sourceDuration, endCardDuration, 3);
        if (plans.Count == 0)
            throw new InvalidOperationException("This Winner quiz does not contain another distinct question for an extra promotional Short.");

        var endCard = Path.Combine(outputFolder, "Winner_End_Card.png");
        QuizPromoShortEndCardRenderer.Write(endCard, title, quizLogoPath);
        var hasSourceAudio = await HasAudioAsync(sourceVideo, cancellationToken);
        var results = new List<QuizWinnerPromoRenderedVariant>();
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var number = index + 1;
            progress?.Invoke($"Winner Autopilot: rendering extra promo {number:N0}/{plans.Count:N0} from {plan.SceneTitle}...");
            var visualSource = QuizPromoNativeShortRenderer.LoadVisualSource(projectFolder, title, quizLogoPath, plan.QuestionId);
            var body = await RenderBodyAsync(outputFolder, number, visualSource, plan.SourceDuration, cancellationToken);
            var destination = QuizWinnerPromoPaths.Video(projectFolder, number);
            await RenderVideoAsync(body, sourceVideo, endCard, ctaAudio, destination, plan, hasSourceAudio, cancellationToken);
            results.Add(new QuizWinnerPromoRenderedVariant(number, plan.QuestionId, plan.SceneTitle, destination));
        }
        return results;
    }

    private static async Task<string> RenderBodyAsync(
        string outputFolder,
        int number,
        QuizPromoNativeVisualSource source,
        double bodyDuration,
        CancellationToken cancellationToken)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            throw new InvalidOperationException("Winner promo cards must be rendered on the desktop UI thread.");

        var cardsFolder = Path.Combine(outputFolder, CardsFolderName + $"_{number:00}");
        if (Directory.Exists(cardsFolder)) Directory.Delete(cardsFolder, recursive: true);
        Directory.CreateDirectory(cardsFolder);
        var phases = QuizPromoNativeShortRenderer.BuildCardPhases(
            source.Options,
            source.NarrationSeconds,
            bodyDuration,
            source.NarrationSuspenseSeconds,
            source.AnswerRevealPauseSeconds);
        if (phases.Count == 0)
            throw new InvalidOperationException("The Winner promo question did not produce any native card phases.");

        var renderer = new QuizThemedCardRenderer();
        var cards = new List<(string Path, double Duration)>();
        for (var index = 0; index < phases.Count; index++)
        {
            var phase = phases[index];
            var bitmap = renderer.RenderPreviewBitmap(
                source.Question,
                source.Options,
                source.Visual,
                phase.Kind,
                source.QuestionNumber,
                source.QuestionTotal,
                phase.CountdownValue);
            var path = Path.Combine(cardsFolder, $"{index:000}.png");
            WriteOpaqueCard(bitmap, path, QuizVisualThemeCatalog.Resolve(source.Visual.ThemeKey));
            cards.Add((path, phase.Duration));
        }

        var concatPath = Path.Combine(cardsFolder, "cards.ffconcat");
        WriteConcatFile(concatPath, cards);
        var destination = Path.Combine(outputFolder, $"Winner_Body_{number:00}.mp4");
        var temporary = destination + ".part.mp4";
        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");
        try
        {
            var result = await RunAsync(ffmpeg,
            [
                "-y", "-f", "concat", "-safe", "0", "-i", concatPath,
                "-vf", "fps=30,scale=1080:1920:flags=lanczos,setsar=1,format=yuv420p",
                "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                "-movflags", "+faststart", "-t", F(bodyDuration), temporary,
            ], TimeSpan.FromMinutes(10), cancellationToken);
            if (result.ExitCode != 0)
                throw new NativeFfmpegTimelineException("Could not render Winner promo cards:\n" + LastUsefulLines(result.StdErr));
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new NativeFfmpegTimelineException("FFmpeg did not create the Winner promo body.");
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static async Task RenderVideoAsync(
        string bodyVideo,
        string sourceVideo,
        string endCard,
        string ctaAudio,
        string destination,
        QuizPromoShortPlan plan,
        bool hasSourceAudio,
        CancellationToken cancellationToken)
    {
        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");
        var temporary = destination + ".part.mp4";
        try
        {
            var result = await RunAsync(ffmpeg,
            [
                "-y", "-i", bodyVideo,
                "-i", sourceVideo,
                "-loop", "1", "-t", F(plan.EndCardDuration), "-i", endCard,
                "-i", ctaAudio,
                "-filter_complex", QuizPromoNativeShortRenderer.BuildFilter(plan, hasSourceAudio),
                "-map", "[v]", "-map", "[a]",
                "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart",
                "-t", F(plan.TotalDuration), temporary,
            ], TimeSpan.FromMinutes(20), cancellationToken);
            if (result.ExitCode != 0)
                throw new NativeFfmpegTimelineException("Could not create Winner promotional Short:\n" + LastUsefulLines(result.StdErr));
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new NativeFfmpegTimelineException("FFmpeg did not create the Winner promotional Short.");
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static async Task<bool> HasAudioAsync(string sourceVideo, CancellationToken cancellationToken)
    {
        var ffprobe = TrustedMediaExecutableLocator.Find("ffprobe");
        var result = await RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=index", "-of", "csv=p=0", sourceVideo],
            TimeSpan.FromSeconds(30), cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Trim().Length > 0;
    }

    private static void WriteOpaqueCard(BitmapSource foreground, string destination, QuizVisualTheme theme)
    {
        var root = new Grid
        {
            Width = QuizPromoShortRenderer.Width,
            Height = QuizPromoShortRenderer.Height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Blend(theme.Background, Color.FromRgb(20, 226, 255), 0.36), 0),
                    new(Color.FromRgb(43, 66, 172), 0.48),
                    new(Blend(theme.Background, Color.FromRgb(196, 70, 255), 0.34), 1),
                }, new Point(0, 0), new Point(1, 1)),
        };
        root.Children.Add(new Image
        {
            Source = foreground,
            Stretch = Stretch.Fill,
            Width = QuizPromoShortRenderer.Width,
            Height = QuizPromoShortRenderer.Height,
        });
        root.Measure(new Size(QuizPromoShortRenderer.Width, QuizPromoShortRenderer.Height));
        root.Arrange(new Rect(0, 0, QuizPromoShortRenderer.Width, QuizPromoShortRenderer.Height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            QuizPromoShortRenderer.Width,
            QuizPromoShortRenderer.Height,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void WriteConcatFile(string path, IReadOnlyList<(string Path, double Duration)> cards)
    {
        var builder = new StringBuilder("ffconcat version 1.0\n");
        foreach (var card in cards)
        {
            builder.Append("file '").Append(ConcatPath(card.Path)).AppendLine("'");
            builder.Append("duration ").AppendLine(F(card.Duration));
        }
        builder.Append("file '").Append(ConcatPath(cards[^1].Path)).AppendLine("'");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static async Task<WinnerProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) throw new NativeFfmpegTimelineException($"Could not start {Path.GetFileName(executable)}.");
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            return new WinnerProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new NativeFfmpegTimelineException($"{Path.GetFileNameWithoutExtension(executable)} timed out.", error);
        }
    }

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(255,
            (byte)Math.Round(left.R + ((right.R - left.R) * amount)),
            (byte)Math.Round(left.G + ((right.G - left.G) * amount)),
            (byte)Math.Round(left.B + ((right.B - left.B) * amount)));
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string ConcatPath(string path) => Path.GetFullPath(path).Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal);
    private static string LastUsefulLines(string value) =>
        string.Join(Environment.NewLine, (value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(10));

    private sealed record WinnerProcessResult(int ExitCode, string StdOut, string StdErr);
}
