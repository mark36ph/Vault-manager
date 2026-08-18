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
        var cachePath = QuizSharedAssetCache.BackgroundPath(timeline.Width, timeline.Height, timeline.FrameRate);
        cachePath = QuizSharedAssetCache.GetOrCreate(cachePath, temporary =>
            RenderLoop(temporary, Path.GetDirectoryName(temporary)!, timeline.Width, timeline.Height, timeline.FrameRate, theme));
        QuizSharedAssetCache.CopyToProject(cachePath, destination);

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
        var glowSize = Math.Max(420, (int)Math.Round(Math.Min(width, height) * 0.82));
        var cyan = Color.FromRgb(20, 226, 255);
        var gold = Color.FromRgb(255, 210, 70);
        var magenta = Color.FromRgb(235, 72, 255);
        var cyanGlow = Path.Combine(mediaFolder, "quiz_glow_cyan.ppm");
        var goldGlow = Path.Combine(mediaFolder, "quiz_glow_gold.ppm");
        var violetGlow = Path.Combine(mediaFolder, "quiz_glow_violet.ppm");
        var starburst = Path.Combine(mediaFolder, "quiz_neon_starburst.ppm");

        WriteRadialGlow(cyanGlow, glowSize, cyan);
        WriteRadialGlow(goldGlow, glowSize, gold);
        WriteRadialGlow(violetGlow, glowSize, magenta);
        WriteStarburst(starburst, width, height, cyan, magenta, gold);

        var background = Color.FromRgb(43, 66, 172);
        var filter =
            "[1:v]format=rgba,colorkey=0x000000:0.025:0.22,colorchannelmixer=aa=0.70[g1];" +
            "[2:v]format=rgba,colorkey=0x000000:0.025:0.22,colorchannelmixer=aa=0.54[g2];" +
            "[3:v]format=rgba,colorkey=0x000000:0.025:0.22,colorchannelmixer=aa=0.66[g3];" +
            "[4:v]format=rgba,colorkey=0x000000:0.025:0.16,colorchannelmixer=aa=0.46[burst];" +
            "[0:v][burst]overlay=0:0:eval=frame[v0];" +
            $"[v0][g1]overlay=x='(W-w)/2+(W-w)*0.60*sin(2*PI*t/{F(LoopSeconds)})':y='(H-h)/2+(H-h)*0.48*cos(2*PI*t/{F(LoopSeconds)})':eval=frame[v1];" +
            $"[v1][g2]overlay=x='(W-w)/2+(W-w)*0.56*sin(2*PI*t/{F(LoopSeconds)}+2.094)':y='(H-h)/2+(H-h)*0.44*cos(2*PI*t/{F(LoopSeconds)}+1.047)':eval=frame[v2];" +
            $"[v2][g3]overlay=x='(W-w)/2+(W-w)*0.58*sin(2*PI*t/{F(LoopSeconds)}+4.189)':y='(H-h)/2+(H-h)*0.46*cos(2*PI*t/{F(LoopSeconds)}+3.142)':eval=frame,eq=saturation=1.18:brightness=0.025,format=yuv420p[out]";

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
            "-loop", "1", "-framerate", F(fps), "-i", starburst,
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
            TryDelete(starburst);
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
                var strength = Math.Pow(Math.Max(0.0, 1.0 - distance), 1.75);
                var offset = x * 3;
                row[offset] = (byte)Math.Round(color.R * strength);
                row[offset + 1] = (byte)Math.Round(color.G * strength);
                row[offset + 2] = (byte)Math.Round(color.B * strength);
            }
            stream.Write(row);
        }
    }

    private static void WriteStarburst(string path, int width, int height, Color cyan, Color magenta, Color gold)
    {
        var pixels = new byte[checked(width * height * 3)];
        var centerX = width / 2.0;
        var centerY = height * 0.52;
        var length = Math.Sqrt((width * width) + (height * height));
        var colors = new[] { cyan, magenta, gold, cyan, gold, magenta };

        for (var ray = 0; ray < 30; ray++)
        {
            var angle = ((Math.PI * 2.0) * ray / 30.0) + 0.052;
            var color = colors[ray % colors.Length];
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var perpX = -sin;
            var perpY = cos;
            var thickness = ray % 3 == 0 ? 10 : 6;

            for (var step = 20; step < length; step += 2)
            {
                var fade = Math.Max(0.0, 1.0 - (step / length) * 0.65);
                for (var offset = -thickness; offset <= thickness; offset++)
                {
                    var edgeFade = 1.0 - (Math.Abs(offset) / (double)(thickness + 1));
                    var x = (int)Math.Round(centerX + (cos * step) + (perpX * offset));
                    var y = (int)Math.Round(centerY + (sin * step) + (perpY * offset));
                    if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                        continue;

                    var strength = fade * edgeFade * (ray % 3 == 0 ? 0.82 : 0.58);
                    var index = ((y * width) + x) * 3;
                    pixels[index] = AddLight(pixels[index], color.R, strength);
                    pixels[index + 1] = AddLight(pixels[index + 1], color.G, strength);
                    pixels[index + 2] = AddLight(pixels[index + 2], color.B, strength);
                }
            }
        }

        for (var sparkle = 0; sparkle < 22; sparkle++)
        {
            var angle = ((Math.PI * 2.0) * sparkle / 22.0) + 0.18;
            var radius = Math.Min(width, height) * (0.20 + ((sparkle % 5) * 0.055));
            var x = (int)Math.Round(centerX + Math.Cos(angle) * radius);
            var y = (int)Math.Round(centerY + Math.Sin(angle) * radius);
            var color = colors[sparkle % colors.Length];
            DrawSparkle(pixels, width, height, x, y, sparkle % 3 == 0 ? 8 : 5, color);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        stream.Write(header);
        stream.Write(pixels);
    }

    private static void DrawSparkle(byte[] pixels, int width, int height, int centerX, int centerY, int radius, Color color)
    {
        for (var offset = -radius; offset <= radius; offset++)
        {
            var strength = 1.0 - (Math.Abs(offset) / (double)(radius + 1));
            AddPixel(pixels, width, height, centerX + offset, centerY, color, strength);
            AddPixel(pixels, width, height, centerX, centerY + offset, color, strength);
        }
    }

    private static void AddPixel(byte[] pixels, int width, int height, int x, int y, Color color, double strength)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return;
        var index = ((y * width) + x) * 3;
        pixels[index] = AddLight(pixels[index], color.R, strength);
        pixels[index + 1] = AddLight(pixels[index + 1], color.G, strength);
        pixels[index + 2] = AddLight(pixels[index + 2], color.B, strength);
    }

    private static byte AddLight(byte current, byte light, double strength) =>
        (byte)Math.Min(255, current + Math.Round(light * Math.Clamp(strength, 0, 1)));

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
