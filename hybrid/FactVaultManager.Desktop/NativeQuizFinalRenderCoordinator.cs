using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FactVaultManager.Desktop;

public sealed class NativeQuizFinalRenderCoordinator
{
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
        var plan = NativeQuizFinalRenderer.CreatePlan(timeline);
        if (plan.AudioClips.Count == 0)
            return new NativeQuizFinalRenderer().Render(timeline, projectFolder, progress, cancellationToken);

        var videoTimeline = timeline.Clone();
        var removedAudioClipIds = videoTimeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Audio)
            .SelectMany(track => track.Clips)
            .Select(clip => clip.Id)
            .ToHashSet(StringComparer.Ordinal);
        videoTimeline.Tracks.RemoveAll(track => track.Kind == NativeTimelineTrackKind.Audio);
        foreach (var scene in videoTimeline.Scenes)
            scene.ClipIds.RemoveAll(id => removedAudioClipIds.Contains(id));
        videoTimeline.Validate();

        var videoResult = new NativeQuizFinalRenderer().Render(
            videoTimeline,
            projectFolder,
            progress,
            cancellationToken);

        var ffmpeg = TrustedMediaExecutableLocator.Find("ffmpeg");
        var working = Path.Combine(projectFolder, ".native-final-audio-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(working);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Mixing narration and sound effects...");
            var audioOnly = Path.Combine(working, "audio.m4a");
            RenderAudio(ffmpeg, plan, audioOnly, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Finishing YouTube-ready MP4...");
            var muxed = Path.Combine(working, NativeQuizFinalRenderer.FinalFileName);
            Mux(ffmpeg, videoResult.VideoPath, audioOnly, muxed, plan.Duration, cancellationToken);
            File.Move(muxed, videoResult.VideoPath, overwrite: true);

            if (!File.Exists(videoResult.VideoPath) || new FileInfo(videoResult.VideoPath).Length == 0)
                throw new NativeFfmpegTimelineException("Final quiz render did not create a usable MP4 file.");

            return videoResult with { HasAudio = true };
        }
        finally
        {
            TryDeleteDirectory(working);
        }
    }

    public static IReadOnlyList<string> BuildAudioFfmpegArguments(
        NativeQuizFinalRenderPlan plan,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (plan.AudioClips.Count == 0)
            throw new ArgumentException("At least one audio clip is required.", nameof(plan));

        var groups = plan.AudioClips
            .GroupBy(clip => Path.GetFullPath(clip.Source!), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var args = new List<string> { "-y" };
        foreach (var group in groups)
            args.AddRange(new[] { "-i", group.Key });

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
                    .Append('[').Append(label).Append("];");
                clipLabels.Add(label);
            }
        }

        foreach (var label in clipLabels)
            filter.Append('[').Append(label).Append(']');
        filter.Append("amix=inputs=").Append(clipLabels.Count)
            .Append(":duration=longest:dropout_transition=0:normalize=0")
            .Append(",alimiter=limit=0.95,apad,atrim=duration=").Append(F(plan.Duration))
            .Append("[mix]");

        // FFmpeg 9 removed -filter_complex_script. Supplying the graph directly via
        // -filter_complex works with both current FFmpeg builds and older supported ones.
        args.AddRange(new[]
        {
            "-filter_complex", filter.ToString(),
            "-map", "[mix]",
            "-t", F(plan.Duration),
            "-c:a", "aac",
            "-b:a", "192k",
            "-ar", "48000",
            "-ac", "2",
            destination,
        });
        return args;
    }

    private static void RenderAudio(
        string ffmpeg,
        NativeQuizFinalRenderPlan plan,
        string destination,
        CancellationToken cancellationToken) =>
        Run(ffmpeg, BuildAudioFfmpegArguments(plan, destination), "final quiz audio mix", cancellationToken);

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

    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Tail(string value, int maximum) =>
        value.Length <= maximum ? value : value[^maximum..];

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
