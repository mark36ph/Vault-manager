using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FactVaultManager.Desktop;

public sealed record NativeQuizFinalRenderPlan(
    string? BackgroundSource,
    IReadOnlyList<NativeTimelineClip> ForegroundClips,
    IReadOnlyList<NativeTimelineClip> AudioClips,
    double Duration,
    int Width,
    int Height,
    double FrameRate);

public sealed record NativeQuizFinalRenderResult(
    string VideoPath,
    double Duration,
    int Width,
    int Height,
    double FrameRate,
    bool HasAudio);

public sealed class NativeQuizFinalRenderer
{
    public const string FinalFileName = "FactburstQuiz_Final.mp4";
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromHours(2);

    public NativeQuizFinalRenderResult Render(
        NativeTimeline timeline,
        string projectFolder,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        cancellationToken.ThrowIfCancellationRequested();

        projectFolder = Path.GetFullPath(projectFolder);
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException($"Quiz project folder was not found: {projectFolder}");

        var plan = CreatePlan(timeline);
        ValidateMedia(plan);
        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");
        var destination = OutputPath(projectFolder);
        var working = Path.Combine(projectFolder, ".native-final-render-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(working);

        try
        {
            var videoOnly = Path.Combine(working, "video.mp4");
            progress?.Invoke("Rendering final quiz video...");
            RenderVideo(ffmpeg, plan, working, videoOnly, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            string? audioOnly = null;
            if (plan.AudioClips.Count > 0)
            {
                audioOnly = Path.Combine(working, "audio.m4a");
                progress?.Invoke("Mixing narration, music and sound effects...");
                RenderAudio(ffmpeg, plan, working, audioOnly, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Finishing YouTube-ready MP4...");
            TryDelete(destination);
            if (audioOnly is null)
            {
                File.Move(videoOnly, destination, overwrite: true);
            }
            else
            {
                Mux(ffmpeg, videoOnly, audioOnly, destination, plan.Duration, cancellationToken);
            }

            if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
                throw new NativeFfmpegTimelineException("Native final render did not create a usable MP4 file.");

            return new NativeQuizFinalRenderResult(
                destination,
                plan.Duration,
                plan.Width,
                plan.Height,
                plan.FrameRate,
                audioOnly is not null);
        }
        finally
        {
            TryDeleteDirectory(working);
        }
    }

    public static string OutputPath(string projectFolder) =>
        Path.Combine(Path.GetFullPath(projectFolder), FinalFileName);

    public static NativeQuizFinalRenderPlan CreatePlan(NativeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        timeline.Validate();
        if (timeline.Duration <= 0)
            throw new InvalidOperationException("Quiz timeline has no duration to render.");

        var videoTracks = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .ToList();
        var backgroundTrack = videoTracks.FirstOrDefault(track =>
            string.Equals(track.Name, QuizAnimatedBackground.TrackName, StringComparison.Ordinal));
        string? backgroundSource = null;
        if (backgroundTrack is not null && backgroundTrack.Clips.Count > 0)
        {
            var sources = backgroundTrack.Clips
                .Where(clip => clip.Kind == NativeTimelineClipKind.Video && !string.IsNullOrWhiteSpace(clip.Source))
                .Select(clip => Path.GetFullPath(clip.Source!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sources.Count != 1)
                throw new InvalidOperationException("The animated quiz background must use one reusable video source.");
            backgroundSource = sources[0];
        }

        var foregroundTracks = videoTracks
            .Where(track => !ReferenceEquals(track, backgroundTrack) &&
                            track.Clips.Any(clip => clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video))
            .ToList();
        if (foregroundTracks.Count != 1)
            throw new InvalidOperationException(
                $"Native final render expects one quiz-card video track; found {foregroundTracks.Count}.");

        var foreground = foregroundTracks[0].Clips
            .Where(clip => clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video)
            .OrderBy(clip => clip.Start)
            .ToList();
        if (foreground.Count == 0)
            throw new InvalidOperationException("Quiz timeline has no foreground cards to render.");
        if (foreground.Any(clip => clip.Kind != NativeTimelineClipKind.Image))
            throw new InvalidOperationException(
                "Native final render currently expects the foreground quiz presentation to use rendered image cards.");

        const double tolerance = 0.05;
        var cursor = 0.0;
        foreach (var clip in foreground)
        {
            if (Math.Abs(clip.Start - cursor) > tolerance)
                throw new InvalidOperationException(
                    $"Quiz card timeline contains a gap or overlap near {clip.Start:0.###} seconds.");
            cursor = clip.End;
        }
        if (Math.Abs(cursor - timeline.Duration) > tolerance)
            throw new InvalidOperationException(
                "Quiz card timeline does not cover the complete final video duration.");

        var audio = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Audio)
            .SelectMany(track => track.Clips)
            .Where(clip => clip.Kind == NativeTimelineClipKind.Audio)
            .OrderBy(clip => clip.Start)
            .ToList();

        return new NativeQuizFinalRenderPlan(
            backgroundSource,
            foreground,
            audio,
            timeline.Duration,
            timeline.Width,
            timeline.Height,
            timeline.FrameRate);
    }

    public static string BuildConcatManifest(IReadOnlyList<NativeTimelineClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        if (clips.Count == 0)
            throw new ArgumentException("At least one quiz card is required.", nameof(clips));

        var builder = new StringBuilder("ffconcat version 1.0\n");
        foreach (var clip in clips)
        {
            if (clip.Kind != NativeTimelineClipKind.Image || string.IsNullOrWhiteSpace(clip.Source))
                throw new InvalidOperationException("Quiz-card concat input must contain image clips with media paths.");
            builder.Append("file ").Append(ConcatPath(clip.Source!)).Append('\n');
            builder.Append("duration ").Append(F(clip.Duration)).Append('\n');
        }
        builder.Append("file ").Append(ConcatPath(clips[^1].Source!)).Append('\n');
        return builder.ToString();
    }

    private static void RenderVideo(
        string ffmpeg,
        NativeQuizFinalRenderPlan plan,
        string working,
        string destination,
        CancellationToken cancellationToken)
    {
        var manifest = Path.Combine(working, "quiz-cards.ffconcat");
        File.WriteAllText(manifest, BuildConcatManifest(plan.ForegroundClips), new UTF8Encoding(false));

        var args = new List<string> { "-y" };
        if (!string.IsNullOrWhiteSpace(plan.BackgroundSource))
        {
            args.AddRange(new[] { "-stream_loop", "-1", "-i", plan.BackgroundSource! });
        }
        else
        {
            args.AddRange(new[]
            {
                "-f", "lavfi",
                "-i", $"color=c=0x182453:s={plan.Width}x{plan.Height}:r={F(plan.FrameRate)}",
            });
        }
        args.AddRange(new[] { "-f", "concat", "-safe", "0", "-i", manifest });

        var filter =
            $"[0:v]fps={F(plan.FrameRate)},scale={plan.Width}:{plan.Height},setpts=PTS-STARTPTS[bg];" +
            $"[1:v]fps={F(plan.FrameRate)},scale={plan.Width}:{plan.Height},format=rgba,setpts=PTS-STARTPTS[fg];" +
            "[bg][fg]overlay=0:0:format=auto:shortest=1,format=yuv420p[out]";
        args.AddRange(new[]
        {
            "-filter_complex", filter,
            "-map", "[out]",
            "-t", F(plan.Duration),
            "-r", F(plan.FrameRate),
            "-c:v", "libx264",
            "-preset", "fast",
            "-crf", "18",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            "-an",
            destination,
        });
        Run(ffmpeg, args, "final quiz video", cancellationToken);
    }

    private static void RenderAudio(
        string ffmpeg,
        NativeQuizFinalRenderPlan plan,
        string working,
        string destination,
        CancellationToken cancellationToken)
    {
        var groups = plan.AudioClips
            .GroupBy(clip => Path.GetFullPath(clip.Source!), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var args = new List<string> { "-y" };
        foreach (var group in groups)
            args.AddRange(new[] { "-i", group.Key });

        var filterPath = Path.Combine(working, "audio-filter.txt");
        var filter = new StringBuilder();
        var clipLabels = new List<string>();
        var branchNumber = 0;
        var gain = plan.Height > plan.Width ? 2.5118864315 : 1.5848931925;

        for (var input = 0; input < groups.Count; input++)
        {
            var groupClips = groups[input].ToList();
            var sourceLabels = Enumerable.Range(0, groupClips.Count)
                .Select(index => $"src{input}_{index}")
                .ToList();
            filter.Append('[').Append(input).Append(":a]")
                .Append("aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo");
            if (sourceLabels.Count > 1)
            {
                filter.Append(",asplit=").Append(sourceLabels.Count);
                foreach (var label in sourceLabels)
                    filter.Append('[').Append(label).Append(']');
                filter.Append(';');
            }
            else
            {
                filter.Append('[').Append(sourceLabels[0]).Append("];");
            }

            for (var index = 0; index < groupClips.Count; index++)
            {
                var clip = groupClips[index];
                var label = $"clip{branchNumber++}";
                var delay = Math.Max(0L, (long)Math.Round(clip.Start * 1000.0, MidpointRounding.AwayFromZero));
                filter.Append('[').Append(sourceLabels[index]).Append(']')
                    .Append("atrim=start=").Append(F(clip.SourceIn))
                    .Append(":duration=").Append(F(clip.Duration))
                    .Append(",asetpts=PTS-STARTPTS")
                    .Append(",volume=").Append(F(gain))
                    .Append(",adelay=").Append(delay.ToString(CultureInfo.InvariantCulture)).Append(":all=1")
                    .Append('[').Append(label).Append("];\n");
                clipLabels.Add(label);
            }
        }

        foreach (var label in clipLabels)
            filter.Append('[').Append(label).Append(']');
        filter.Append("amix=inputs=").Append(clipLabels.Count)
            .Append(":duration=longest:dropout_transition=0:normalize=0")
            .Append(",alimiter=limit=0.95,apad,atrim=duration=").Append(F(plan.Duration))
            .Append("[mix]\n");
        File.WriteAllText(filterPath, filter.ToString(), new UTF8Encoding(false));

        args.AddRange(new[]
        {
            "-filter_complex_script", filterPath,
            "-map", "[mix]",
            "-t", F(plan.Duration),
            "-c:a", "aac",
            "-b:a", "192k",
            "-ar", "48000",
            "-ac", "2",
            destination,
        });
        Run(ffmpeg, args, "final quiz audio mix", cancellationToken);
    }

    private static void Mux(
        string ffmpeg,
        string video,
        string audio,
        string destination,
        double duration,
        CancellationToken cancellationToken)
    {
        Run(ffmpeg, new[]
        {
            "-y",
            "-i", video,
            "-i", audio,
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-c", "copy",
            "-t", F(duration),
            "-movflags", "+faststart",
            destination,
        }, "final MP4 mux", cancellationToken);
    }

    private static void ValidateMedia(NativeQuizFinalRenderPlan plan)
    {
        var paths = plan.ForegroundClips
            .Concat(plan.AudioClips)
            .Select(clip => clip.Source)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToList();
        if (!string.IsNullOrWhiteSpace(plan.BackgroundSource))
            paths.Add(Path.GetFullPath(plan.BackgroundSource));

        var missing = paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(path))
            .ToList();
        if (missing.Count > 0)
            throw new FileNotFoundException(
                "Native final render is missing media: " + string.Join(", ", missing.Select(Path.GetFileName)),
                missing[0]);
    }

    private static void Run(
        string executable,
        IEnumerable<string> arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                throw new NativeFfmpegTimelineException($"Could not start FFmpeg for {operation}.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var deadline = DateTime.UtcNow + RenderTimeout;
            while (!process.WaitForExit(250))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (DateTime.UtcNow >= deadline)
                {
                    TryKill(process);
                    throw new NativeFfmpegTimelineException($"FFmpeg timed out while creating the {operation}.");
                }
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                var error = stderr.Result.Trim();
                throw new NativeFfmpegTimelineException(
                    $"FFmpeg could not create the {operation}:\n" +
                    (error.Length == 0 ? "Unknown FFmpeg error" : Tail(error, 5000)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NativeFfmpegTimelineException)
        {
            throw;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new NativeFfmpegTimelineException($"Could not run FFmpeg for {operation}: {error.Message}", error);
        }
    }

    private static string ConcatPath(string path)
    {
        var value = Path.GetFullPath(path).Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal);
        return "'" + value + "'";
    }

    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Tail(string value, int maximum) =>
        value.Length <= maximum ? value : value[^maximum..];

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
