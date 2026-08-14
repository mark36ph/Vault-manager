using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeFfmpegTimelineException : Exception
{
    public NativeFfmpegTimelineException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record NativeCaptionEntry(double Start, double End, string Text);

public sealed class NativeFfmpegTimelineService
{
    private static readonly Regex OnscreenTiming = new(
        @"(?m)^\s*(\d+(?:\.\d+)?)\s*[–—-]\s*(\d+(?:\.\d+)?)\s*(?:sec|secs|seconds?)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmojiPattern = new(
        "[\\u2600-\\u27BF\\u200D\\uFE0F\\U0001F1E6-\\U0001FAFF]+",
        RegexOptions.Compiled);

    public Action<string, double, string>? Progress { get; set; }

    public async Task<double> MediaDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("media file does not exist", path);

        var result = await RunAsync(
            FindExecutable("ffprobe"),
            [
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                path,
            ],
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (result.ExitCode != 0)
            throw new NativeFfmpegTimelineException($"Could not read media duration: {Path.GetFileName(path)}");

        if (!double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
            throw new NativeFfmpegTimelineException($"Invalid media duration returned for: {Path.GetFileName(path)}");
        return duration;
    }

    public async Task SynchronizeVisualsToNarrationAsync(
        NativeTimeline timeline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var narration = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Audio)
            .SelectMany(track => track.Clips)
            .FirstOrDefault(clip => !string.IsNullOrWhiteSpace(clip.Source));
        if (narration is null)
            return;

        var narrationPath = Path.GetFullPath(narration.Source!);
        if (string.Equals(Path.GetFileName(narrationPath), "narration_with_fact_unlocked.wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new NativeFfmpegTimelineException(
                "The export-only combined narration was reused as the source narration. The project timeline should point to the original narration file.");
        }
        if (!File.Exists(narrationPath))
            throw new NativeFfmpegTimelineException($"Narration file does not exist: {narrationPath}");

        var narrationDuration = await MediaDurationAsync(narrationPath, cancellationToken);
        var visuals = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .SelectMany(track => track.Clips)
            .Where(clip => clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video)
            .ToList();

        if (visuals.Count == 0)
        {
            narration.Duration = narrationDuration;
            return;
        }

        var originalDuration = visuals.Max(clip => clip.Start + clip.Duration);
        if (originalDuration <= 0)
            return;
        var scale = narrationDuration / originalDuration;
        foreach (var clip in visuals)
        {
            clip.Start *= scale;
            clip.Duration *= scale;
        }
        foreach (var scene in timeline.Scenes)
        {
            scene.Start *= scale;
            scene.Duration *= scale;
        }
        narration.Start = 0;
        narration.SourceIn = 0;
        narration.Duration = narrationDuration;
    }

    public static void CloseVisualGaps(NativeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var visuals = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .SelectMany(track => track.Clips)
            .Where(clip =>
                clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video &&
                !string.IsNullOrWhiteSpace(clip.Source))
            .OrderBy(clip => clip.Start)
            .ToList();
        if (visuals.Count == 0)
            return;

        var first = visuals[0];
        if (first.Start > 0)
        {
            first.Duration += first.Start;
            first.Start = 0;
        }

        for (var index = 0; index + 1 < visuals.Count; index++)
        {
            var current = visuals[index];
            var next = visuals[index + 1];
            var required = Math.Max(0, next.Start - current.Start);
            if (required > current.Duration)
                current.Duration = required;
        }

        var last = visuals[^1];
        var finalDuration = Math.Max(0, timeline.Duration - last.Start);
        if (finalDuration > last.Duration)
            last.Duration = finalDuration;
    }

    public static IReadOnlyList<NativeCaptionEntry> ParseOnscreenText(string value)
    {
        var text = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
            return Array.Empty<NativeCaptionEntry>();

        var matches = OnscreenTiming.Matches(text).Cast<Match>().ToList();
        var entries = new List<NativeCaptionEntry>();
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var captionStart = match.Index + match.Length;
            var captionEnd = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var caption = text[captionStart..captionEnd].Trim();
            if (caption.Length == 0)
                continue;
            entries.Add(new NativeCaptionEntry(
                double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                caption));
        }
        return entries;
    }

    public static IReadOnlyList<NativeCaptionEntry> CaptionsForClip(
        double clipStart,
        double clipDuration,
        IEnumerable<NativeCaptionEntry> entries)
    {
        var clipEnd = clipStart + clipDuration;
        var result = new List<NativeCaptionEntry>();
        foreach (var entry in entries)
        {
            var overlapStart = Math.Max(clipStart, entry.Start);
            var overlapEnd = Math.Min(clipEnd, entry.End);
            if (overlapEnd <= overlapStart)
                continue;
            var localStart = overlapStart - clipStart;
            var localEnd = Math.Max(localStart, overlapEnd - clipStart - (1.0 / 30.0));
            result.Add(new NativeCaptionEntry(localStart, localEnd, entry.Text));
        }
        return result;
    }

    public async Task ConvertStillsToVideoAsync(
        NativeTimeline timeline,
        string projectFolder,
        string onscreenText = "",
        double? captionEndLimit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        projectFolder = Path.GetFullPath(projectFolder);
        var ffmpeg = FindExecutable("ffmpeg");
        var outputFolder = Path.Combine(projectFolder, "ResolveClips");
        Directory.CreateDirectory(outputFolder);
        var fps = timeline.FrameRate;
        var captions = ParseOnscreenText(onscreenText).ToList();
        if (captionEndLimit is not null)
        {
            captions = captions
                .Where(entry => entry.Start < captionEndLimit.Value)
                .Select(entry => entry with { End = Math.Min(entry.End, captionEndLimit.Value) })
                .ToList();
        }

        var fontPath = FindCaptionFont();
        var logo = FindLogoPath();
        var motionIndex = 0;

        foreach (var clip in timeline.Tracks.SelectMany(track => track.Clips))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clip.Kind != NativeTimelineClipKind.Image || string.IsNullOrWhiteSpace(clip.Source))
                continue;

            var source = Path.GetFullPath(clip.Source);
            if (!File.Exists(source))
                throw new NativeFfmpegTimelineException($"Image does not exist: {source}");

            var duration = Math.Max(0.1, clip.Duration);
            var frameCount = Math.Max(1, (int)Math.Round(duration * fps, MidpointRounding.ToEven));
            var exactDuration = frameCount / fps;
            var clipStart = clip.Start;
            var motionType = motionIndex++ % 4;
            var progressExpression = $"on/{Math.Max(1, frameCount - 1)}";
            string zoomExpression;
            string xExpression;
            string yExpression;
            switch (motionType)
            {
                case 0:
                    zoomExpression = $"1+0.10*{progressExpression}";
                    xExpression = "iw/2-(iw/zoom/2)";
                    yExpression = "ih/2-(ih/zoom/2)";
                    break;
                case 1:
                    zoomExpression = $"1.10-0.10*{progressExpression}";
                    xExpression = "iw/2-(iw/zoom/2)";
                    yExpression = "ih/2-(ih/zoom/2)";
                    break;
                case 2:
                    zoomExpression = "1.08";
                    xExpression = $"(iw-iw/zoom)*{progressExpression}";
                    yExpression = "ih/2-(ih/zoom/2)";
                    break;
                default:
                    zoomExpression = "1.08";
                    xExpression = $"(iw-iw/zoom)*(1-{progressExpression})";
                    yExpression = "ih/2-(ih/zoom/2)";
                    break;
            }

            var clipCaptions = CaptionsForClip(clipStart, exactDuration, captions).ToList();
            var filterBuilder = new StringBuilder();
            var captionIdentity = new StringBuilder();
            var fontFilter = fontPath.Replace('\\', '/').Replace(":", "\\:", StringComparison.Ordinal);
            for (var captionIndex = 0; captionIndex < clipCaptions.Count; captionIndex++)
            {
                var entry = clipCaptions[captionIndex];
                var captionText = WrapCaption(RemoveEmojis(entry.Text), 24);
                var inputLabel = captionIndex == 0 ? "branded" : $"captioned{captionIndex}";
                var outputLabel = $"captioned{captionIndex + 1}";
                filterBuilder.Append($"[{inputLabel}]drawtext=fontfile='{fontFilter}':text='{EscapeDrawtext(captionText)}':");
                filterBuilder.Append("fontcolor=white:fontsize=76:line_spacing=12:borderw=5:bordercolor=black:");
                filterBuilder.Append("box=1:boxcolor=black@0.45:boxborderw=22:x=(w-text_w)/2:y=120:");
                filterBuilder.Append($"enable='between(t\\,{F(entry.Start)}\\,{F(entry.End)})'[{outputLabel}];");
                if (captionIdentity.Length > 0) captionIdentity.Append('|');
                captionIdentity.Append($"{F(entry.Start)}-{F(entry.End)}-{captionText}");
            }
            var finalLabel = clipCaptions.Count > 0 ? $"captioned{clipCaptions.Count}" : "branded";
            var modifiedNs = (File.GetLastWriteTimeUtc(source).Ticks - DateTime.UnixEpoch.Ticks) * 100L;
            var identity = string.Join('|',
                source,
                modifiedNs.ToString(CultureInfo.InvariantCulture),
                F(clipStart),
                F(duration),
                F(fps),
                captionIdentity.ToString(),
                captionEndLimit?.ToString(CultureInfo.InvariantCulture) ?? "None",
                $"motion={motionType}",
                "1080x1920-wrap-audio-motion-v32");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..12];
            var destination = Path.Combine(outputFolder, $"scene_{digest}.mp4");

            if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
            {
                var filter =
                    $"[0:v]format=rgba,scale=1200:2134:force_original_aspect_ratio=increase,crop=1080:1920," +
                    $"zoompan=z='{zoomExpression}':x='{xExpression}':y='{yExpression}':d={frameCount}:s=1080x1920:fps={F(fps)}[scene];" +
                    "[1:v]format=rgba,scale=190:-1[logo];" +
                    "[scene][logo]overlay=W-w-35:H-h-35:repeatlast=1:shortest=0[branded];" +
                    filterBuilder +
                    $"[{finalLabel}]format=yuv420p[out]";

                var result = await RunAsync(
                    ffmpeg,
                    [
                        "-y", "-i", source, "-i", logo,
                        "-filter_complex", filter,
                        "-map", "[out]",
                        "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
                        "-r", F(fps),
                        "-c:v", "libx264",
                        "-preset", "veryfast",
                        "-profile:v", "high",
                        "-level", "4.1",
                        "-pix_fmt", "yuv420p",
                        "-movflags", "+faststart",
                        "-an",
                        destination,
                    ],
                    TimeSpan.FromSeconds(300),
                    cancellationToken);
                if (result.ExitCode != 0)
                {
                    TryDelete(destination);
                    throw new NativeFfmpegTimelineException(
                        "FFmpeg conversion failed:\n" + (string.IsNullOrWhiteSpace(result.StdErr) ? "Unknown FFmpeg error" : result.StdErr.Trim()));
                }
            }

            clip.Source = Path.GetFullPath(destination);
            clip.Kind = NativeTimelineClipKind.Video;
            clip.SourceIn = 0;
            clip.Duration = exactDuration;
        }
    }

    public async Task AddFactUnlockedOutroAsync(
        NativeTimeline timeline,
        string projectFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        projectFolder = Path.GetFullPath(projectFolder);
        var ffmpeg = FindExecutable("ffmpeg");
        var outroAudio = Path.Combine(projectFolder, "Voice", "fact_unlocked.mp3");
        if (!File.Exists(outroAudio))
            throw new NativeFfmpegTimelineException("Fact unlocked audio was not found. Run the voice stage again first.");

        var audioTrack = timeline.Tracks.FirstOrDefault(track => track.Kind == NativeTimelineTrackKind.Audio);
        var narration = audioTrack?.Clips.FirstOrDefault();
        if (audioTrack is null || narration is null || string.IsNullOrWhiteSpace(narration.Source))
            throw new NativeFfmpegTimelineException("Narration audio clip was not found.");

        var narrationPath = Path.GetFullPath(narration.Source);
        if (!File.Exists(narrationPath))
            throw new NativeFfmpegTimelineException($"Narration file does not exist: {narrationPath}");

        var narrationDuration = await MediaDurationAsync(narrationPath, cancellationToken);
        var outroVoiceDuration = await MediaDurationAsync(outroAudio, cancellationToken);
        var outroStart = narrationDuration;
        var outroDuration = outroVoiceDuration + 0.35;
        var outputFolder = Path.Combine(projectFolder, "ResolveClips");
        Directory.CreateDirectory(outputFolder);
        var combinedAudio = Path.Combine(outputFolder, "narration_with_fact_unlocked.wav");

        var audioFilter =
            "[0:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[narration];" +
            "[1:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[outro];" +
            "[narration][outro]concat=n=2:v=0:a=1[combined];" +
            "[combined]loudnorm=I=-13:TP=-1.0:LRA=7:dual_mono=true[audio]";
        var combine = await RunAsync(
            ffmpeg,
            [
                "-y", "-i", narrationPath, "-i", outroAudio,
                "-filter_complex", audioFilter,
                "-map", "[audio]",
                "-c:a", "pcm_s16le",
                "-ar", "48000",
                combinedAudio,
            ],
            TimeSpan.FromSeconds(120),
            cancellationToken);
        if (combine.ExitCode != 0)
        {
            TryDelete(combinedAudio);
            throw new NativeFfmpegTimelineException(
                "Could not combine narration and outro audio:\n" +
                (string.IsNullOrWhiteSpace(combine.StdErr) ? "Unknown FFmpeg error" : combine.StdErr.Trim()));
        }

        var combinedDuration = await MediaDurationAsync(combinedAudio, cancellationToken);
        narration.Source = Path.GetFullPath(combinedAudio);
        narration.Start = 0;
        narration.SourceIn = 0;
        narration.Duration = combinedDuration;
        narration.Name = "Narration and Fact Unlocked";
        audioTrack.Clips = [narration];

        var outroVideo = await CreateOutroVideoAsync(projectFolder, outroDuration, timeline.FrameRate, cancellationToken);
        var videoTrack = timeline.Tracks.FirstOrDefault(track =>
            track.Kind == NativeTimelineTrackKind.Video && string.Equals(track.Name, "Visuals", StringComparison.Ordinal))
            ?? timeline.Tracks.FirstOrDefault(track => track.Kind == NativeTimelineTrackKind.Video)
            ?? timeline.AddTrack(new NativeTimelineTrack { Name = "Visuals", Kind = NativeTimelineTrackKind.Video });
        videoTrack.Clips.RemoveAll(clip => string.Equals(clip.Name, "Fact Unlocked Outro", StringComparison.Ordinal));
        videoTrack.AddClip(new NativeTimelineClip
        {
            Kind = NativeTimelineClipKind.Video,
            Start = outroStart,
            Duration = outroDuration,
            Source = Path.GetFullPath(outroVideo),
            Name = "Fact Unlocked Outro",
            SourceIn = 0,
            Metadata = new() { ["branding"] = true },
        });
    }

    public async Task<NativeTimeline> PrepareResolveTimelineAsync(
        NativeTimeline projectTimeline,
        string projectFolder,
        string onscreenText,
        CancellationToken cancellationToken = default)
    {
        var exportTimeline = projectTimeline.Clone();
        await SynchronizeVisualsToNarrationAsync(exportTimeline, cancellationToken);
        CloseVisualGaps(exportTimeline);
        var narration = exportTimeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Audio)
            .SelectMany(track => track.Clips)
            .FirstOrDefault(clip => !string.IsNullOrWhiteSpace(clip.Source));
        double? narrationDuration = null;
        if (narration?.Source is { Length: > 0 })
            narrationDuration = await MediaDurationAsync(narration.Source, cancellationToken);

        Progress?.Invoke("timeline", 0.35, "Converting still images into Resolve-compatible video clips");
        var captionEndLimit = narrationDuration is null ? null : Math.Max(0, narrationDuration.Value - 1.25);
        await ConvertStillsToVideoAsync(exportTimeline, projectFolder, onscreenText, captionEndLimit, cancellationToken);
        await AddFactUnlockedOutroAsync(exportTimeline, projectFolder, cancellationToken);
        CleanupUnusedResolveClips(exportTimeline, projectFolder);
        return exportTimeline;
    }

    public static void CleanupUnusedResolveClips(NativeTimeline timeline, string projectFolder)
    {
        var outputFolder = Path.Combine(Path.GetFullPath(projectFolder), "ResolveClips");
        if (!Directory.Exists(outputFolder))
            return;
        var used = timeline.Tracks
            .SelectMany(track => track.Clips)
            .Where(clip => !string.IsNullOrWhiteSpace(clip.Source))
            .Select(clip => Path.GetFullPath(clip.Source!))
            .Where(source => string.Equals(Path.GetDirectoryName(source), outputFolder, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(outputFolder, "scene_*.mp4"))
            if (!used.Contains(Path.GetFullPath(path))) TryDelete(path);

        var clipIds = timeline.Tracks.SelectMany(track => track.Clips).Select(clip => clip.Id).ToArray();
        foreach (var path in Directory.EnumerateFiles(outputFolder, "caption_*.txt"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!clipIds.Any(stem.Contains)) TryDelete(path);
        }
    }

    private async Task<string> CreateOutroVideoAsync(
        string projectFolder,
        double duration,
        double fps,
        CancellationToken cancellationToken)
    {
        var ffmpeg = FindExecutable("ffmpeg");
        var logo = FindLogoPath();
        var outputFolder = Path.Combine(projectFolder, "ResolveClips");
        Directory.CreateDirectory(outputFolder);
        var frameCount = Math.Max(1, (int)Math.Round(duration * fps, MidpointRounding.ToEven));
        var destination = Path.Combine(outputFolder, "fact_unlocked_outro.mp4");
        var filter = "[1:v]format=rgba,scale=850:-1[logo];[0:v][logo]overlay=(W-w)/2:(H-h)/2,format=yuv420p";
        var result = await RunAsync(
            ffmpeg,
            [
                "-y",
                "-f", "lavfi",
                "-i", $"color=c=black:s=1080x1920:r={F(fps)}",
                "-i", logo,
                "-filter_complex", filter,
                "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
                "-c:v", "libx264",
                "-preset", "fast",
                "-pix_fmt", "yuv420p",
                "-movflags", "+faststart",
                "-an",
                destination,
            ],
            TimeSpan.FromSeconds(120),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            TryDelete(destination);
            throw new NativeFfmpegTimelineException(
                "Could not create branded outro:\n" +
                (string.IsNullOrWhiteSpace(result.StdErr) ? "Unknown FFmpeg error" : result.StdErr.Trim()));
        }
        return destination;
    }

    private static string FindCaptionFont()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(windows, "Fonts", "arialbd.ttf"),
            Path.Combine(windows, "Fonts", "seguisb.ttf"),
            Path.Combine(windows, "Fonts", "segoeui.ttf"),
            Path.Combine(windows, "Fonts", "arial.ttf"),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new NativeFfmpegTimelineException("A compatible Windows caption font could not be found.");
    }

    private static string FindLogoPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "assets", "facts_logo.png");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
                directory = directory.Parent;
            }
        }
        throw new NativeFfmpegTimelineException("Logo file does not exist: assets/facts_logo.png");
    }

    private static string WrapCaption(string value, int width)
    {
        var words = (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "";
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return string.Join("\n", lines);
    }

    private static string RemoveEmojis(string value) =>
        string.Join(" ", EmojiPattern.Replace(value ?? "", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string EscapeDrawtext(string value)
    {
        var text = (value ?? "").Trim().Replace("'", "’", StringComparison.Ordinal);
        foreach (var pair in new[]
        {
            ("\\", "\\\\"),
            (":", "\\:"),
            ("%", "\\%"),
            ("[", "\\["),
            ("]", "\\]"),
            (",", "\\,"),
            (";", "\\;"),
        })
        {
            text = text.Replace(pair.Item1, pair.Item2, StringComparison.Ordinal);
        }
        return text.Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string FindExecutable(string name)
    {
        var fileName = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name + ".exe"
            : name;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        throw new NativeFfmpegTimelineException($"{(name.Equals("ffprobe", StringComparison.OrdinalIgnoreCase) ? "FFprobe" : "FFmpeg")} was not found in PATH");
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
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start())
                throw new NativeFfmpegTimelineException($"Could not start {Path.GetFileName(executable)}");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw new NativeFfmpegTimelineException($"{Path.GetFileNameWithoutExtension(executable)} timed out", error);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
        catch (NativeFfmpegTimelineException)
        {
            throw;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new NativeFfmpegTimelineException($"Could not run {Path.GetFileName(executable)}: {error.Message}", error);
        }
    }

    private static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
