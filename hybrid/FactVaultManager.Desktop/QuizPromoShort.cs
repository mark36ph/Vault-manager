using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace FactVaultManager.Desktop;

public sealed record QuizPromoShortPlan(
    double SourceStart,
    double SourceDuration,
    double EndCardDuration,
    string SceneTitle,
    int QuestionId)
{
    public double TotalDuration => SourceDuration + EndCardDuration;
}

public static class QuizPromoShortPlanner
{
    public const double MaximumDuration = 45;

    public static QuizPromoShortPlan Create(
        NativeTimeline timeline,
        double sourceVideoDuration,
        double endCardDuration)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (!double.IsFinite(sourceVideoDuration) || sourceVideoDuration <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceVideoDuration));
        if (!double.IsFinite(endCardDuration) || endCardDuration is < 2 or > 10)
            throw new ArgumentOutOfRangeException(nameof(endCardDuration), "The promotional end card must last between 2 and 10 seconds.");

        var questionScenes = timeline.Scenes
            .Where(scene => MetadataInt(scene, "question_id") > 0)
            .OrderBy(scene => scene.Start)
            .ToList();
        var selected = questionScenes.FirstOrDefault(scene => string.Equals(
                           MetadataText(scene, "difficulty"), "insane", StringComparison.OrdinalIgnoreCase))
                       ?? questionScenes.LastOrDefault();
        if (selected is null)
            throw new InvalidOperationException("The long-form timeline does not contain a usable quiz question for the promotional Short.");
        if (selected.Start >= sourceVideoDuration)
            throw new InvalidOperationException("The selected promotional question starts beyond the end of the rendered video.");

        var available = sourceVideoDuration - selected.Start;
        var sourceDuration = Math.Min(selected.Duration, Math.Min(available, MaximumDuration - endCardDuration));
        if (sourceDuration < 3)
            throw new InvalidOperationException("The selected promotional question is too short to create a promotional Short.");

        return new QuizPromoShortPlan(
            selected.Start,
            sourceDuration,
            endCardDuration,
            selected.Title,
            MetadataInt(selected, "question_id"));
    }

    private static string MetadataText(NativeTimelineScene scene, string key) =>
        scene.Metadata.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";

    private static int MetadataInt(NativeTimelineScene scene, string key)
    {
        var value = MetadataText(scene, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}

public static class QuizPromoShortScript
{
    public const string DefaultCallToAction =
        "Think you can beat the full test? Tap the related video.";

    public static string Normalize(string? value)
    {
        var script = (value ?? "").Trim();
        if (script.Length == 0) script = DefaultCallToAction;
        if (script.Length > 300)
            throw new ArgumentException("The promotional call to action must be 300 characters or fewer.", nameof(value));
        return script;
    }
}

public static class QuizPromoShortPaths
{
    public const string FolderName = "PromoShort";
    public const string VideoFileName = "Factburst_Promo_Short.mp4";
    public const string EndCardFileName = "Promo_End_Card.png";
    public const string MetadataFileName = "promo-short.json";

    public static string Folder(string projectFolder) =>
        Path.Combine(Path.GetFullPath(projectFolder), FolderName);

    public static string Video(string projectFolder) => Path.Combine(Folder(projectFolder), VideoFileName);
    public static string EndCard(string projectFolder) => Path.Combine(Folder(projectFolder), EndCardFileName);
    public static string Metadata(string projectFolder) => Path.Combine(Folder(projectFolder), MetadataFileName);

    public static string? FindExisting(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder)) return null;
        try
        {
            var path = Video(projectFolder);
            return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

public sealed record QuizPromoShortYouTubeUpload(
    string VideoId,
    string Url,
    string Privacy,
    string UploadedAt);

public static class QuizPromoShortPublicationStore
{
    private const string YouTubeUploadKey = "youtube_upload";

    public static QuizPromoShortYouTubeUpload? LoadYouTube(string projectFolder)
    {
        try
        {
            var path = QuizPromoShortPaths.Metadata(projectFolder);
            if (!File.Exists(path)) return null;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var upload = root?[YouTubeUploadKey] as JsonObject;
            if (upload is null) return null;
            var videoId = upload["video_id"]?.GetValue<string>()?.Trim() ?? "";
            var url = upload["url"]?.GetValue<string>()?.Trim() ?? "";
            if (videoId.Length == 0 || url.Length == 0) return null;
            return new QuizPromoShortYouTubeUpload(
                videoId,
                url,
                upload["privacy"]?.GetValue<string>()?.Trim() ?? "",
                upload["uploaded_at"]?.GetValue<string>()?.Trim() ?? "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read promo Short upload status: {error.Message}");
            return null;
        }
    }

    public static void RecordYouTube(
        string projectFolder,
        YouTubeVideoUploadResult upload,
        string privacy,
        DateTimeOffset uploadedAt)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var path = QuizPromoShortPaths.Metadata(projectFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        root[YouTubeUploadKey] = new JsonObject
        {
            ["video_id"] = upload.VideoId,
            ["url"] = upload.Url,
            ["privacy"] = privacy,
            ["uploaded_at"] = uploadedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}

public static class QuizPromoShortUploadMetadata
{
    private const string TitleSuffix = " | Final Question #Shorts";

    public static string Title(string sourceTitle)
    {
        var title = QuizPublishMetadataGenerator.DisplayName(sourceTitle).Trim();
        if (title.Length == 0) title = "Factburst Quiz";
        var maximumBaseLength = 100 - TitleSuffix.Length;
        if (title.Length > maximumBaseLength)
            title = title[..maximumBaseLength].TrimEnd();
        return title + TitleSuffix;
    }

    public static string Description(string sourceTitle, string fullVideoUrl, string hashtags)
    {
        var url = QuizYouTubePublication.NormalizeUrl(fullVideoUrl);
        if (url.Length == 0)
            throw new ArgumentException("Upload the full quiz to YouTube before uploading its promotional Short.");
        var tags = (hashtags ?? "").Trim();
        if (!tags.Contains("#Shorts", StringComparison.OrdinalIgnoreCase))
            tags = tags.Length == 0 ? "#Shorts #Quiz #Trivia" : tags + " #Shorts";
        return
            $"Think you can beat the full {QuizPublishMetadataGenerator.DisplayName(sourceTitle)} test?" +
            Environment.NewLine + Environment.NewLine +
            $"Watch the full quiz: {url}" +
            Environment.NewLine + Environment.NewLine + tags;
    }
}

public sealed record QuizPromoShortResult(
    string VideoPath,
    string EndCardPath,
    string CallToActionAudioPath,
    string MetadataPath,
    QuizPromoShortPlan Plan);

public sealed class QuizPromoShortRenderer
{
    public const int Width = 1080;
    public const int Height = 1920;
    public const double MinimumEndCardDuration = 4.5;
    public const double MaximumEndCardDuration = 6.0;
    public const double CallToActionTailPadding = 0.35;

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
        var endCardDuration = EndCardDurationFor(ctaDuration);
        var sourceDuration = await media.MediaDurationAsync(sourceVideo, cancellationToken);
        var plan = QuizPromoShortPlanner.Create(timeline, sourceDuration, endCardDuration);

        progress?.Invoke($"Building a vertical clip from {plan.SceneTitle}...");
        var endCard = QuizPromoShortPaths.EndCard(projectFolder);
        QuizPromoShortEndCardRenderer.Write(endCard, title, quizLogoPath);
        var destination = QuizPromoShortPaths.Video(projectFolder);
        var hasSourceAudio = await HasAudioAsync(sourceVideo, cancellationToken);
        await RenderVideoAsync(
            sourceVideo,
            endCard,
            ctaAudio,
            destination,
            plan,
            hasSourceAudio,
            cancellationToken);

        var metadataPath = QuizPromoShortPaths.Metadata(projectFolder);
        WriteMetadata(metadataPath, sourceVideo, sourceVideoUrl, script, ctaAudio, quizLogoPath, plan);
        progress?.Invoke("Promotional Short ready.");
        return new QuizPromoShortResult(destination, endCard, ctaAudio, metadataPath, plan);
    }

    internal static string BuildFilter(QuizPromoShortPlan plan, bool hasSourceAudio)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var bodyDuration = F(plan.SourceDuration);
        var endDuration = F(plan.EndCardDuration);
        var bodyAudio = hasSourceAudio
            ? $"[0:a]atrim=duration={bodyDuration},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a0];"
            : $"anullsrc=r=48000:cl=stereo:d={bodyDuration}[a0];";
        return
            "[0:v]setpts=PTS-STARTPTS,split=2[base][front];" +
            "[base]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=20:10[bg];" +
            "[front]crop=iw*0.9:ih:(iw-iw*0.9)/2:0,scale=1080:-2[fg];" +
            "[bg][fg]overlay=(W-w)/2:(H-h)/2,setsar=1,fps=30,format=yuv420p[v0];" +
            $"[1:v]trim=duration={endDuration},setpts=PTS-STARTPTS,scale=1080:1920,setsar=1,fps=30,format=yuv420p[v1];" +
            bodyAudio +
            $"[2:a]atrim=duration={endDuration},asetpts=PTS-STARTPTS,apad=whole_dur={endDuration},aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a1];" +
            "[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]";
    }

    internal static double EndCardDurationFor(double callToActionDuration)
    {
        if (!double.IsFinite(callToActionDuration) || callToActionDuration <= 0)
            throw new ArgumentOutOfRangeException(nameof(callToActionDuration));
        if (callToActionDuration + CallToActionTailPadding > MaximumEndCardDuration)
        {
            throw new InvalidOperationException(
                $"The Fable end-card narration is {callToActionDuration:0.0} seconds. " +
                "Shorten the script so it fits within a six-second end card.");
        }
        return Math.Clamp(
            callToActionDuration + CallToActionTailPadding,
            MinimumEndCardDuration,
            MaximumEndCardDuration);
    }

    private static async Task RenderVideoAsync(
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
                "-ss", F(plan.SourceStart),
                "-t", F(plan.SourceDuration),
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
        var result = await RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=index", "-of", "csv=p=0", sourceVideo],
            TimeSpan.FromSeconds(30), cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Trim().Length > 0;
    }

    private static void WriteMetadata(
        string path,
        string sourceVideo,
        string sourceVideoUrl,
        string script,
        string ctaAudio,
        string quizLogoPath,
        QuizPromoShortPlan plan)
    {
        JsonNode? existingYouTubeUpload = null;
        try
        {
            if (File.Exists(path))
                existingYouTubeUpload = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)?["youtube_upload"]?.DeepClone();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not preserve promo Short upload metadata: {error.Message}");
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
            ["call_to_action"] = script,
            ["call_to_action_voice"] = "fable",
            ["call_to_action_audio"] = Path.GetFileName(ctaAudio),
            ["quiz_logo"] = string.IsNullOrWhiteSpace(quizLogoPath) ? "" : Path.GetFileName(quizLogoPath),
            ["output"] = QuizPromoShortPaths.VideoFileName,
            ["width"] = Width,
            ["height"] = Height,
        };
        if (existingYouTubeUpload is not null)
            payload["youtube_upload"] = existingYouTubeUpload;
        File.WriteAllText(path, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

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
            if (!process.Start()) throw new NativeFfmpegTimelineException($"Could not start {Path.GetFileName(executable)}.");
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

public static class QuizPromoShortEndCardRenderer
{
    public static void Write(string destination, string title, string? quizLogoPath)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var root = new Grid
        {
            Width = QuizPromoShortRenderer.Width,
            Height = QuizPromoShortRenderer.Height,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.55),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 900,
        };
        content.Children.Add(BuildBranding(quizLogoPath));
        content.Children.Add(Text("THE FULL TEST\nGETS HARDER", 88, Colors.White, new Thickness(0, 45, 0, 45)));
        content.Children.Add(Text(
            QuizPublishMetadataGenerator.DisplayName(title).ToUpperInvariant(),
            50,
            Color.FromRgb(0, 204, 255),
            new Thickness(0, 0, 0, 70)));
        var action = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 235, 115)),
            BorderThickness = new Thickness(5),
            CornerRadius = new CornerRadius(38),
            Padding = new Thickness(48, 30, 48, 30),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(70, 235, 115),
                BlurRadius = 30,
                ShadowDepth = 0,
                Opacity = 0.75,
            },
            Child = Text("TAP THE RELATED VIDEO", 48, Colors.White),
        };
        content.Children.Add(action);
        root.Children.Add(content);
        root.Measure(new Size(QuizPromoShortRenderer.Width, QuizPromoShortRenderer.Height));
        root.Arrange(new Rect(0, 0, QuizPromoShortRenderer.Width, QuizPromoShortRenderer.Height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            QuizPromoShortRenderer.Width, QuizPromoShortRenderer.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    internal static FrameworkElement BuildBranding(string? quizLogoPath)
    {
        if (string.IsNullOrWhiteSpace(quizLogoPath))
            return Text("FACTBURST QUIZ", 42, Color.FromRgb(255, 202, 45));

        var path = QuizBranding.ValidateLogoPath(quizLogoPath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return new Image
        {
            Source = bitmap,
            Height = 260,
            MaxWidth = 700,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                Color = Colors.White,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.35,
            },
        };
    }

    private static TextBlock Text(string value, double size, Color color, Thickness? margin = null) => new()
    {
        Text = value,
        Foreground = new SolidColorBrush(color),
        FontSize = size,
        FontWeight = FontWeights.Black,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0),
    };
}
