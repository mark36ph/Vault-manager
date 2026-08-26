namespace FactVaultManager.Desktop.Tests;

public sealed class NativeQuizFinalRenderCoordinatorTests
{
    [Fact]
    public void BuildAudioFfmpegArguments_UsesSupportedFilterComplexOption()
    {
        var source = Path.Combine(Path.GetTempPath(), "quiz-audio.wav");
        var plan = new NativeQuizFinalRenderPlan(
            BackgroundSource: null,
            ForegroundClips: Array.Empty<NativeTimelineClip>(),
            AudioClips:
            [
                new NativeTimelineClip
                {
                    Kind = NativeTimelineClipKind.Audio,
                    Start = 0.25,
                    Duration = 1.5,
                    Source = source,
                },
                new NativeTimelineClip
                {
                    Kind = NativeTimelineClipKind.Audio,
                    Start = 2.0,
                    Duration = 0.2,
                    Source = source,
                },
            ],
            Duration: 4,
            Width: 1920,
            Height: 1080,
            FrameRate: 30);

        var args = NativeQuizFinalRenderCoordinator.BuildAudioFfmpegArguments(
            plan,
            Path.Combine(Path.GetTempPath(), "audio.m4a"));

        Assert.Contains("-filter_complex", args);
        Assert.DoesNotContain("-filter_complex_script", args);
        var filterIndex = args.ToList().IndexOf("-filter_complex");
        Assert.True(filterIndex >= 0 && filterIndex + 1 < args.Count);
        var filter = args[filterIndex + 1];
        Assert.Contains("asplit=2", filter, StringComparison.Ordinal);
        Assert.Contains("amix=inputs=2", filter, StringComparison.Ordinal);
        Assert.Contains("adelay=250:all=1", filter, StringComparison.Ordinal);
    }
}
