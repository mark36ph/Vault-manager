using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public static class QuizAnimatedBackground
{
    public const string TrackName = "Quiz Animated Background";
    public const double LoopSeconds = 12.0;

    public static void RenderAndApply(NativeTimeline timeline, string projectFolder)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        timeline.Validate();
        if (timeline.Duration <= 0)
            throw new InvalidOperationException("Quiz timeline must have a duration before adding the animated background.");

        projectFolder = Path.GetFullPath(projectFolder);
        var mediaFolder = Path.Combine(projectFolder, "Media");
        Directory.CreateDirectory(mediaFolder);
        var theme = ResolveTheme(projectFolder);
        var destination = Path.Combine(mediaFolder, "quiz_animated_background.mp4");

        RenderLoop(destination, mediaFolder, timeline.Width, timeline.Height, timeline.FrameRate, theme);
        ApplyTimeline(timeline, destination, LoopSeconds);
    }

    public static void ApplyTimeline(NativeTimeline timeline, string source, double loopSeconds = LoopSeconds)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (loopSeconds <= 0 || double.IsNaN(loopSeconds) || double.IsInfinity(loopSeconds))
            throw new ArgumentOutOfRangeException(nameof(loopSeconds));
        timeline.Validate();

        var duration = timeline.Duration;
        if (duration <= 0)
            throw new InvalidOperationException("Quiz timeline must have a duration before adding the animated background.");

        source = Path.GetFullPath(source);
        timeline.Tracks.RemoveAll(track =>
            track.Kind == NativeTimelineTrackKind.Video &&
            string.Equals(track.Name, TrackName, StringComparison.Ordinal));

        var background = new NativeTimelineTrack
        {
            Name = TrackName,
            Kind = NativeTimelineTrackKind.Video,
        };

        var start = 0.0;
        var index = 0;
        while (start < duration - 0.0001)
        {
            var segmentDuration = Math.Min(loopSeconds, duration - start);
            background.AddClip(new NativeTimelineClip
            {
                Kind = NativeTimelineClipKind.Video,
                Start = start,
                Duration = segmentDuration,
                Source = source,
                SourceIn = 0,
                Name = $"Animated Background {++index}",
                Metadata = new()
                {
                    ["quiz_background"] = "animated",
                    ["loop_seconds"] = loopSeconds,
                },
            });
            start += segmentDuration;
        }

        timeline.Tracks.Insert(0, background);
        timeline.Metadata["animated_background_applied"] = true;
        timeline.Metadata["animated_background_loop_seconds"] = loopSeconds;
        timeline.Validate();
    }

    private static void RenderLoop(
        string destination,
        string mediaFolder,
        int width,
        int height,
        double frameRate,
        QuizVisualTheme theme)
    {
        var fps = Math.Max(1.0, frameRate);
        var frameCount = Math.Max(1, (int)Math.Round(LoopSeconds * fps, MidpointRounding.ToEven));
        var glowSize = Math.Max(360, (int)Math.Round(Math.Min(width, height) * 0.72));
        var cyanGlow = Path.Combine(mediaFolder, "quiz_glow_cyan.ppm");
        var goldGlow = Path.Combine(mediaFolder, "quiz_glow_gold.ppm");
        var violetGlow = Path.Combine(mediaFolder, "quiz_glow_violet.ppm");

        WriteRadialGlow(cyanGlow, glowSize, theme.Accent);
        WriteRadialGlow(goldGlow, glowSize, theme.Countdown);
        WriteRadialGlow(violetGlow, glowSize, theme.Narration);

        var background = Blend(Blend(theme.Background, Colors.White, 0.20), theme.AccentSoft, 0.18);
        var filter =
            "[1:v]format=rgba,colorkey=0x000000:0.025:0.22,colorchannelmixer=aa=0.46[g1];" +
            "[2:v]format=rgba,colorkey=0x000000:0.025:0.22,colorchannelmixer=aa=0.40[g2];" +
            "[3:v]format=rgba,colorkey=0x000000:0.025:0.22,colorchannelmixer=aa=0.42[g3];" +
            $"[0:v][g1]overlay=x='(W-w)/2+(W-w)*0.44*sin(2*PI*t/{F(LoopSeconds)})':y='(H-h)/2+(H-h)*0.34*cos(2*PI*t/{F(LoopSeconds)})':eval=frame[v1];" +
            $"[v1][g2]overlay=x='(W-w)/2+(W-w)*0.40*sin(2*PI*t/{F(LoopSeconds)}+2.094)':y='(H-h)/2+(H-h)*0.30*cos(2*PI*t/{F(LoopSeconds)}+1.047)':eval=frame[v2];" +
            $"[v2][g3]overlay=x='(W-w)/2+(W-w)*0.42*sin(2*PI*t/{F(LoopSeconds)}+4.189)':y='(H-h)/2+(H-h)*0.32*cos(2*PI*t/{F(LoopSeconds)}+3.142)':eval=frame,format=yuv420p[out]";

        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[]
        {
            "-y",
            "-f", "lavfi",
            "-i", $"color=c={Hex(background)}:s={width}x{height}:r={F(fps)}",
            "-loop", "1", "-framerate", F(fps), "-i", cyanGlow,
            "-loop", "1", "-framerate", F(fps), "-i", goldGlow,
            "-loop", "1", "-framerate", F(fps), "-i", violetGlow,
            "-filter_complex", filter,
            "-map", "[out]",
            "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
            "-r", F(fps),
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "20",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            "-an",
            destination,
        })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
                throw new NativeFfmpegTimelineException("Could not start FFmpeg for the animated quiz background.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(4).TotalMilliseconds))
            {
                try { process.Kill(true); } catch { }
                throw new NativeFfmpegTimelineException("Animated quiz background generation timed out.");
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0 || !File.Exists(destination) || new FileInfo(destination).Length == 0)
            {
                TryDelete(destination);
                var error = stderr.Result.Trim();
                throw new NativeFfmpegTimelineException(
                    "Could not create the animated quiz background:\n" +
                    (error.Length == 0 ? "Unknown FFmpeg error" : error));
            }
        }
        catch (NativeFfmpegTimelineException)
        {
            throw;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new NativeFfmpegTimelineException($"Could not run FFmpeg for the animated quiz background: {error.Message}", error);
        }
        finally
        {
            TryDelete(cyanGlow);
            TryDelete(goldGlow);
            TryDelete(violetGlow);
        }
    }

    private static QuizVisualTheme ResolveTheme(string projectFolder)
    {
        var path = Path.Combine(projectFolder, "quiz.json");
        if (!File.Exists(path))
            return QuizVisualThemeCatalog.Resolve("dark");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("theme", out var value) && value.ValueKind == JsonValueKind.String)
                return QuizVisualThemeCatalog.Resolve(value.GetString());
        }
        catch (JsonException)
        {
        }
        return QuizVisualThemeCatalog.Resolve("dark");
    }

    private static void WriteRadialGlow(string path, int size, Color color)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var header = Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n");
        stream.Write(header);
        var row = new byte[size * 3];
        var center = (size - 1) / 2.0;
        var radius = Math.Max(1.0, size * 0.5);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt((dx * dx) + (dy * dy)) / radius;
                var strength = Math.Pow(Math.Max(0.0, 1.0 - distance), 2.2);
                var offset = x * 3;
                row[offset] = (byte)Math.Round(color.R * strength);
                row[offset + 1] = (byte)Math.Round(color.G * strength);
                row[offset + 2] = (byte)Math.Round(color.B * strength);
            }
            stream.Write(row);
        }
    }

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(left.R + ((right.R - left.R) * amount)),
            (byte)Math.Round(left.G + ((right.G - left.G) * amount)),
            (byte)Math.Round(left.B + ((right.B - left.B) * amount)));
    }

    private static string Hex(Color color) => $"0x{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
