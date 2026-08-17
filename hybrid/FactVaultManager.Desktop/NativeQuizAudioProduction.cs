using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FactVaultManager.Desktop;

public sealed record QuizAudioCue(string Path, double Duration);

public sealed record QuizPreparedBackgroundMusic(
    string Path,
    double Duration,
    bool DuckedForNarration);

public sealed record QuizAudioAssets(
    QuizAudioCue? CountdownTick = null,
    QuizAudioCue? AnswerReveal = null,
    QuizPreparedBackgroundMusic? BackgroundMusic = null);

public sealed record QuizNarrationWindow(double Start, double End)
{
    public double Duration => Math.Max(0, End - Start);
}

public static class QuizVoiceCatalog
{
    private static readonly string[] Voices =
    [
        "alloy",
        "ash",
        "ballad",
        "coral",
        "echo",
        "fable",
        "nova",
        "onyx",
        "sage",
        "shimmer",
        "verse",
        "marin",
        "cedar",
    ];

    public static IReadOnlyList<string> BuiltInVoices => Voices;

    public static string Validate(string? voice)
    {
        var normalized = (voice ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("Quiz voice is required.", nameof(voice));
        if (!Voices.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException($"Unsupported quiz voice: {voice}", nameof(voice));
        return normalized;
    }
}

public static class QuizAudioTimelinePlanner
{
    public static IReadOnlyList<QuizNarrationWindow> BuildNarrationWindows(
        IReadOnlyList<QuizQuestion> questions,
        QuizVideoBuildOptions options,
        IReadOnlyDictionary<int, QuizNarrationAsset> narrationByQuestion)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(narrationByQuestion);
        options.Validate();

        var windows = new List<QuizNarrationWindow>();
        var cursor = 2.0;
        foreach (var question in questions)
        {
            if (narrationByQuestion.TryGetValue(question.Id, out var narration))
            {
                if (narration.Duration <= 0 || double.IsNaN(narration.Duration) || double.IsInfinity(narration.Duration))
                    throw new ArgumentException($"Quiz narration for question #{question.Id} has an invalid duration.", nameof(narrationByQuestion));
                windows.Add(new QuizNarrationWindow(cursor, cursor + narration.Duration));
                cursor += narration.Duration;
            }
            cursor += options.QuestionSeconds + options.AnswerSeconds;
        }
        return windows;
    }
}

public static class QuizAudioCueFactory
{
    private const int SampleRate = 48_000;

    public static QuizAudioCue EnsureCountdownTick(string audioFolder)
    {
        audioFolder = PrepareAudioFolder(audioFolder);
        var path = Path.Combine(audioFolder, "countdown_tick.wav");
        const double duration = 0.14;
        EnsureWave(path, duration, time =>
        {
            var envelope = Math.Exp(-24 * time);
            return 0.42 * envelope * Math.Sin(2 * Math.PI * 960 * time);
        });
        return new QuizAudioCue(path, duration);
    }

    public static QuizAudioCue EnsureAnswerReveal(string audioFolder)
    {
        audioFolder = PrepareAudioFolder(audioFolder);
        var path = Path.Combine(audioFolder, "answer_reveal.wav");
        const double duration = 0.46;
        EnsureWave(path, duration, time =>
        {
            var first = Math.Exp(-8.5 * time) * Math.Sin(2 * Math.PI * 660 * time);
            var secondStart = 0.11;
            var secondTime = Math.Max(0, time - secondStart);
            var second = time >= secondStart
                ? Math.Exp(-7.0 * secondTime) * Math.Sin(2 * Math.PI * 990 * secondTime)
                : 0;
            return 0.30 * first + 0.34 * second;
        });
        return new QuizAudioCue(path, duration);
    }

    private static string PrepareAudioFolder(string audioFolder)
    {
        if (string.IsNullOrWhiteSpace(audioFolder))
            throw new ArgumentException("Quiz audio folder is required.", nameof(audioFolder));
        var full = Path.GetFullPath(audioFolder.Trim());
        Directory.CreateDirectory(full);
        return full;
    }

    private static void EnsureWave(string path, double duration, Func<double, double> sample)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 44)
            return;

        var samples = Math.Max(1, (int)Math.Round(duration * SampleRate, MidpointRounding.AwayFromZero));
        var dataBytes = samples * sizeof(short);
        var temporary = path + ".part";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(SampleRate);
                writer.Write(SampleRate * sizeof(short));
                writer.Write((short)sizeof(short));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);

                for (var index = 0; index < samples; index++)
                {
                    var value = Math.Clamp(sample(index / (double)SampleRate), -1.0, 1.0);
                    writer.Write((short)Math.Round(value * short.MaxValue, MidpointRounding.AwayFromZero));
                }
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}

public static class QuizMusicFile
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus",
    };

    public static string Validate(string? path)
    {
        var text = (path ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException("Choose a background music file first.", nameof(path));
        var full = Path.GetFullPath(text);
        if (!File.Exists(full))
            throw new FileNotFoundException("Quiz background music file was not found.", full);
        if (!SupportedExtensions.Contains(Path.GetExtension(full)))
            throw new InvalidDataException("Quiz background music must be MP3, WAV, M4A, AAC, FLAC, OGG, or OPUS.");
        return full;
    }
}

public sealed class NativeQuizBackgroundMusicRenderer
{
    public async Task<QuizPreparedBackgroundMusic> RenderAsync(
        string source,
        string audioFolder,
        double totalDuration,
        IReadOnlyList<QuizNarrationWindow> narrationWindows,
        CancellationToken cancellationToken = default)
    {
        source = QuizMusicFile.Validate(source);
        if (string.IsNullOrWhiteSpace(audioFolder))
            throw new ArgumentException("Quiz audio folder is required.", nameof(audioFolder));
        if (totalDuration <= 0 || double.IsNaN(totalDuration) || double.IsInfinity(totalDuration))
            throw new ArgumentOutOfRangeException(nameof(totalDuration), "Quiz duration must be greater than zero.");
        ArgumentNullException.ThrowIfNull(narrationWindows);

        audioFolder = Path.GetFullPath(audioFolder.Trim());
        Directory.CreateDirectory(audioFolder);
        var destination = Path.Combine(audioFolder, "background_music.wav");
        var filter = BuildAudioFilter(totalDuration, narrationWindows);
        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-y",
            "-stream_loop", "-1",
            "-i", source,
            "-vn",
            "-filter:a", filter,
            "-t", F(totalDuration),
            "-ar", "48000",
            "-ac", "2",
            "-c:a", "pcm_s16le",
            destination,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new NativeFfmpegTimelineException("Could not start FFmpeg for quiz background music.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new NativeFfmpegTimelineException("FFmpeg timed out while preparing quiz background music.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stderr = await stderrTask;
        _ = await stdoutTask;
        if (process.ExitCode != 0)
        {
            TryDelete(destination);
            throw new NativeFfmpegTimelineException(
                "Could not prepare quiz background music:\n" +
                (string.IsNullOrWhiteSpace(stderr) ? "Unknown FFmpeg error" : stderr.Trim()));
        }
        if (!File.Exists(destination) || new FileInfo(destination).Length <= 44)
            throw new NativeFfmpegTimelineException("Prepared quiz background music was empty.");

        return new QuizPreparedBackgroundMusic(
            destination,
            totalDuration,
            narrationWindows.Count > 0);
    }

    public static string BuildAudioFilter(
        double totalDuration,
        IReadOnlyList<QuizNarrationWindow> narrationWindows)
    {
        if (totalDuration <= 0 || double.IsNaN(totalDuration) || double.IsInfinity(totalDuration))
            throw new ArgumentOutOfRangeException(nameof(totalDuration));
        ArgumentNullException.ThrowIfNull(narrationWindows);

        var filters = new List<string>
        {
            "aresample=48000",
            "aformat=sample_fmts=fltp:channel_layouts=stereo",
            "volume=0.20",
        };
        foreach (var window in narrationWindows)
        {
            if (window.End <= window.Start)
                continue;
            filters.Add($"volume=0.32:enable='between(t,{F(window.Start)},{F(window.End)})'");
        }

        var fade = Math.Min(0.6, totalDuration / 4.0);
        if (fade > 0.02)
        {
            filters.Add($"afade=t=in:st=0:d={F(fade)}");
            filters.Add($"afade=t=out:st={F(Math.Max(0, totalDuration - fade))}:d={F(fade)}");
        }
        return string.Join(',', filters);
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
