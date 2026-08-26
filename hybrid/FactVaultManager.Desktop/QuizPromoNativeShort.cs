using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

internal sealed record QuizPromoNativeVisualSource(
    QuizQuestion Question,
    QuizVideoBuildOptions Options,
    QuizVisualRenderSettings Visual,
    int QuestionNumber,
    int QuestionTotal,
    double NarrationSeconds);

internal sealed record QuizPromoNativeCardPhase(
    QuizPreviewCardKind Kind,
    int? CountdownValue,
    double Duration);

public sealed class QuizPromoNativeShortRenderer
{
    public const string VisualStyle = "native_factburst_short";
    private const string BodyFileName = "Promo_Short_Body.mp4";
    private const string CardsFolderName = "Cards";
    private const string ConcatFileName = "cards.ffconcat";

    public async Task<QuizPromoShortResult> CreateAsync(
        string sourceVideo,
        string projectFolder,
        string title,
        string sourceVideoUrl,
        string callToAction,
        string openAiApiKey,
        string quizLogoPath,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        sourceVideo = Path.GetFullPath(sourceVideo ?? "");
        if (!File.Exists(sourceVideo))
            throw new FileNotFoundException("Choose the final rendered long-form video first.", sourceVideo);

        projectFolder = Path.GetFullPath(projectFolder ?? "");
        var timeline = new NativeProjectTimelineStore(projectFolder).Load();
        if (timeline.Width <= timeline.Height)
            throw new InvalidOperationException("Promotional Shorts can only be created from a landscape long-form quiz.");

        var outputFolder = QuizPromoShortPaths.Folder(projectFolder);
        Directory.CreateDirectory(outputFolder);
        var script = QuizPromoShortScript.Normalize(callToAction);

        progress?.Invoke("Generating the Fable promotional hook...");
        string ctaAudio;
        using (var speech = new NativeQuizSpeechProvider(openAiApiKey, voice: "fable"))
            ctaAudio = await speech.GeneratePromoCallToActionAsync(script, outputFolder, cancellationToken);

        var media = new NativeFfmpegTimelineService();
        var ctaDuration = await media.MediaDurationAsync(ctaAudio, cancellationToken);
        var endCardDuration = QuizPromoShortRenderer.EndCardDurationFor(ctaDuration);
        var sourceDuration = await media.MediaDurationAsync(sourceVideo, cancellationToken);
        var plan = QuizPromoShortPlanner.Create(timeline, sourceDuration, endCardDuration);

        progress?.Invoke($"Rebuilding {plan.SceneTitle} in the Factburst Shorts layout...");
        var visualSource = LoadVisualSource(projectFolder, title, quizLogoPath, plan.QuestionId);
        var bodyVideo = await RenderBodyAsync(
            outputFolder,
            visualSource,
            plan.SourceDuration,
            cancellationToken);

        var endCard = QuizPromoShortPaths.EndCard(projectFolder);
        QuizPromoShortEndCardRenderer.Write(endCard, title, quizLogoPath);
        var destination = QuizPromoShortPaths.Video(projectFolder);
        var hasSourceAudio = await HasAudioAsync(sourceVideo, cancellationToken);

        progress?.Invoke("Combining the native Short visuals with the original quiz audio...");
        await RenderVideoAsync(
            bodyVideo,
            sourceVideo,
            endCard,
            ctaAudio,
            destination,
            plan,
            hasSourceAudio,
            cancellationToken);

        var metadataPath = QuizPromoShortPaths.Metadata(projectFolder);
        WriteMetadata(
            metadataPath,
            sourceVideo,
            sourceVideoUrl,
            script,
            ctaAudio,
            quizLogoPath,
            plan,
            visualSource);
        progress?.Invoke("Promotional Short ready in the Factburst Shorts style.");
        return new QuizPromoShortResult(destination, endCard, ctaAudio, metadataPath, plan);
    }

    internal static QuizPromoNativeVisualSource LoadVisualSource(
        string projectFolder,
        string fallbackTitle,
        string quizLogoPath,
        int questionId)
    {
        var quizPath = Path.Combine(Path.GetFullPath(projectFolder), "quiz.json");
        if (!File.Exists(quizPath))
            throw new FileNotFoundException("The saved quiz.json is required to rebuild the promo in the Shorts layout.", quizPath);

        using var document = JsonDocument.Parse(File.ReadAllText(quizPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The saved quiz does not contain its question list.");

        JsonElement selected = default;
        var selectedNumber = 0;
        var total = questions.GetArrayLength();
        for (var index = 0; index < total; index++)
        {
            var candidate = questions[index];
            if (Int(candidate, "id", 0) != questionId) continue;
            selected = candidate;
            selectedNumber = Int(candidate, "number", index + 1);
            break;
        }
        if (selectedNumber == 0)
            throw new InvalidDataException($"Question #{questionId} from the Insane scene was not found in quiz.json.");

        if (!selected.TryGetProperty("answers", out var answersElement) ||
            answersElement.ValueKind != JsonValueKind.Array ||
            answersElement.GetArrayLength() != 4)
        {
            throw new InvalidDataException("The promotional question must contain exactly four saved answers.");
        }

        var answers = answersElement.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "")
            .ToArray();
        if (answers.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The promotional question contains an empty saved answer.");

        var imagePath = Text(selected, "image_path", "");
        if (imagePath.Length > 0 && !Path.IsPathRooted(imagePath))
            imagePath = Path.Combine(projectFolder, imagePath);
        if (imagePath.Length > 0 && !File.Exists(imagePath))
            imagePath = "";

        var question = new QuizQuestion(
            questionId,
            RequiredText(selected, "question"),
            answers[0],
            answers[1],
            answers[2],
            answers[3],
            Math.Clamp(Int(selected, "correct_index", 0), 0, 3),
            Text(selected, "explanation", ""),
            Text(selected, "category", "General Knowledge"),
            Text(selected, "difficulty", "insane"),
            "Saved quiz project",
            0,
            true,
            imagePath);

        var savedTitle = Text(root, "title", fallbackTitle);
        if (string.IsNullOrWhiteSpace(savedTitle))
            savedTitle = "Factburst Quiz";
        var questionSeconds = Math.Clamp(Int(root, "question_seconds", 8), 2, 60);
        var answerSeconds = Math.Clamp(Int(root, "answer_seconds", 3), 1, 15);
        var frameRate = Double(root, "frame_rate", 30);
        if (!double.IsFinite(frameRate) || frameRate <= 0 || frameRate > 120)
            frameRate = 30;
        var showCountdown = Bool(root, "show_countdown", true);
        var animateReveal = Bool(root, "animate_answer_reveal", true);

        var options = new QuizVideoBuildOptions(
            savedTitle,
            questionSeconds,
            answerSeconds,
            Vertical: true,
            FrameRate: frameRate,
            QuizLogoPath: quizLogoPath,
            ShowCountdown: showCountdown,
            AnimateAnswerReveal: animateReveal);

        var requestedQuizType = QuizTypeCatalog.Normalize(Text(root, "quiz_type", question.QuizType));
        if (requestedQuizType == QuizTypeCatalog.Logo && string.IsNullOrWhiteSpace(question.ImagePath))
            requestedQuizType = QuizTypeCatalog.Standard;
        var visual = new QuizVisualRenderSettings(
            Text(root, "theme", "dark"),
            Text(root, "logo_position", "Bottom right"),
            Double(root, "logo_scale", 1.0),
            requestedQuizType).Normalize();

        var narrationSeconds = 0.0;
        if (selected.TryGetProperty("narration", out var narration) && narration.ValueKind == JsonValueKind.Object)
            narrationSeconds = Math.Max(0, Double(narration, "duration", 0));

        return new QuizPromoNativeVisualSource(
            question,
            options,
            visual,
            Math.Clamp(selectedNumber, 1, Math.Max(1, total)),
            Math.Max(1, total),
            narrationSeconds);
    }

    internal static IReadOnlyList<QuizPromoNativeCardPhase> BuildCardPhases(
        QuizVideoBuildOptions options,
        double narrationSeconds,
        double targetDuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!double.IsFinite(narrationSeconds) || narrationSeconds < 0)
            narrationSeconds = 0;
        if (!double.IsFinite(targetDuration) || targetDuration <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetDuration));

        var phases = new List<QuizPromoNativeCardPhase>();
        var questionLead = Math.Max(0, options.QuestionSeconds - options.CountdownSeconds);
        var questionCardDuration = narrationSeconds + questionLead;
        if (questionCardDuration > 0)
            phases.Add(new QuizPromoNativeCardPhase(QuizPreviewCardKind.Question, null, questionCardDuration));

        for (var remaining = options.CountdownSeconds; remaining >= 1; remaining--)
            phases.Add(new QuizPromoNativeCardPhase(QuizPreviewCardKind.Countdown, remaining, 1));

        if (options.RevealEmphasisSeconds > 0)
            phases.Add(new QuizPromoNativeCardPhase(
                QuizPreviewCardKind.AnswerReveal,
                null,
                options.RevealEmphasisSeconds));

        var steadyAnswer = options.AnswerSeconds - options.RevealEmphasisSeconds;
        if (steadyAnswer > 0)
            phases.Add(new QuizPromoNativeCardPhase(QuizPreviewCardKind.Explanation, null, steadyAnswer));

        return FitPhasesToDuration(phases, targetDuration);
    }

    internal static string BuildFilter(QuizPromoShortPlan plan, bool hasSourceAudio)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var bodyDuration = F(plan.SourceDuration);
        var endDuration = F(plan.EndCardDuration);
        var bodyAudio = hasSourceAudio
            ? $"[1:a]atrim=start={F(plan.SourceStart)}:duration={bodyDuration},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a0];"
            : $"anullsrc=r=48000:cl=stereo:d={bodyDuration}[a0];";
        return
            $"[0:v]trim=duration={bodyDuration},setpts=PTS-STARTPTS,scale=1080:1920,setsar=1,fps=30,format=yuv420p[v0];" +
            bodyAudio +
            $"[2:v]trim=duration={endDuration},setpts=PTS-STARTPTS,scale=1080:1920,setsar=1,fps=30,format=yuv420p[v1];" +
            $"[3:a]atrim=duration={endDuration},asetpts=PTS-STARTPTS,apad=whole_dur={endDuration},aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a1];" +
            "[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]";
    }

    private static IReadOnlyList<QuizPromoNativeCardPhase> FitPhasesToDuration(
        IReadOnlyList<QuizPromoNativeCardPhase> phases,
        double targetDuration)
    {
        var fitted = new List<QuizPromoNativeCardPhase>();
        var remaining = targetDuration;
        foreach (var phase in phases)
        {
            if (remaining <= 0.000001) break;
            var duration = Math.Min(phase.Duration, remaining);
            if (duration > 0.000001)
                fitted.Add(phase with { Duration = duration });
            remaining -= duration;
        }

        if (remaining > 0.000001)
        {
            if (fitted.Count == 0)
                fitted.Add(new QuizPromoNativeCardPhase(QuizPreviewCardKind.Question, null, remaining));
            else
                fitted[^1] = fitted[^1] with { Duration = fitted[^1].Duration + remaining };
        }
        return fitted;
    }

    private static async Task<string> RenderBodyAsync(
        string outputFolder,
        QuizPromoNativeVisualSource source,
        double bodyDuration,
        CancellationToken cancellationToken)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            throw new InvalidOperationException("Promo Short cards must be rendered on the desktop UI thread.");

        var cardsFolder = Path.Combine(outputFolder, CardsFolderName);
        if (Directory.Exists(cardsFolder))
            Directory.Delete(cardsFolder, recursive: true);
        Directory.CreateDirectory(cardsFolder);

        var phases = BuildCardPhases(source.Options, source.NarrationSeconds, bodyDuration);
        if (phases.Count == 0)
            throw new InvalidOperationException("The saved Insane question did not produce any Short card phases.");

        var renderer = new QuizThemedCardRenderer();
        var cardFiles = new List<(string Path, double Duration)>();
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
            var path = Path.Combine(cardsFolder, $"{index:000}_{PhaseName(phase)}.png");
            WriteOpaqueCard(bitmap, path, QuizVisualThemeCatalog.Resolve(source.Visual.ThemeKey));
            cardFiles.Add((path, phase.Duration));
        }

        var concatPath = Path.Combine(cardsFolder, ConcatFileName);
        WriteConcatFile(concatPath, cardFiles);
        var destination = Path.Combine(outputFolder, BodyFileName);
        var temporary = destination + ".part.mp4";
        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");
        try
        {
            var arguments = new[]
            {
                "-y",
                "-f", "concat",
                "-safe", "0",
                "-i", concatPath,
                "-vf", "fps=30,scale=1080:1920:flags=lanczos,setsar=1,format=yuv420p",
                "-an",
                "-c:v", "libx264",
                "-preset", "medium",
                "-crf", "18",
                "-movflags", "+faststart",
                "-t", F(bodyDuration),
                temporary,
            };
            var result = await RunAsync(ffmpeg, arguments, TimeSpan.FromMinutes(10), cancellationToken);
            if (result.ExitCode != 0)
                throw new NativeFfmpegTimelineException(
                    "Could not render the native Shorts-style promo body:\n" + LastUsefulLines(result.StdErr));
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new NativeFfmpegTimelineException("FFmpeg completed without creating the native Shorts-style promo body.");
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static void WriteOpaqueCard(BitmapSource foreground, string destination, QuizVisualTheme theme)
    {
        var blue = Color.FromRgb(20, 226, 255);
        var purple = Color.FromRgb(196, 70, 255);
        var root = new Grid
        {
            Width = QuizPromoShortRenderer.Width,
            Height = QuizPromoShortRenderer.Height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Blend(theme.Background, blue, 0.36), 0),
                    new(Color.FromRgb(43, 66, 172), 0.48),
                    new(Blend(theme.Background, purple, 0.34), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
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
            96,
            96,
            PixelFormats.Pbgra32);
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
            var arguments = new[]
            {
                "-y",
                "-i", bodyVideo,
                "-i", sourceVideo,
                "-loop", "1",
                "-t", F(plan.EndCardDuration),
                "-i", endCard,
                "-i", ctaAudio,
                "-filter_complex", BuildFilter(plan, hasSourceAudio),
                "-map", "[v]",
                "-map", "[a]",
                "-c:v", "libx264",
                "-preset", "medium",
                "-crf", "18",
                "-c:a", "aac",
                "-b:a", "192k",
                "-movflags", "+faststart",
                "-t", F(plan.TotalDuration),
                temporary,
            };
            var result = await RunAsync(ffmpeg, arguments, TimeSpan.FromMinutes(20), cancellationToken);
            if (result.ExitCode != 0)
                throw new NativeFfmpegTimelineException(
                    "Could not create the promotional Short:\n" + LastUsefulLines(result.StdErr));
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new NativeFfmpegTimelineException("FFmpeg completed without creating the promotional Short.");
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
        var result = await RunAsync(
            ffprobe,
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=index", "-of", "csv=p=0", sourceVideo],
            TimeSpan.FromSeconds(30),
            cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Trim().Length > 0;
    }

    private static void WriteMetadata(
        string path,
        string sourceVideo,
        string sourceVideoUrl,
        string script,
        string ctaAudio,
        string quizLogoPath,
        QuizPromoShortPlan plan,
        QuizPromoNativeVisualSource visualSource)
    {
        JsonNode? existingYouTubeUpload = null;
        try
        {
            if (File.Exists(path))
                existingYouTubeUpload = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)?["youtube_upload"]?.DeepClone();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Could not preserve promo Short upload metadata: {error.Message}");
        }

        var payload = new JsonObject
        {
            ["source_video"] = sourceVideo,
            ["source_video_url"] = (sourceVideoUrl ?? "").Trim(),
            ["source_start_seconds"] = plan.SourceStart,
            ["source_duration_seconds"] = plan.SourceDuration,
            ["end_card_duration_seconds"] = plan.EndCardDuration,
            ["total_duration_seconds"] = plan.TotalDuration,
            ["scene"] = plan.SceneTitle,
            ["question_id"] = plan.QuestionId,
            ["question_number"] = visualSource.QuestionNumber,
            ["question_total"] = visualSource.QuestionTotal,
            ["visual_style"] = VisualStyle,
            ["visual_renderer"] = nameof(QuizThemedCardRenderer),
            ["theme"] = visualSource.Visual.ThemeKey,
            ["call_to_action"] = script,
            ["call_to_action_voice"] = "fable",
            ["call_to_action_audio"] = Path.GetFileName(ctaAudio),
            ["quiz_logo"] = string.IsNullOrWhiteSpace(quizLogoPath) ? "" : Path.GetFileName(quizLogoPath),
            ["output"] = QuizPromoShortPaths.VideoFileName,
            ["width"] = QuizPromoShortRenderer.Width,
            ["height"] = QuizPromoShortRenderer.Height,
        };
        if (existingYouTubeUpload is not null)
            payload["youtube_upload"] = existingYouTubeUpload;
        File.WriteAllText(path, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static string PhaseName(QuizPromoNativeCardPhase phase) => phase.Kind switch
    {
        QuizPreviewCardKind.Countdown => $"countdown_{phase.CountdownValue}",
        QuizPreviewCardKind.AnswerReveal => "answer_reveal",
        QuizPreviewCardKind.Explanation => "answer",
        _ => "question",
    };

    private static string RequiredText(JsonElement root, string property)
    {
        var value = Text(root, property, "");
        if (value.Length == 0)
            throw new InvalidDataException($"The saved promotional question is missing '{property}'.");
        return value;
    }

    private static string Text(JsonElement root, string property, string fallback)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return fallback;
        return value.GetString()?.Trim() ?? fallback;
    }

    private static int Int(JsonElement root, string property, int fallback)
    {
        if (!root.TryGetProperty(property, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return fallback;
    }

    private static double Double(JsonElement root, string property, double fallback)
    {
        if (!root.TryGetProperty(property, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        return fallback;
    }

    private static bool Bool(JsonElement root, string property, bool fallback)
    {
        if (!root.TryGetProperty(property, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(left.R + ((right.R - left.R) * amount)),
            (byte)Math.Round(left.G + ((right.G - left.G) * amount)),
            (byte)Math.Round(left.B + ((right.B - left.B) * amount)));
    }

    private static string ConcatPath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal);

    private static async Task<ProcessResult> RunAsync(
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
            if (!process.Start())
                throw new NativeFfmpegTimelineException($"Could not start {Path.GetFileName(executable)}.");
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new NativeFfmpegTimelineException($"{Path.GetFileNameWithoutExtension(executable)} timed out.", error);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static string LastUsefulLines(string value)
    {
        var lines = (value ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\n', lines.TakeLast(12));
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
